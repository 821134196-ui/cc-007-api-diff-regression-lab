namespace RegressionLab.Domain;

public enum RunStatus
{
    Pending = 0,
    Running = 1,
    Completed = 2,
    Cancelled = 3,
    Failed = 4
}

/// <summary>Runtime settings snapshotted onto the run so historical runs stay reproducible.</summary>
public class RunOptions
{
    public int Concurrency { get; set; } = 4;
    public int TimeoutMs { get; set; } = 10_000;

    /// <summary>Max requests per second independently for baseline and candidate targets.</summary>
    public double BaselineRateLimit { get; set; } = 0; // 0 = unlimited
    public double CandidateRateLimit { get; set; } = 0;
}

public class Run
{
    public Guid Id { get; set; }
    public RunStatus Status { get; set; } = RunStatus.Pending;

    /// <summary>Snapshot of the rule set at start time (old runs keep the version they ran with).</summary>
    public Guid RuleSetId { get; set; }
    public int RuleSetVersion { get; set; }
    public string RuleSetName { get; set; } = "";
    public string RuleSnapshotJson { get; set; } = "{}";

    public string BaselineBaseUrl { get; set; } = "";
    public string CandidateBaseUrl { get; set; } = "";

    /// <summary>Snapshot of options (concurrency/timeout/rate limits).</summary>
    public RunOptions Options { get; set; } = new();

    public int TotalScenarios { get; set; }
    public int CompletedScenarios { get; set; }
    public int DiffCount { get; set; }
    public int NetworkFailureCount { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }

    public List<RunResult> Results { get; set; } = new();
}
