using System.Web;
using RegressionLab.Domain;
using RegressionLab.Security;

namespace RegressionLab.Execution;

/// <summary>
/// Builds the concrete HttpRequestMessage for one target. Every user-supplied fragment
/// passes through <see cref="SecretResolver"/>; the caller decides which env-backed
/// secrets the scenario is allowed to reference.
/// </summary>
public sealed class RequestFactory
{
    private readonly SecretResolver _secrets;

    public RequestFactory(SecretResolver secrets) => _secrets = secrets;

    public HttpRequestMessage Build(Scenario scenario, Uri baseAddress)
    {
        var template = scenario.PathTemplate ?? "";

        // Separate path from embedded query string.
        var queryStart = template.IndexOf('?');
        var pathPart = queryStart >= 0 ? template[..queryStart] : template;
        var queryPart = queryStart >= 0 ? template[(queryStart + 1)..] : "";

        // {pathParam} substitution.
        foreach (var (name, raw) in scenario.PathParameters)
        {
            var value = _secrets.Resolve(raw);
            pathPart = pathPart.Replace("{" + name + "}", Uri.EscapeDataString(value));
        }

        var unresolved = System.Text.RegularExpressions.Regex.Match(pathPart, @"\{[^{}]+\}");
        if (unresolved.Success)
            throw new InvalidScenarioException($"Unresolved path parameter token: {unresolved.Value}");

        // Merge embedded query + explicit query parameters.
        var query = HttpUtility.ParseQueryString(string.Empty);
        if (!string.IsNullOrEmpty(queryPart))
        {
            var parsed = HttpUtility.ParseQueryString(queryPart);
            foreach (var k in parsed.AllKeys)
                if (k is not null) query[k] = _secrets.Resolve(parsed[k]);
        }
        foreach (var (k, v) in scenario.QueryParameters)
            query[Uri.EscapeDataString(k)] = _secrets.Resolve(v);

        var qs = query.ToString();
        var uri = new Uri(baseAddress, pathPart + (string.IsNullOrEmpty(qs) ? "" : "?" + qs));

        var method = scenario.Method switch
        {
            HttpMethodCode.GET => HttpMethod.Get,
            HttpMethodCode.POST => HttpMethod.Post,
            HttpMethodCode.PUT => HttpMethod.Put,
            HttpMethodCode.PATCH => HttpMethod.Patch,
            HttpMethodCode.DELETE => HttpMethod.Delete,
            _ => HttpMethod.Get
        };
        var msg = new HttpRequestMessage(method, uri);

        HttpContent? content = null;
        if (!string.IsNullOrWhiteSpace(scenario.JsonBody))
        {
            var resolved = _secrets.Resolve(scenario.JsonBody);
            // Validates JSON; a malformed body is a request error, not an HTTP call.
            try { _ = System.Text.Json.JsonDocument.Parse(resolved); }
            catch (System.Text.Json.JsonException ex)
            {
                throw new InvalidScenarioException($"Invalid JSON body: {ex.Message}");
            }
            content = new StringContent(resolved, System.Text.Encoding.UTF8, "application/json");
        }

        foreach (var (name, value) in scenario.Headers)
        {
            var v = _secrets.Resolve(value);
            if (!msg.Headers.TryAddWithoutValidation(name, v))
            {
                // Content headers (Content-Type, ...) need a content instance.
                content ??= new StringContent("");
                if (!content.Headers.TryAddWithoutValidation(name, v))
                    throw new InvalidScenarioException($"Unsupported request header: {name}");
            }
        }

        if (content is not null) msg.Content = content;

        return msg;
    }
}

public sealed class InvalidScenarioException : Exception
{
    public InvalidScenarioException(string message) : base(message) { }
}
