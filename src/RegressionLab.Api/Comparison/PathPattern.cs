using System.Text.RegularExpressions;

namespace RegressionLab.Comparison;

/// <summary>
/// Parsed, path-indexed view of a <see cref="Domain.RuleSet"/>. Immutable for the
/// duration of a run (the run stores its own snapshot, so rule edits never affect it).
/// </summary>
public sealed class RuleSettings
{
    public required int Version { get; init; }
    public required IReadOnlyList<PathPattern> IgnorePaths { get; init; }
    public required IReadOnlyDictionary<PathPattern, string> ArraySortKeys { get; init; }
    public required IReadOnlyDictionary<PathPattern, Regex> DynamicPatterns { get; init; }
    public required IReadOnlySet<string> IgnoreHeaders { get; init; }
    public required double NumericAbsTolerance { get; init; }
    public required double NumericRelTolerance { get; init; }
    public required double TimingAbsToleranceMs { get; init; }
    public required double TimingRelTolerance { get; init; }

    public const string DynamicMask = "<dynamic>";
}

/// <summary>One tokenized JSON path such as $.items[*].id.</summary>
public sealed class PathPattern
{
    public enum Seg { Prop, AnyProp, Index, AnyIndex }
    public required IReadOnlyList<(Seg Kind, string Value)> Segments { get; init; }

    public bool Matches(IReadOnlyList<(Seg Kind, string Value)> actual)
    {
        if (actual.Count != Segments.Count) return false;
        for (int i = 0; i < actual.Count; i++)
        {
            var (pk, pv) = Segments[i];
            var (ak, av) = actual[i];
            switch (pk)
            {
                case Seg.AnyProp when ak == Seg.Prop:
                case Seg.AnyIndex when ak == Seg.Index:
                    continue;
                case Seg.Prop when ak == Seg.Prop && string.Equals(pv, av, StringComparison.Ordinal):
                case Seg.Index when ak == Seg.Index && pv == av:
                    continue;
                default:
                    return false;
            }
        }
        return true;
    }

    public bool MatchesPrefix(IReadOnlyList<(Seg Kind, string Value)> actual)
    {
        if (actual.Count < Segments.Count) return false;
        for (int i = 0; i < Segments.Count; i++)
        {
            var (pk, pv) = Segments[i];
            var (ak, av) = actual[i];
            switch (pk)
            {
                case Seg.AnyProp when ak == Seg.Prop:
                case Seg.AnyIndex when ak == Seg.Index:
                    continue;
                case Seg.Prop when ak == Seg.Prop && string.Equals(pv, av, StringComparison.Ordinal):
                case Seg.Index when ak == Seg.Index && pv == av:
                    continue;
                default:
                    return false;
            }
        }
        return true;
    }

    private static readonly Regex Tokenizer = new(
        @"\.\*|\.[A-Za-z_][A-Za-z0-9_]*|\.\[[^]]+\]|\[\*\]|\[\d+\]",
        RegexOptions.Compiled);

    /// <summary>Parse $.a.b / $.items[*].id / $.a[0].b / $.* / $.['weird name'].</summary>
    public static PathPattern Parse(string text)
    {
        text = text.Trim();
        if (!text.StartsWith('$'))
            throw new FormatException($"Path must start with '$': {text}");

        var segs = new List<(Seg, string)>();
        var rest = text[1..];
        var pos = 0;
        while (pos < rest.Length)
        {
            var m = Tokenizer.Match(rest, pos);
            if (!m.Success || m.Index != pos)
                throw new FormatException($"Cannot parse path at offset {pos}: {text}");
            pos = m.Index + m.Length;
            var tok = m.Value;
            if (tok == ".*") segs.Add((Seg.AnyProp, "*"));
            else if (tok == "[*]") segs.Add((Seg.AnyIndex, "*"));
            else if (tok.StartsWith(".[")) segs.Add((Seg.Prop, tok[2..^1]));
            else if (tok.StartsWith('[')) segs.Add((Seg.Index, tok[1..^1]));
            else segs.Add((Seg.Prop, tok[1..]));
        }
        return new PathPattern { Segments = segs };
    }
}
