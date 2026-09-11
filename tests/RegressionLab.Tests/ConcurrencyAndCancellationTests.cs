using System.Diagnostics;
using System.Net;
using RegressionLab.Comparison;
using RegressionLab.Domain;
using RegressionLab.Execution;
using Xunit;

namespace RegressionLab.Tests;

public class ConcurrencyAndCancellationTests
{
    [Fact]
    public async Task Concurrency_is_bounded_and_both_targets_called_in_parallel()
    {
        const int slots = 3;
        var gate = new TaskCompletionSource();
        var handler = new FakeHttpHandler
        {
            Baseline = async (req, ct) =>
            {
                await gate.Task;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"ok":true}""",
                        System.Text.Encoding.UTF8, "application/json")
                };
            },
            Candidate = async (req, ct) =>
            {
                await gate.Task;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"ok":true}""",
                        System.Text.Encoding.UTF8, "application/json")
                };
            }
        };

        var scenarios = Enumerable.Range(0, 9)
            .Select(i => ScenarioBuilder.Scenario($"s{i}", $"/p{i}"))
            .ToList();
        var run = ScenarioBuilder.Run(new RunOptions
        {
            Concurrency = slots, TimeoutMs = 10_000
        });
        var store = new MemoryRunStore(run, scenarios, TestRules.Create());
        var runner = new RunRunner(store, () => handler);

        var runTask = runner.ExecuteAsync(run.Id, CancellationToken.None);

        // 3 scenarios × 2 sides = 6 in flight, never more.
        var sw = Stopwatch.StartNew();
        while (handler.ObservedMaxInflight < 6 && sw.Elapsed < TimeSpan.FromSeconds(5))
            await Task.Delay(10);
        Assert.Equal(6, handler.ObservedMaxInflight);
        Assert.Equal(0, store.Results.Count); // nothing completes while gated

        gate.SetResult();
        await runTask;

        Assert.Equal(9, store.Results.Count);
        Assert.Equal(RunStatus.Completed, store.FinalStatus);
        Assert.All(store.Results, r => Assert.Equal(OutcomeKind.Match, r.Outcome));
    }

    [Fact]
    public async Task Cancel_stops_new_requests_but_keeps_completed_results()
    {
        const int slots = 2;
        var firstBatch = new TaskCompletionSource();

        // Calls 0..3 (scenarios 0,1) block until released; later calls block on
        // a cancellation-aware infinite delay.
        var callIndex = 0;
        var handler = new FakeHttpHandler();
        handler.Baseline = Responder;
        handler.Candidate = Responder;

        Task<HttpResponseMessage> Responder(HttpRequestMessage req, CancellationToken ct)
        {
            var idx = Interlocked.Increment(ref callIndex) - 1;
            return idx < 4 ? FirstBatch() : Blocked(ct);
        }

        async Task<HttpResponseMessage> FirstBatch()
        {
            await firstBatch.Task;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"ok":true}""",
                    System.Text.Encoding.UTF8, "application/json")
            };
        }

        static async Task<HttpResponseMessage> Blocked(CancellationToken ct)
        {
            // Responds to the run token being cancelled.
            await Task.Delay(Timeout.Infinite, ct);
            return null!;
        }

        var scenarios = Enumerable.Range(0, 6)
            .Select(i => ScenarioBuilder.Scenario($"s{i}", $"/p{i}"))
            .ToList();
        var run = ScenarioBuilder.Run(new RunOptions { Concurrency = slots, TimeoutMs = 30_000 });
        var store = new MemoryRunStore(run, scenarios, TestRules.Create());
        var runner = new RunRunner(store, () => handler);
        using var cts = new CancellationTokenSource();

        var runTask = runner.ExecuteAsync(run.Id, cts.Token);

        // Wait for the first batch (2 scenarios × 2 sides).
        var sw = Stopwatch.StartNew();
        while (Volatile.Read(ref callIndex) < 4 && sw.Elapsed < TimeSpan.FromSeconds(5))
            await Task.Delay(5);
        Assert.Equal(4, Volatile.Read(ref callIndex));

        firstBatch.SetResult();
        while (store.Results.Count < 2 && sw.Elapsed < TimeSpan.FromSeconds(5))
            await Task.Delay(5);
        Assert.Equal(2, store.Results.Count);

        // Cancel: scenarios in flight may abort, queued ones must not start.
        cts.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(RunStatus.Cancelled, store.FinalStatus);
        Assert.Equal(2, store.FinalCounters!.Completed);
        Assert.Equal(2, store.Results.Count); // finished work retained

        // Slots freed by cancellation: at most scenarios 0..3 could have touched
        // the network; s4 (/p4) and s5 (/p5) must never have been called.
        lock (handler.Requests)
        {
            Assert.DoesNotContain(handler.Requests, r => r.Uri.Contains("/p4"));
            Assert.DoesNotContain(handler.Requests, r => r.Uri.Contains("/p5"));
        }
    }

    [Fact]
    public async Task Per_request_timeout_yields_network_failure_not_empty_response()
    {
        var handler = new FakeHttpHandler
        {
            Baseline = async (req, ct) =>
            {
                await Task.Delay(500, ct);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"slow":true}""",
                        System.Text.Encoding.UTF8, "application/json")
                };
            },
            Candidate = (req, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"slow":true}""",
                    System.Text.Encoding.UTF8, "application/json")
            })
        };

        var run = ScenarioBuilder.Run(new RunOptions { Concurrency = 1, TimeoutMs = 100 });
        var store = new MemoryRunStore(run, new() { ScenarioBuilder.Scenario() }, TestRules.Create());
        await new RunRunner(store, () => handler).ExecuteAsync(run.Id, CancellationToken.None);

        var result = Assert.Single(store.Results);
        Assert.Equal(OutcomeKind.NetworkFailure, result.Outcome);
        Assert.False(result.Baseline.Reached);
        Assert.Equal("timeout", result.Baseline.ErrorKind);
        Assert.True(result.Candidate.Reached);
        Assert.Equal(200, result.Candidate.StatusCode);
        // The failure is recorded as reachability, not a missing/empty body.
        Assert.Contains(result.Diffs, d => d.Area == DiffArea.Reachability && d.Path == "baseline");
        Assert.DoesNotContain(result.Diffs, d => d.Kind == "missing");
    }

    [Fact]
    public async Task Per_target_rate_limiter_spaces_requests()
    {
        const double rps = 10;
        var handler = new FakeHttpHandler();
        var scenarios = Enumerable.Range(0, 4)
            .Select(i => ScenarioBuilder.Scenario($"s{i}", $"/r{i}"))
            .ToList();
        var run = ScenarioBuilder.Run(new RunOptions
        {
            Concurrency = 8,
            TimeoutMs = 10_000,
            BaselineRateLimit = rps,
            CandidateRateLimit = rps
        });
        var store = new MemoryRunStore(run, scenarios, TestRules.Create());

        var sw = Stopwatch.StartNew();
        await new RunRunner(store, () => handler).ExecuteAsync(run.Id, CancellationToken.None);
        sw.Stop();

        // 4 candidate calls @10 rps -> t = 0, .1, .2, .3 -> >= 300 ms total.
        var candidateStarts = handler.Requests
            .Where(r => r.Uri.Contains("candidate"))
            .OrderBy(x => 0)
            .ToList();
        Assert.Equal(4, candidateStarts.Count);
        Assert.True(sw.Elapsed >= TimeSpan.FromMilliseconds(290),
            $"rate limit not respected, elapsed={sw.Elapsed}");
    }

    [Fact]
    public async Task Request_errors_do_not_kill_the_run()
    {
        Environment.SetEnvironmentVariable("RLAB_CONCURRENCY_MISSING", null);
        var scenarios = new List<Scenario>
        {
            ScenarioBuilder.Scenario("good", "/good"),
            ScenarioBuilder.Scenario("bad-secret", "/x",
                headers: new() { ["X-Api-Key"] = "{{secret:RLAB_CONCURRENCY_MISSING}}" },
                secretRefs: new() { "RLAB_CONCURRENCY_MISSING" }),
            ScenarioBuilder.Scenario("also-good", "/good2")
        };
        var run = ScenarioBuilder.Run();
        var store = new MemoryRunStore(run, scenarios, TestRules.Create());
        await new RunRunner(store, () => new FakeHttpHandler())
            .ExecuteAsync(run.Id, CancellationToken.None);

        Assert.Equal(3, store.Results.Count);
        Assert.Contains(store.Results, r => r.Outcome == OutcomeKind.RequestError);
        Assert.Equal(RunStatus.Completed, store.FinalStatus);
    }
}
