using Microsoft.EntityFrameworkCore;
using RegressionLab.Comparison;
using RegressionLab.Data;
using RegressionLab.Domain;

namespace RegressionLab.Execution;

public sealed class EfRunStore : IRunStore
{
    private readonly IDbContextFactory<LabDbContext> _dbFactory;

    public EfRunStore(IDbContextFactory<LabDbContext> dbFactory) => _dbFactory = dbFactory;

    public async Task<RunPlan> LoadAndMarkRunningAsync(Guid runId, CancellationToken ct)
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync(ct);
        var run = await ctx.Runs.FirstAsync(r => r.Id == runId, ct);
        var scenarios = await ctx.Scenarios
            .Where(s => s.Enabled && s.RuleSetId == run.RuleSetId)
            .OrderBy(s => s.Name)
            .ToListAsync(ct);
        var rule = RuleSettingsFactory.FromSnapshot(run.RuleSnapshotJson);

        run.Status = RunStatus.Running;
        run.StartedAt = DateTime.UtcNow;
        run.TotalScenarios = scenarios.Count;
        await ctx.SaveChangesAsync(ct);

        return new RunPlan(run, scenarios, rule);
    }

    public async Task AppendResultAsync(Guid runId, RunResult result, RunCounters counters)
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync();
        ctx.RunResults.Add(result);
        await ctx.SaveChangesAsync();
        await ctx.Database.ExecuteSqlInterpolatedAsync(
            $@"UPDATE ""Runs"" SET ""CompletedScenarios"" = {counters.Completed},
                   ""DiffCount"" = {counters.Diffs},
                   ""NetworkFailureCount"" = {counters.NetworkFailures}
               WHERE ""Id"" = {runId}");
    }

    public async Task FinishAsync(Guid runId, RunStatus status, RunCounters counters)
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync();
        var run = await ctx.Runs.FirstAsync(r => r.Id == runId);
        run.Status = status;
        run.CompletedScenarios = counters.Completed;
        run.DiffCount = counters.Diffs;
        run.NetworkFailureCount = counters.NetworkFailures;
        run.FinishedAt = DateTime.UtcNow;
        await ctx.SaveChangesAsync();
    }
}
