using System.Net;
using RegressionLab.Comparison;
using RegressionLab.Domain;
using RegressionLab.Execution;

namespace RegressionLab.Tests;

/// <summary>Routes fake responses by host: baseline:8080 / candidate:8080.</summary>
internal sealed class FakeHttpHandler : HttpMessageHandler
{
    private static readonly object _gate = new();
    public int ObservedMaxInflight => _maxInflight;
    private int _inflight;
    private int _maxInflight;
    public List<DateTimeOffset> StartTimes { get; } = new();
    public List<(string Method, string Uri, string? Body, string? ApiKey)> Requests { get; } = new();

    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Baseline { get; set; }
        = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"ok":true}""",
                System.Text.Encoding.UTF8, "application/json")
        });

    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Candidate { get; set; }
        = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"ok":true}""",
                System.Text.Encoding.UTF8, "application/json")
        });

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var n = Interlocked.Increment(ref _inflight);
        lock (_gate) { if (n > _maxInflight) _maxInflight = n; }
        lock (StartTimes) StartTimes.Add(DateTimeOffset.UtcNow);
        try
        {
            var body = request.Content is not null
                ? await request.Content.ReadAsStringAsync(cancellationToken)
                : null;
            lock (Requests)
                Requests.Add((request.Method.ToString(),
                    request.RequestUri!.ToString(), body,
                    request.Headers.TryGetValues("X-Api-Key", out var v) ? string.Join(",", v) : null));

            var responder = request.RequestUri!.Host == "candidate" ? Candidate : Baseline;
            return await responder(request, cancellationToken);
        }
        finally { Interlocked.Decrement(ref _inflight); }
    }
}

internal sealed class MemoryRunStore : IRunStore
{
    private readonly Run _run;
    private readonly List<Scenario> _scenarios;
    private readonly RuleSettings _rule;

    public List<RunResult> Results { get; } = new();
    public RunStatus? FinalStatus { get; private set; }
    public RunCounters? FinalCounters { get; private set; }

    public MemoryRunStore(Run run, List<Scenario> scenarios, RuleSettings rule)
    {
        _run = run; _scenarios = scenarios; _rule = rule;
    }

    public Task<RunPlan> LoadAndMarkRunningAsync(Guid runId, CancellationToken ct)
    {
        _run.Status = RunStatus.Running;
        return Task.FromResult(new RunPlan(_run, _scenarios, _rule));
    }

    public Task AppendResultAsync(Guid runId, RunResult result, RunCounters counters)
    {
        Results.Add(result);
        return Task.CompletedTask;
    }

    public Task FinishAsync(Guid runId, RunStatus status, RunCounters counters)
    {
        FinalStatus = status;
        FinalCounters = counters;
        return Task.CompletedTask;
    }
}

internal static class ScenarioBuilder
{
    public static Scenario Scenario(
        string name = "s",
        string path = "/ping",
        HttpMethodCode method = HttpMethodCode.GET,
        Dictionary<string, string>? headers = null,
        string? body = null,
        List<string>? secretRefs = null,
        Dictionary<string, string>? pathParams = null) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Method = method,
        PathTemplate = path,
        Headers = headers ?? new(),
        JsonBody = body,
        SecretRefs = secretRefs ?? new(),
        PathParameters = pathParams ?? new(),
        QueryParameters = new(),
        Enabled = true
    };

    public static Run Run(
        RunOptions? options = null,
        string baseline = "http://baseline:8080",
        string candidate = "http://candidate:8080",
        RuleSet? ruleSet = null)
    {
        ruleSet ??= new RuleSet { Version = 1 };
        return new Domain.Run
        {
            Id = Guid.NewGuid(),
            BaselineBaseUrl = baseline,
            CandidateBaseUrl = candidate,
            Options = options ?? new RunOptions { Concurrency = 4, TimeoutMs = 10_000 },
            RuleSnapshotJson = RuleSettingsFactory.ToSnapshot(ruleSet)
        };
    }
}
