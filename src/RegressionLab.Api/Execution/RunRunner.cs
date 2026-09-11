using RegressionLab.Domain;
using RegressionLab.Security;

namespace RegressionLab.Execution;

/// <summary>
/// Orchestrates one run: bounded concurrency, per-target rate limiting, per-request
/// timeout and cooperative cancellation. Completed results are committed incrementally
/// so a cancelled run still keeps everything that finished.
/// </summary>
public sealed class RunRunner
{
    private readonly IRunStore _store;
    private readonly Func<HttpMessageHandler>? _handlerFactory;

    public RunRunner(IRunStore store, Func<HttpMessageHandler>? handlerFactory = null)
    {
        _store = store;
        _handlerFactory = handlerFactory;
    }

    public async Task ExecuteAsync(Guid runId, CancellationToken cancellationToken)
    {
        var plan = await _store.LoadAndMarkRunningAsync(runId, cancellationToken);
        var run = plan.Run;
        var options = run.Options;

        using var baselineGate = new RateGate(options.BaselineRateLimit);
        using var candidateGate = new RateGate(options.CandidateRateLimit);

        using var http = new HttpClient(_handlerFactory?.Invoke() ?? new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        })
        { Timeout = Timeout.InfiniteTimeSpan };
        var requester = new HttpRequester(http);

        var counters = new RunCounters();
        var savedResults = new List<RunResult>();
        using var flushLock = new SemaphoreSlim(1, 1);
        var concurrency = Math.Max(1, options.Concurrency);
        using var slot = new SemaphoreSlim(concurrency, concurrency);

        var tasks = plan.Scenarios.Select(async (scenario, index) =>
        {
            // After cancellation, WaitAsync throws so no NEW request ever starts.
            await slot.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // The slot can be granted in the same instant cancellation fires; re-check
                // before touching the network to keep the "no new requests after cancel" guarantee.
                if (cancellationToken.IsCancellationRequested) return;
                RunResult result;
                try
                {
                    result = await ExecuteScenarioAsync(
                        scenario, index, plan.Rule, run, requester,
                        baselineGate, candidateGate, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // Dequeued but cancelled before/during the call: drop without saving.
                    return;
                }

                await flushLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                try
                {
                    counters.Completed++;
                    if (result.Outcome is OutcomeKind.Diff) counters.Diffs++;
                    if (result.Outcome is OutcomeKind.NetworkFailure) counters.NetworkFailures++;
                    savedResults.Add(result);
                    await _store.AppendResultAsync(runId, result, counters).ConfigureAwait(false);
                }
                finally { flushLock.Release(); }
            }
            finally { slot.Release(); }
        }).ToList();

        try { await Task.WhenAll(tasks); }
        catch (OperationCanceledException) { /* expected on cancel; keep finished work */ }

        var finalStatus = cancellationToken.IsCancellationRequested
            ? RunStatus.Cancelled
            : RunStatus.Completed;
        await _store.FinishAsync(runId, finalStatus, counters).ConfigureAwait(false);
    }

    private async Task<RunResult> ExecuteScenarioAsync(
        Scenario scenario,
        int index,
        Comparison.RuleSettings rule,
        Run run,
        HttpRequester requester,
        RateGate baselineGate,
        RateGate candidateGate,
        CancellationToken runToken)
    {
        var result = new RunResult
        {
            Id = Guid.NewGuid(),
            RunId = run.Id,
            ScenarioId = scenario.Id,
            ScenarioName = scenario.Name,
            OrderIndex = index,
            CompletedAt = DateTime.UtcNow
        };

        TargetCall baselineCall;
        TargetCall candidateCall;
        SecretResolver? resolver = null;

        try
        {
            resolver = new SecretResolver(knownRefs: scenario.SecretRefs);
            var factory = new RequestFactory(resolver);

            using var baseReq = factory.Build(scenario, new Uri(run.BaselineBaseUrl));
            using var candReq = factory.Build(scenario, new Uri(run.CandidateBaseUrl));

            // Fire both targets concurrently, each behind its own rate limiter.
            await Task.WhenAll(
                baselineGate.WaitAsync(runToken),
                candidateGate.WaitAsync(runToken)).ConfigureAwait(false);
            var baseTask = requester.SendAsync(baseReq, run.Options.TimeoutMs, runToken);
            var candTask = requester.SendAsync(candReq, run.Options.TimeoutMs, runToken);
            await Task.WhenAll(baseTask, candTask).ConfigureAwait(false);
            baselineCall = baseTask.Result;
            candidateCall = candTask.Result;
        }
        catch (SecretNotFoundException ex)
        {
            result.Outcome = OutcomeKind.RequestError;
            result.Summary = $"missing secret: {ex.Name}";
            return result;
        }
        catch (UnauthorizedSecretException ex)
        {
            result.Outcome = OutcomeKind.RequestError;
            result.Summary = $"undeclared secret reference: {ex.Name}";
            return result;
        }
        catch (InvalidScenarioException ex)
        {
            result.Outcome = OutcomeKind.RequestError;
            result.Summary = ex.Message;
            return result;
        }

        // Redact concrete secret values from persisted responses BEFORE comparison:
        // equal values mask equally, so redaction cannot manufacture false diffs.
        SecretRedactor.Apply(baselineCall, resolver!.ResolvedValues);
        SecretRedactor.Apply(candidateCall, resolver.ResolvedValues);

        result.Baseline = baselineCall;
        result.Candidate = candidateCall;

        var (outcome, diffEntries, summary) =
            new ResponseComparer(rule).Compare(baselineCall, candidateCall);
        result.Outcome = outcome;
        result.Diffs = diffEntries;
        result.Summary = summary;
        return result;
    }
}
