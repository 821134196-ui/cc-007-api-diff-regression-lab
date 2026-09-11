namespace RegressionLab.Domain;

/// <summary>A comparison rule set version. Once used by a run it is immutable; editing bumps Version.</summary>
public class RuleSet
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public int Version { get; set; } = 1;
    public bool IsActive { get; set; } = true;

    /// <summary>JSON paths ($.a.b / $.arr[*].id) whose values are skipped entirely.</summary>
    public List<string> IgnorePaths { get; set; } = new();

    /// <summary>JSON path → key field. Arrays at these paths are sorted by the key before comparing.</summary>
    public Dictionary<string, string> ArraySortKeys { get; set; } = new();

    /// <summary>Absolute and relative tolerance for floating point comparison.</summary>
    public double NumericAbsTolerance { get; set; }
    public double NumericRelTolerance { get; set; }

    /// <summary>JSON path → regex. String values matching the regex at that path are masked as &lt;dynamic&gt;.</summary>
    public Dictionary<string, string> DynamicPatterns { get; set; } = new();

    /// <summary>Response header names ignored on every comparison (case-insensitive).</summary>
    public List<string> IgnoreHeaders { get; set; } = new();

    public DateTime CreatedAt { get; set; }

    public List<Scenario> Scenarios { get; set; } = new();
}
