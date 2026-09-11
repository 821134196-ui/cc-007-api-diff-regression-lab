using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using RegressionLab.Data;
using RegressionLab.Execution;

namespace RegressionLab.Services;

public interface IRunQueue
{
    void Enqueue(Guid runId);
    bool Cancel(Guid runId);
    bool IsCancelled(Guid runId);
}

/// <summary>
/// Runs execute one at a time (queued); each gets its own cancellable token.
/// Cancelling marks the token and never starts queued items that were cancelled
/// while waiting.
/// </summary>
public sealed class RunQueueService : BackgroundService, IRunQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _tokens = new();
    private readonly IServiceProvider _services;
    private readonly ILogger<RunQueueService> _logger;

    public RunQueueService(IServiceProvider services, ILogger<RunQueueService> logger)
    {
        _services = services;
        _logger = logger;
    }

    public void Enqueue(Guid runId)
    {
        var cts = new CancellationTokenSource();
        if (!_tokens.TryAdd(runId, cts))
            throw new InvalidOperationException($"Run {runId} is already queued/running");
        _channel.Writer.TryWrite(runId);
    }

    public bool Cancel(Guid runId)
    {
        if (_tokens.TryGetValue(runId, out var cts))
        {
            cts.Cancel();
            return true;
        }
        return false;
    }

    public bool IsCancelled(Guid runId) =>
        _tokens.TryGetValue(runId, out var cts) && cts.IsCancellationRequested;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var runId in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            if (!_tokens.TryGetValue(runId, out var cts)) continue;
            if (cts.IsCancellationRequested)
            {
                // Cancelled while queued: mark Cancelled without launching any request.
                await MarkCancelledAsync(runId);
                _tokens.TryRemove(runId, out _);
                continue;
            }

            try
            {
                using var scope = _services.CreateScope();
                var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<LabDbContext>>();
                var runner = new RunRunner(new EfRunStore(factory));
                await runner.ExecuteAsync(runId, cts.Token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Run {RunId} failed", runId);
                await TryFailAsync(runId, ex.Message);
            }
            finally
            {
                _tokens.TryRemove(runId, out _);
                cts.Dispose();
            }
        }
    }

    private async Task MarkCancelledAsync(Guid runId)
    {
        try
        {
            using var scope = _services.CreateScope();
            await using var ctx = await scope.ServiceProvider
                .GetRequiredService<IDbContextFactory<LabDbContext>>().CreateDbContextAsync();
            var run = await ctx.Runs.FindAsync(runId);
            if (run is not null && run.Status is Domain.RunStatus.Pending)
            {
                run.Status = Domain.RunStatus.Cancelled;
                run.FinishedAt = DateTime.UtcNow;
                await ctx.SaveChangesAsync();
            }
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to cancel queued run"); }
    }

    private async Task TryFailAsync(Guid runId, string message)
    {
        try
        {
            using var scope = _services.CreateScope();
            await using var ctx = await scope.ServiceProvider
                .GetRequiredService<IDbContextFactory<LabDbContext>>().CreateDbContextAsync();
            var run = await ctx.Runs.FindAsync(runId);
            if (run is not null && run.Status is not Domain.RunStatus.Completed)
            {
                run.Status = Domain.RunStatus.Failed;
                run.FinishedAt = DateTime.UtcNow;
                await ctx.SaveChangesAsync();
            }
        }
        catch { /* best effort */ }
    }
}
