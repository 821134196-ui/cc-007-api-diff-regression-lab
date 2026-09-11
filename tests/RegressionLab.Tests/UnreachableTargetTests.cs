using System.Net;
using System.Net.Sockets;
using RegressionLab.Comparison;
using RegressionLab.Domain;
using RegressionLab.Execution;
using Xunit;

namespace RegressionLab.Tests;

public class UnreachableTargetTests
{
    internal static int FreeTcpPort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    [Fact]
    public async Task Connection_refused_is_classified_as_connect_refused()
    {
        var port = FreeTcpPort(); // nothing listening
        using var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        var requester = new HttpRequester(client);

        using var req = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}/health");
        var call = await requester.SendAsync(req, 3000, CancellationToken.None);

        Assert.False(call.Reached);
        Assert.Null(call.StatusCode);
        Assert.Equal("connect_refused", call.ErrorKind);
        Assert.Null(call.BodyText);
    }

    [Fact]
    public void Dns_failure_is_classified_as_dns_error()
    {
        // A name-resolution socket exception is exactly what a direct resolver raises;
        // assert the mapping on the classifier (sandboxed CI proxies would mask an
        // end-to-end DNS failure as a transport reset).
        var dnsEx = new HttpRequestException("name resolution failed",
            new SocketException((int)SocketError.HostNotFound));
        var (kind, _) = HttpRequester.Classify(dnsEx);
        Assert.Equal("dns_error", kind);

        var refusedEx = new HttpRequestException("connection refused",
            new SocketException((int)SocketError.ConnectionRefused));
        Assert.Equal("connect_refused", HttpRequester.Classify(refusedEx).Kind);
    }

    [Fact]
    public async Task Server_that_accepts_but_never_responds_times_out()
    {
        // Black-hole listener: accept TCP connections, never write an HTTP response.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(10);
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var accepting = Task.Run(async () =>
        {
            while (true)
            {
                try { await listener.AcceptTcpClientAsync(); }
                catch { break; }
            }
        });

        try
        {
            using var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            var requester = new HttpRequester(client);
            using var req = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}/hang");

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var call = await requester.SendAsync(req, 300, CancellationToken.None);
            sw.Stop();

            Assert.False(call.Reached);
            Assert.Equal("timeout", call.ErrorKind);
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), "timeout was not enforced");
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task Runner_distinguishes_unreachable_candidate_from_a_real_diff()
    {
        // Baseline answers on a local HttpListener; candidate points at a closed port.
        using var baselineServer = new LocalHttpServer(async ctx =>
        {
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            var bytes = System.Text.Encoding.UTF8.GetBytes("""{"status":"ok"}""");
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes);
            ctx.Response.Close();
        });
        baselineServer.Start();
        var deadPort = FreeTcpPort();

        var run = ScenarioBuilder.Run(
            baseline: $"http://127.0.0.1:{baselineServer.Port}",
            candidate: $"http://127.0.0.1:{deadPort}");
        var store = new MemoryRunStore(run, new() { ScenarioBuilder.Scenario() }, TestRules.Create());
        await new RunRunner(store).ExecuteAsync(run.Id, CancellationToken.None);

        var result = Assert.Single(store.Results);
        Assert.Equal(OutcomeKind.NetworkFailure, result.Outcome);
        Assert.True(result.Baseline.Reached);
        Assert.Equal(200, result.Baseline.StatusCode);
        Assert.False(result.Candidate.Reached);
        Assert.Equal("connect_refused", result.Candidate.ErrorKind);

        // Reachability is its own diff area — no fabricated body/empty-response diffs.
        var reach = Assert.Single(result.Diffs, d => d.Area == DiffArea.Reachability);
        Assert.Equal("candidate", reach.Path);
        Assert.DoesNotContain(result.Diffs, d => d.Area == DiffArea.Body);
        Assert.Equal(1, store.FinalCounters!.NetworkFailures);
        Assert.Equal(0, store.FinalCounters.Diffs);
    }

    [Fact]
    public async Task Real_200_with_empty_body_is_NOT_a_network_failure()
    {
        using var server = new LocalHttpServer(ctx =>
        {
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentLength64 = 0;
            ctx.Response.Close();
            return Task.CompletedTask;
        });
        server.Start();
        var run = ScenarioBuilder.Run(
            baseline: $"http://127.0.0.1:{server.Port}",
            candidate: $"http://127.0.0.1:{server.Port}");
        var store = new MemoryRunStore(run, new() { ScenarioBuilder.Scenario() }, TestRules.Create());
        await new RunRunner(store).ExecuteAsync(run.Id, CancellationToken.None);

        var result = Assert.Single(store.Results);
        Assert.True(result.Baseline.Reached);
        Assert.True(result.Candidate.Reached);
        Assert.Equal(200, result.Baseline.StatusCode);
        Assert.Equal(OutcomeKind.Match, result.Outcome);
        Assert.Equal(0, store.FinalCounters!.NetworkFailures);
    }
}

/// <summary>Minimal localhost HTTP server built on HttpListener for reachable-target tests.</summary>
internal sealed class LocalHttpServer : IDisposable
{
    private readonly Func<HttpListenerContext, Task> _handle;
    private HttpListener? _listener;
    private volatile bool _stopping;

    public int Port { get; private set; }

    public LocalHttpServer(Func<HttpListenerContext, Task> handle) => _handle = handle;

    public void Start()
    {
        _listener = new HttpListener();
        // Prefixes don't support port 0, so reserve a free TCP port first.
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        Port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
        _listener.Start();
        _ = Task.Run(Loop);
    }

    private async Task Loop()
    {
        while (!_stopping)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener!.GetContextAsync(); }
            catch { return; }
            try { await _handle(ctx); }
            catch { try { ctx.Response.Abort(); } catch { } }
        }
    }

    public void Dispose()
    {
        _stopping = true;
        try { _listener?.Close(); } catch { }
    }
}
