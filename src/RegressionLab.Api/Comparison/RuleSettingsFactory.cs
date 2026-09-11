using System.Text.Json;
using System.Text.RegularExpressions;
using RegressionLab.Domain;

namespace RegressionLab.Comparison;

public static class RuleSettingsFactory
{
    private sealed class RuleSnapshot
    {
        public int Version { get; set; }
        public List<string> IgnorePaths { get; set; } = new();
        public Dictionary<string, string> ArraySortKeys { get; set; } = new();
        public double NumericAbsTolerance { get; set; }
        public double NumericRelTolerance { get; set; }
        public Dictionary<string, string> DynamicPatterns { get; set; } = new();
        public List<string> IgnoreHeaders { get; set; } = new();
        public double TimingAbsToleranceMs { get; set; } = 100;
        public double TimingRelTolerance { get; set; } = 0.25;
    }

    public static RuleSettings FromEntity(RuleSet rs) =>
        Build(new RuleSnapshot
        {
            Version = rs.Version,
            IgnorePaths = rs.IgnorePaths,
            ArraySortKeys = rs.ArraySortKeys,
            NumericAbsTolerance = rs.NumericAbsTolerance,
            NumericRelTolerance = rs.NumericRelTolerance,
            DynamicPatterns = rs.DynamicPatterns,
            IgnoreHeaders = rs.IgnoreHeaders,
        });

    /// <summary>Rebuild rule settings from the JSON snapshot stored on a historical run.</summary>
    public static RuleSettings FromSnapshot(string json)
    {
        var snap = JsonSerializer.Deserialize<RuleSnapshot>(json, JsonOpts)
                   ?? throw new InvalidOperationException("Invalid rule snapshot");
        return Build(snap);
    }

    public static string ToSnapshot(RuleSet rs) =>
        JsonSerializer.Serialize(new RuleSnapshot
        {
            Version = rs.Version,
            IgnorePaths = rs.IgnorePaths,
            ArraySortKeys = rs.ArraySortKeys,
            NumericAbsTolerance = rs.NumericAbsTolerance,
            NumericRelTolerance = rs.NumericRelTolerance,
            DynamicPatterns = rs.DynamicPatterns,
            IgnoreHeaders = rs.IgnoreHeaders,
        }, JsonOpts);

    private static readonly JsonSerializerOptions JsonOpts =
        new(JsonSerializerDefaults.Web);

    private static RuleSettings Build(RuleSnapshot snap)
    {
        var ignore = new List<PathPattern>();
        foreach (var p in snap.IgnorePaths.Where(x => !string.IsNullOrWhiteSpace(x)))
            ignore.Add(PathPattern.Parse(p));

        var sortKeys = new Dictionary<PathPattern, string>();
        foreach (var (path, key) in snap.ArraySortKeys)
        {
            if (string.IsNullOrWhiteSpace(key)) continue;
            sortKeys[PathPattern.Parse(path)] = key;
        }

        var dyn = new Dictionary<PathPattern, Regex>();
        foreach (var (path, pattern) in snap.DynamicPatterns)
        {
            if (string.IsNullOrWhiteSpace(pattern)) continue;
            // Anchored full-match by default; users can write .* fragments inside.
            var anchored = pattern.StartsWith('^') || pattern.EndsWith('$')
                ? pattern
                : $"^(?:{pattern})$";
            dyn[PathPattern.Parse(path)] = new Regex(anchored,
                RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
        }

        var ignoreHeaders = snap.IgnoreHeaders
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim().ToLowerInvariant())
            .ToHashSet();
        // Hop-by-hop / environment headers are never compared.
        foreach (var h in DefaultIgnoredHeaders) ignoreHeaders.Add(h);

        return new RuleSettings
        {
            Version = snap.Version,
            IgnorePaths = ignore,
            ArraySortKeys = sortKeys,
            DynamicPatterns = dyn,
            IgnoreHeaders = ignoreHeaders,
            NumericAbsTolerance = snap.NumericAbsTolerance,
            NumericRelTolerance = snap.NumericRelTolerance,
            TimingAbsToleranceMs = snap.TimingAbsToleranceMs,
            TimingRelTolerance = snap.TimingRelTolerance,
        };
    }

    public static readonly string[] DefaultIgnoredHeaders =
    {
        "date", "server", "content-length", "connection", "keep-alive", "transfer-encoding",
        "x-powered-by", "x-aspnet-version", "via", "age", "x-cache", "x-served-by",
        "cf-ray", "x-request-id", "x-correlation-id", "x-response-time", "x-processing-time"
    };
}
