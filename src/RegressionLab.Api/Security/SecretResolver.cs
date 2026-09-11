using System.Text.RegularExpressions;

namespace RegressionLab.Security;

/// <summary>
/// Resolves {{secret:NAME}} placeholders from process environment variables only.
/// Secrets are read on demand and never persisted by the application.
/// </summary>
public partial class SecretResolver
{
    private readonly Func<string, string?> _readEnv;
    private readonly HashSet<string> _knownRefs;

    /// <summary>Every concrete secret value resolved by this instance (for post-hoc redaction).</summary>
    public List<string> ResolvedValues { get; } = new();

    public SecretResolver(Func<string, string?>? readEnv = null, IEnumerable<string>? knownRefs = null)
    {
        _readEnv = readEnv ?? Environment.GetEnvironmentVariable;
        _knownRefs = new HashSet<string>(knownRefs ?? Array.Empty<string>(), StringComparer.Ordinal);
    }

    [GeneratedRegex(@"\{\{secret:([A-Za-z_][A-Za-z0-9_]*)\}\}", RegexOptions.Compiled)]
    private static partial Regex RefRegex();

    public const string Prefix = "{{secret:";

    public static IReadOnlyList<string> ExtractRefs(string? text)
    {
        if (string.IsNullOrEmpty(text)) return Array.Empty<string>();
        return RefRegex().Matches(text).Select(m => m.Groups[1].Value).Distinct().ToList();
    }

    public static bool ContainsRef(string? text) =>
        !string.IsNullOrEmpty(text) && text.Contains(Prefix, StringComparison.Ordinal);

    /// <summary>Throws <see cref="SecretNotFoundException"/> when a referenced env var is missing.</summary>
    public string Resolve(string? template)
    {
        if (string.IsNullOrEmpty(template)) return template ?? "";
        return RefRegex().Replace(template, m =>
        {
            var name = m.Groups[1].Value;
            if (_knownRefs.Count > 0 && !_knownRefs.Contains(name))
                throw new UnauthorizedSecretException(name);
            var value = _readEnv(name);
            if (string.IsNullOrEmpty(value))
                throw new SecretNotFoundException(name);
            lock (ResolvedValues)
            {
                if (!ResolvedValues.Contains(value)) ResolvedValues.Add(value);
            }
            return value;
        });
    }
}

public sealed class SecretNotFoundException : Exception
{
    public string Name { get; }
    public SecretNotFoundException(string name)
        : base($"Referenced secret '{name}' is not set on the server.") => Name = name;
}

public sealed class UnauthorizedSecretException : Exception
{
    public string Name { get; }
    public UnauthorizedSecretException(string name)
        : base($"Secret '{name}' is not declared in this scenario's secret references.") => Name = name;
}
