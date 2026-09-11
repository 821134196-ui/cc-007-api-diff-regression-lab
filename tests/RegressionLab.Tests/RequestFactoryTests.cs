using System.Net;
using RegressionLab.Domain;
using RegressionLab.Execution;
using RegressionLab.Security;
using Xunit;

namespace RegressionLab.Tests;

public class RequestFactoryTests
{
    private static RequestFactory Factory(Dictionary<string, string> env, params string[] refs)
    {
        var lookup = new HashSet<string>(refs);
        return new RequestFactory(new SecretResolver(
            name => env.TryGetValue(name, out var v) ? v : null, lookup));
    }

    [Fact]
    public void Path_parameter_is_substituted_and_escaped()
    {
        var s = ScenarioBuilder.Scenario(path: "/users/{id}/items",
            pathParams: new() { ["id"] = "a/b x" });
        var msg = new RequestFactory(new SecretResolver()).Build(s, new Uri("http://baseline:8080"));
        Assert.Equal("http://baseline:8080/users/a%2Fb%20x/items", msg.RequestUri!.AbsoluteUri);
        Assert.Equal(HttpMethod.Get, msg.Method);
    }

    [Fact]
    public void Embedded_and_explicit_query_parameters_merge()
    {
        var s = ScenarioBuilder.Scenario(path: "/search?q=one");
        s.QueryParameters["page"] = "2";
        var msg = new RequestFactory(new SecretResolver()).Build(s, new Uri("http://host:9/"));
        var query = msg.RequestUri!.Query;
        Assert.Contains("q=one", query);
        Assert.Contains("page=2", query);
    }

    [Fact]
    public void Secret_in_header_and_body_is_resolved_at_send_time_only()
    {
        var f = Factory(new() { ["RLAB_TOKEN"] = "tok-123" }, "RLAB_TOKEN");
        var s = ScenarioBuilder.Scenario(
            path: "/echo", method: HttpMethodCode.POST,
            headers: new() { ["X-Api-Key"] = "{{secret:RLAB_TOKEN}}" },
            body: """{"key":"{{secret:RLAB_TOKEN}}"}""",
            secretRefs: new() { "RLAB_TOKEN" });

        var msg = f.Build(s, new Uri("http://host:9/"));
        Assert.Equal("tok-123", msg.Headers.GetValues("X-Api-Key").Single());
        Assert.Contains("tok-123", msg.Content!.ReadAsStringAsync().Result);

        // The stored scenario still carries only the reference.
        Assert.Equal("{{secret:RLAB_TOKEN}}", s.Headers["X-Api-Key"]);
        Assert.DoesNotContain("tok-123", s.JsonBody!);
    }

    [Fact]
    public void Unresolved_path_token_is_a_request_error()
    {
        var s = ScenarioBuilder.Scenario(path: "/users/{id}");
        Assert.Throws<InvalidScenarioException>(
            () => new RequestFactory(new SecretResolver()).Build(s, new Uri("http://h/")));
    }

    [Fact]
    public void Malformed_json_body_is_a_request_error_not_an_http_call()
    {
        var s = ScenarioBuilder.Scenario(path: "/x", method: HttpMethodCode.POST,
            body: "{not-json");
        Assert.Throws<InvalidScenarioException>(
            () => new RequestFactory(new SecretResolver()).Build(s, new Uri("http://h/")));
    }
}
