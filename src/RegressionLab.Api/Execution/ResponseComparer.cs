using RegressionLab.Comparison;
using RegressionLab.Domain;

namespace RegressionLab.Execution;

/// <summary>Compares two completed target calls into outcome + structured diff entries.</summary>
public sealed class ResponseComparer
{
    private readonly JsonDiffer _json;
    private readonly RuleSettings _rule;

    public ResponseComparer(RuleSettings rule)
    {
        _rule = rule;
        _json = new JsonDiffer(rule);
    }

    public (OutcomeKind Outcome, List<DiffEntry> Diffs, string Summary) Compare(TargetCall baseline, TargetCall candidate)
    {
        var diffs = new List<DiffEntry>();

        // Reachability first: a failed target is NEVER represented as an empty response.
        if (!baseline.Reached || !candidate.Reached)
        {
            if (!baseline.Reached)
                diffs.Add(new DiffEntry
                {
                    Area = DiffArea.Reachability,
                    Path = "baseline",
                    Expected = "reachable",
                    Actual = baseline.ErrorKind ?? "unreachable",
                    Kind = baseline.ErrorDetail
                });
            if (!candidate.Reached)
                diffs.Add(new DiffEntry
                {
                    Area = DiffArea.Reachability,
                    Path = "candidate",
                    Expected = "reachable",
                    Actual = candidate.ErrorKind ?? "unreachable",
                    Kind = candidate.ErrorDetail
                });

            var side = !baseline.Reached && !candidate.Reached
                ? "both targets unreachable"
                : !baseline.Reached ? "baseline unreachable" : "candidate unreachable";
            return (OutcomeKind.NetworkFailure, diffs, side);
        }

        // Status code.
        if (baseline.StatusCode != candidate.StatusCode)
        {
            diffs.Add(new DiffEntry
            {
                Area = DiffArea.Status,
                Path = "status",
                Expected = baseline.StatusCode?.ToString(),
                Actual = candidate.StatusCode?.ToString(),
                Kind = "status_mismatch"
            });
        }

        // Headers (case-insensitive, minus ignored/hop-by-hop names).
        CompareHeaders(baseline.Headers, candidate.Headers, diffs);

        // Body (JSON-aware).
        diffs.AddRange(_json.CompareBodies(
            baseline.BodyText, baseline.BodyIsJson,
            candidate.BodyText, candidate.BodyIsJson));

        // Response time: flag only when both the absolute and relative tolerances are exceeded.
        var b = baseline.ElapsedMs;
        var c = candidate.ElapsedMs;
        var absDelta = Math.Abs(b - c);
        var relBase = (double)Math.Max(Math.Max(b, c), 1);
        if (absDelta > _rule.TimingAbsToleranceMs &&
            (double)absDelta / relBase > _rule.TimingRelTolerance)
        {
            diffs.Add(new DiffEntry
            {
                Area = DiffArea.Timing,
                Path = "elapsed_ms",
                Expected = b.ToString(),
                Actual = c.ToString(),
                Kind = "timing_delta"
            });
        }

        var outcome = diffs.Count == 0 ? OutcomeKind.Match : OutcomeKind.Diff;
        var summary = diffs.Count == 0
            ? "match"
            : string.Join("; ", diffs.GroupBy(d => d.Area).Select(g => $"{g.Key}:{g.Count()}"));
        return (outcome, diffs, summary);
    }

    private void CompareHeaders(
        Dictionary<string, string> baseline,
        Dictionary<string, string> candidate,
        List<DiffEntry> diffs)
    {
        var keys = new HashSet<string>(baseline.Keys, StringComparer.Ordinal);
        keys.UnionWith(candidate.Keys);

        foreach (var key in keys.Where(k => !_rule.IgnoreHeaders.Contains(k)).OrderBy(k => k))
        {
            baseline.TryGetValue(key, out var bv);
            candidate.TryGetValue(key, out var cv);
            if (!string.Equals(bv, cv, StringComparison.Ordinal))
            {
                diffs.Add(new DiffEntry
                {
                    Area = DiffArea.Header,
                    Path = key,
                    Expected = bv,
                    Actual = cv,
                    Kind = baseline.ContainsKey(key) && candidate.ContainsKey(key)
                        ? "header_mismatch"
                        : baseline.ContainsKey(key) ? "missing" : "extra"
                });
            }
        }
    }
}
