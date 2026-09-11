using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using RegressionLab.Domain;

namespace RegressionLab.Execution;

/// <summary>Min-interval rate limiter (0 = unlimited). One instance per target side.</summary>
public sealed class RateGate : IDisposable
{
    private readonly long _minIntervalTicks;
    private readonly SemaphoreSlim _mutex = new(1, 1);
    private long _nextAllowedTick;
    private readonly Stopwatch _clock;

    public RateGate(double maxPerSecond)
    {
        _minIntervalTicks = maxPerSecond > 0
            ? (long)(Stopwatch.Frequency / maxPerSecond)
            : 0;
        _clock = Stopwatch.StartNew();
        _nextAllowedTick = 0;
    }

    public async Task WaitAsync(CancellationToken ct)
    {
        if (_minIntervalTicks == 0) return;
        await _mutex.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var now = _clock.ElapsedTicks;
            var waitTicks = _nextAllowedTick - now;
            if (waitTicks > 0)
            {
                await Task.Delay((int)Math.Ceiling(waitTicks * 1000.0 / Stopwatch.Frequency), ct)
                    .ConfigureAwait(false);
                now = _clock.ElapsedTicks;
            }
            _nextAllowedTick = Math.Max(now, _nextAllowedTick) + _minIntervalTicks;
        }
        finally { _mutex.Release(); }
    }

    public void Dispose() => _mutex.Dispose();
}

/// <summary>Executes one HTTP call and classifies transport failures distinctly from responses.</summary>
public sealed class HttpRequester
{
    private readonly HttpClient _client;

    public HttpRequester(HttpClient client) => _client = client;

    public async Task<TargetCall> SendAsync(
        HttpRequestMessage request, int timeoutMs, CancellationToken runToken)
    {
        var call = new TargetCall();
        var sw = Stopwatch.StartNew();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(runToken);
        cts.CancelAfter(timeoutMs);

        try
        {
            using var resp = await _client.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);

            string body;
            using (var bodyCts = CancellationTokenSource.CreateLinkedTokenSource(runToken))
            {
                bodyCts.CancelAfter(timeoutMs);
                body = await resp.Content.ReadAsStringAsync(bodyCts.Token).ConfigureAwait(false);
            }
            sw.Stop();

            call.Reached = true;
            call.StatusCode = (int)resp.StatusCode;
            call.ElapsedMs = sw.ElapsedMilliseconds;
            call.BodyText = body;
            call.BodyIsJson = IsJson(resp.Content.Headers.ContentType?.MediaType, body);

            foreach (var h in resp.Headers)
                call.Headers[h.Key.ToLowerInvariant()] = string.Join(",", h.Value);
            foreach (var h in resp.Content.Headers)
                call.Headers[h.Key.ToLowerInvariant()] = string.Join(",", h.Value);

            return call;
        }
        catch (OperationCanceledException) when (!runToken.IsCancellationRequested)
        {
            sw.Stop();
            call.Reached = false;
            call.ElapsedMs = sw.ElapsedMilliseconds;
            call.ErrorKind = "timeout";
            call.ErrorDetail = $"No response within {timeoutMs} ms";
            return call;
        }
        catch (HttpRequestException ex)
        {
            sw.Stop();
            call.Reached = false;
            call.ElapsedMs = sw.ElapsedMilliseconds;
            (call.ErrorKind, call.ErrorDetail) = Classify(ex);
            return call;
        }
        // runToken cancellation propagates as OperationCanceledException to the caller.
    }

    public static (string Kind, string Detail) Classify(Exception ex)
    {
        var current = ex;
        while (current is not null)
        {
            if (current is SocketException se)
            {
                return se.SocketErrorCode switch
                {
                    SocketError.ConnectionRefused => ("connect_refused", "TCP connection refused"),
                    SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain
                        => ("dns_error", $"DNS resolution failed: {se.Message}"),
                    SocketError.TimedOut => ("timeout", se.Message),
                    SocketError.NetworkUnreachable or SocketError.HostUnreachable
                        => ("network_unreachable", se.Message),
                    _ => ("transport_error", se.Message)
                };
            }
            if (current is HttpIOException hioe)
            {
                // Covers proxy/DNS-induced "response ended prematurely", protocol errors, etc.
                return hioe.HttpRequestError == HttpRequestError.NameResolutionError
                    ? ("dns_error", hioe.Message)
                    : ("transport_error", $"{hioe.HttpRequestError}: {hioe.Message}");
            }
            if (current is System.ComponentModel.Win32Exception)
                return ("transport_error", current.Message);
            current = current.InnerException;
        }

        var msg = ex.Message ?? "";
        if (msg.Contains("NameResolution", StringComparison.OrdinalIgnoreCase))
            return ("dns_error", msg);
        if (msg.Contains("SSL", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("TLS", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("authentication", StringComparison.OrdinalIgnoreCase))
            return ("tls_error", msg);
        return ("other", msg);
    }

    private static bool IsJson(string? mediaType, string body)
    {
        if (mediaType is not null &&
            (mediaType.Contains("json", StringComparison.OrdinalIgnoreCase)))
            return true;
        if (string.IsNullOrWhiteSpace(body)) return false;
        var trimmed = body.TrimStart();
        return trimmed.StartsWith('{') || trimmed.StartsWith('[');
    }
}
