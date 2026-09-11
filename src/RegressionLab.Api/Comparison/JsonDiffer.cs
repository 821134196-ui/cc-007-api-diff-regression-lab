using System.Text.Json;
using System.Text.RegularExpressions;
using RegressionLab.Domain;

namespace RegressionLab.Comparison;

/// <summary>
/// Deep JSON comparison implementing ignore paths, key-based array sorting,
/// floating-point tolerance and dynamic-value regex masking.
/// </summary>
public sealed class JsonDiffer
{
    private const int MaxSavedText = 200;

    private readonly RuleSettings _rule;

    public JsonDiffer(RuleSettings rule) => _rule = rule;

    public List<DiffEntry> CompareBodies(string? baseline, bool baseIsJson, string? candidate, bool candIsJson)
    {
        var diffs = new List<DiffEntry>();

        if (!baseIsJson || !candIsJson)
        {
            // Both non-JSON: text compare. Mixed JSON/non-JSON: structural mismatch.
            if (baseIsJson != candIsJson)
            {
                diffs.Add(Entry(DiffArea.Body, "$",
                    baseIsJson ? "json" : "text", candIsJson ? "json" : "text", "type_mismatch"));
            }
            else if (!string.Equals(baseline ?? "", candidate ?? "", StringComparison.Ordinal))
            {
                diffs.Add(Entry(DiffArea.Body, "$", Trunc(baseline), Trunc(candidate), "text_mismatch"));
            }
            return diffs;
        }

        JsonDocument? baseDoc = null, candDoc = null;
        try
        {
            baseDoc = JsonDocument.Parse(baseline ?? "null");
            candDoc = JsonDocument.Parse(candidate ?? "null");
        }
        catch (JsonException)
        {
            // Detected as JSON by content-type but unparseable — fall back to text.
            if (!string.Equals(baseline ?? "", candidate ?? "", StringComparison.Ordinal))
                diffs.Add(Entry(DiffArea.Body, "$", Trunc(baseline), Trunc(candidate), "text_mismatch"));
            return diffs;
        }

        using (baseDoc)
        using (candDoc)
        {
            CompareElement("$", new(), baseDoc.RootElement, candDoc.RootElement, diffs);
        }
        return diffs;
    }

    private void CompareElement(
        string pathStr,
        List<(PathPattern.Seg Kind, string Value)> path,
        JsonElement expected,
        JsonElement actual,
        List<DiffEntry> diffs)
    {
        if (IsIgnored(path)) return;

        // Dynamic masking: when the path matches a pattern and BOTH sides are strings
        // matching the regex, values are considered equal regardless of content.
        if (expected.ValueKind == JsonValueKind.String && actual.ValueKind == JsonValueKind.String &&
            TryGetDynamicMask(path, out var rx) &&
            rx.IsMatch(expected.GetString() ?? "") && rx.IsMatch(actual.GetString() ?? ""))
        {
            return;
        }

        if (expected.ValueKind != actual.ValueKind ||
            (expected.ValueKind == JsonValueKind.Number) != (actual.ValueKind == JsonValueKind.Number))
        {
            // Numbers vs strings/bools are type mismatches; null vs anything is a value mismatch.
            diffs.Add(Entry(DiffArea.Body, pathStr, KindValue(expected), KindValue(actual), "type_mismatch"));
            return;
        }

        switch (expected.ValueKind)
        {
            case JsonValueKind.Object:
                CompareObject(pathStr, path, expected, actual, diffs);
                break;
            case JsonValueKind.Array:
                CompareArray(pathStr, path, expected, actual, diffs);
                break;
            case JsonValueKind.Number:
                if (!NumbersEqual(expected, actual, out var ev, out var av))
                    diffs.Add(Entry(DiffArea.Body, pathStr, ev.ToString("R"), av.ToString("R"), "numeric_delta"));
                break;
            case JsonValueKind.String:
                var es = expected.GetString() ?? "";
                var asx = actual.GetString() ?? "";
                if (!string.Equals(es, asx, StringComparison.Ordinal))
                {
                    var kind = TryGetDynamicMask(path, out _) ? "dynamic_mismatch" : "value_mismatch";
                    diffs.Add(Entry(DiffArea.Body, pathStr, Trunc(es), Trunc(asx), kind));
                }
                break;
            case JsonValueKind.True:
            case JsonValueKind.False:
                if (expected.GetBoolean() != actual.GetBoolean())
                    diffs.Add(Entry(DiffArea.Body, pathStr, expected.GetRawText(), actual.GetRawText(), "value_mismatch"));
                break;
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                break; // both same kind
        }
    }

    private void CompareObject(
        string pathStr,
        List<(PathPattern.Seg, string)> path,
        JsonElement expected,
        JsonElement actual,
        List<DiffEntry> diffs)
    {
        var expProps = expected.EnumerateObject().ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);
        var actProps = actual.EnumerateObject().ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);

        foreach (var (name, ev) in expProps)
        {
            var childPath = ChildPathStr(pathStr, name);
            var childSegs = WithSeg(path, (PathPattern.Seg.Prop, name));
            if (IsIgnored(childSegs)) continue;

            if (!actProps.TryGetValue(name, out var av))
            {
                if (!IsContainer(ev))
                    diffs.Add(Entry(DiffArea.Body, childPath, Trunc(ev.GetRawText()), null, "missing"));
                else
                    diffs.Add(Entry(DiffArea.Body, childPath, "present", "absent", "missing"));
                continue;
            }
            CompareElement(childPath, childSegs, ev, av, diffs);
        }

        foreach (var name in actProps.Keys)
        {
            if (!expProps.ContainsKey(name))
            {
                var childPath = ChildPathStr(pathStr, name);
                var childSegs = WithSeg(path, (PathPattern.Seg.Prop, name));
                if (IsIgnored(childSegs)) continue;
                var av = actProps[name];
                if (!IsContainer(av))
                    diffs.Add(Entry(DiffArea.Body, childPath, null, Trunc(av.GetRawText()), "extra"));
                else
                    diffs.Add(Entry(DiffArea.Body, childPath, "absent", "present", "extra"));
            }
        }
    }

    private void CompareArray(
        string pathStr,
        List<(PathPattern.Seg, string)> path,
        JsonElement expected,
        JsonElement actual,
        List<DiffEntry> diffs)
    {
        var exp = expected.EnumerateArray().ToList();
        var act = actual.EnumerateArray().ToList();

        if (TryGetSortKey(path, out var sortKey))
        {
            SortByKey(exp, sortKey);
            SortByKey(act, sortKey);
        }

        if (exp.Count != act.Count)
        {
            diffs.Add(Entry(DiffArea.Body, pathStr + "[length]",
                exp.Count.ToString(), act.Count.ToString(), "array_length"));
        }

        var n = Math.Min(exp.Count, act.Count);
        for (var i = 0; i < n; i++)
        {
            var childPath = $"{pathStr}[{i}]";
            var childSegs = WithSeg(path, (PathPattern.Seg.Index, i.ToString()));
            if (IsIgnored(childSegs)) continue;
            CompareElement(childPath, childSegs, exp[i], act[i], diffs);
        }
        // Remaining elements on either side are already summarised by the [length] entry.
    }

    private bool NumbersEqual(JsonElement a, JsonElement b, out double av, out double bv)
    {
        av = a.GetDouble();
        bv = b.GetDouble();
        var diff = Math.Abs(av - bv);
        var tol = Math.Max(_rule.NumericAbsTolerance,
            _rule.NumericRelTolerance * Math.Max(Math.Abs(av), Math.Abs(bv)));
        return diff <= tol;
    }

    private bool IsIgnored(List<(PathPattern.Seg, string)> path)
    {
        foreach (var p in _rule.IgnorePaths)
            if (p.Matches(path) || p.MatchesPrefix(path)) return true;
        return false;
    }

    private bool TryGetSortKey(List<(PathPattern.Seg, string)> path, out string key)
    {
        foreach (var (pattern, k) in _rule.ArraySortKeys)
        {
            if (pattern.Matches(path)) { key = k; return true; }
        }
        key = "";
        return false;
    }

    private bool TryGetDynamicMask(List<(PathPattern.Seg, string)> path, out Regex rx)
    {
        foreach (var (pattern, compiled) in _rule.DynamicPatterns)
        {
            if (pattern.Matches(path)) { rx = compiled; return true; }
        }
        rx = null!;
        return false;
    }

    private static void SortByKey(List<JsonElement> list, string key)
    {
        list.Sort((a, b) =>
        {
            var av = TryKey(a, key);
            var bv = TryKey(b, key);
            return string.CompareOrdinal(av, bv);
        });
    }

    private static string? TryKey(JsonElement el, string key)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;
        foreach (var p in el.EnumerateObject())
        {
            if (string.Equals(p.Name, key, StringComparison.Ordinal))
            {
                return p.Value.ValueKind switch
                {
                    JsonValueKind.String => p.Value.GetString(),
                    JsonValueKind.Number => p.Value.GetRawText(),
                    _ => null
                };
            }
        }
        return null;
    }

    private static bool IsContainer(JsonElement el) =>
        el.ValueKind is JsonValueKind.Object or JsonValueKind.Array;

    private static List<(PathPattern.Seg, string)> WithSeg(
        List<(PathPattern.Seg, string)> path, (PathPattern.Seg, string) seg)
    {
        var copy = new List<(PathPattern.Seg, string)>(path.Count + 1);
        copy.AddRange(path);
        copy.Add(seg);
        return copy;
    }

    private static string ChildPathStr(string parent, string prop) =>
        prop.IsSimpleName() ? $"{parent}.{prop}" : $"{parent}[{JsonSerializer.Serialize(prop)}]";

    private static string KindValue(JsonElement el) => el.ValueKind.ToString().ToLowerInvariant();

    private static string? Trunc(string? s) =>
        s is null ? null : (s.Length <= MaxSavedText ? s : s[..MaxSavedText] + "…");

    private static DiffEntry Entry(DiffArea area, string path, string? expected, string? actual, string kind) =>
        new()
        {
            Area = area,
            Path = path,
            Expected = expected,
            Actual = actual,
            Kind = kind
        };
}

internal static class StringExtensions
{
    public static bool IsSimpleName(this string s)
    {
        if (s.Length == 0) return false;
        if (!(char.IsLetter(s[0]) || s[0] == '_')) return false;
        foreach (var c in s)
            if (!(char.IsLetterOrDigit(c) || c == '_')) return false;
        return true;
    }
}
