using RegressionLab.Comparison;
using RegressionLab.Domain;

namespace RegressionLab.Execution;

public sealed record RunPlan(Run Run, List<Scenario> Scenarios, RuleSettings Rule);

public sealed class RunCounters
{
    public int Completed;
    public int Diffs;
    public int NetworkFailures;
}

/// <summary>Persistence seam so the runner can be unit-tested without a database.</summary>
public interface IRunStore
{
    Task<RunPlan> LoadAndMarkRunningAsync(Guid runId, CancellationToken ct);
    Task AppendResultAsync(Guid runId, RunResult result, RunCounters counters);
    Task FinishAsync(Guid runId, RunStatus status, RunCounters counters);
}
