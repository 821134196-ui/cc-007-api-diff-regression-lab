namespace RegressionLab.Domain;

public enum OutcomeKind
{
    /// <summary>Both targets responded and responses are equivalent under the rule set.</summary>
    Match = 0,

    /// <summary>Both targets responded but at least one of status/header/body differs.</summary>
    Diff = 1,

    /// <summary>
    /// At least one target could not be reached (DNS/connect/timeout) or replied with a
    /// non-HTTP transport error. Never confused with an empty 200 response.
    /// </summary>
    NetworkFailure = 2,

    /// <summary>The request itself was invalid (bad template, missing secret, invalid JSON body).</summary>
    RequestError = 3
}

public enum TargetSide
{
    Baseline = 0,
    Candidate = 1
}

/// <summary>How an individual target call ended.</summary>
public class TargetCall
{
    public bool Reached { get; set; }
    public int? StatusCode { get; set; }

    /// <summary>"OK" or a machine-readable failure reason: dns_error / connect_refused / timeout / tls / other.</summary>
    public string? ErrorKind { get; set; }
    public string? ErrorDetail { get; set; }

    public Dictionary<string, string> Headers { get; set; } = new();
    public string? BodyText { get; set; }
    public bool BodyIsJson { get; set; }

    public long ElapsedMs { get; set; }
}

public class RunResult
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }

    public Guid ScenarioId { get; set; }
    public string ScenarioName { get; set; } = "";
    public int OrderIndex { get; set; }

    public OutcomeKind Outcome { get; set; }
    public string? Summary { get; set; }

    public TargetCall Baseline { get; set; } = new();
    public TargetCall Candidate { get; set; } = new();

    /// <summary>Structured diff entries (status / header / body path / timing).</summary>
    public List<DiffEntry> Diffs { get; set; } = new();

    public DateTime CompletedAt { get; set; }
}

public enum DiffArea
{
    Status = 0,
    Header = 1,
    Body = 2,
    Timing = 3,
    Reachability = 4
}

public class DiffEntry
{
    public DiffArea Area { get; set; }
    public string Path { get; set; } = "";
    public string? Expected { get; set; }
    public string? Actual { get; set; }
    public string? Kind { get; set; } // value_mismatch / missing / extra / type_mismatch / masked_dynamic / numeric_delta ...
}
