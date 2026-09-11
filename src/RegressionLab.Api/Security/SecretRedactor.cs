using System.Text;
using RegressionLab.Domain;

namespace RegressionLab.Security;

/// <summary>
/// Strips concrete secret values out of everything we persist/return: headers,
/// bodies and error text. The lab UI can therefore never echo a secret back.
/// </summary>
public static class SecretRedactor
{
    public const string Mask = "***";

    public static TargetCall Apply(TargetCall call, IEnumerable<string> secrets)
    {
        var values = secrets
            .Where(v => !string.IsNullOrEmpty(v))
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(v => v.Length)
            .ToList();
        if (values.Count == 0) return call;

        foreach (var key in call.Headers.Keys.ToList())
            call.Headers[key] = Redact(call.Headers[key], values);

        call.BodyText = Redact(call.BodyText, values);
        if (call.ErrorDetail is not null)
            call.ErrorDetail = Redact(call.ErrorDetail, values);
        return call;
    }

    public static string Redact(string? text, List<string> values)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        var sb = new StringBuilder(text.Length);
        var rest = text;
        while (true)
        {
            var bestIdx = -1;
            string? best = null;
            foreach (var v in values)
            {
                var idx = rest.IndexOf(v, StringComparison.Ordinal);
                if (idx >= 0 && (bestIdx < 0 || idx < bestIdx || (idx == bestIdx && v.Length > best!.Length)))
                {
                    bestIdx = idx;
                    best = v;
                }
            }
            if (best is null) { sb.Append(rest); break; }
            sb.Append(rest.AsSpan(0, bestIdx)).Append(Mask);
            rest = rest[(bestIdx + best.Length)..];
        }
        return sb.ToString();
    }

    /// <summary>
    /// Scans user-submitted scenario fields and throws if any of them embeds a raw
    /// server-side secret value. Only {{secret:NAME}} references are allowed.
    /// </summary>
    public static void AssertNoRawSecrets(IEnumerable<string?> fields, IReadOnlyDictionary<string, string?> knownSecrets)
    {
        foreach (var field in fields)
        {
            if (string.IsNullOrEmpty(field)) continue;
            foreach (var (name, value) in knownSecrets)
            {
                if (!string.IsNullOrEmpty(value) && field.Contains(value, StringComparison.Ordinal))
                    throw new RawSecretSubmittedException(name);
            }
        }
    }
}

public sealed class RawSecretSubmittedException : Exception
{
    public RawSecretSubmittedException(string secretName)
        : base($"Submitted payload contains the raw value of server secret '{secretName}'. " +
               $"Use the {SecretResolver.Prefix}NAME}} reference instead.") { }
}
