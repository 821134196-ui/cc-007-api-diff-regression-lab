using System.Net;
using RegressionLab.Comparison;
using RegressionLab.Domain;
using RegressionLab.Execution;
using RegressionLab.Security;
using Xunit;

namespace RegressionLab.Tests;

public class SecretResolutionTests
{
    [Fact]
    public void Resolve_substitutes_env_value()
    {
        var resolver = new SecretResolver(
            name => name == "RLAB_TOKEN" ? "s3cr3t-value" : null,
            new[] { "RLAB_TOKEN" });
        Assert.Equal("Bearer s3cr3t-value", resolver.Resolve("Bearer {{secret:RLAB_TOKEN}}"));
    }

    [Fact]
    public void Missing_env_var_throws_SecretNotFoundException()
    {
        var resolver = new SecretResolver(_ => null, new[] { "RLAB_TOKEN" });
        var ex = Assert.Throws<SecretNotFoundException>(
            () => resolver.Resolve("{{secret:RLAB_TOKEN}}"));
        Assert.Equal("RLAB_TOKEN", ex.Name);
    }

    [Fact]
    public void Undeclared_secret_reference_is_rejected()
    {
        var resolver = new SecretResolver(
            name => Environment.GetEnvironmentVariable(name),
            knownRefs: new[] { "RLAB_ALLOWED" });
        Assert.Throws<UnauthorizedSecretException>(
            () => resolver.Resolve("{{secret:RLAB_OTHER}}"));
    }

    [Fact]
    public void Scenario_request_does_not_store_resolved_value()
    {
        // The scenario entity only ever holds the reference placeholder.
        var scenario = ScenarioBuilder.Scenario(
            headers: new() { ["Authorization"] = "{{secret:RLAB_TOKEN}}" },
            secretRefs: new() { "RLAB_TOKEN" });
        Assert.DoesNotContain("resolved-", scenario.Headers["Authorization"]);
        Assert.Equal("{{secret:RLAB_TOKEN}}", scenario.Headers["Authorization"]);
    }

    [Fact]
    public async Task Resolved_secret_is_redacted_from_persisted_responses()
    {
        const string secret = "resolved-super-secret-9001";
        Environment.SetEnvironmentVariable("RLAB_REDACT_TEST", secret);

        var handler = new FakeHttpHandler
        {
            Baseline = (req, ct) =>
            {
                var resp = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    // Target echoes the secret in header AND body.
                    Content = new StringContent(
                        $$"""{"echo":"{{secret}}","note":"ok"}""",
                        System.Text.Encoding.UTF8, "application/json")
                };
                resp.Headers.TryAddWithoutValidation("X-Echo-Key", req.Headers.TryGetValues("X-Api-Key", out var k) ? string.Join(",", k) : "");
                return Task.FromResult(resp);
            },
            Candidate = (req, ct) =>
            {
                var resp = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        $$"""{"echo":"{{secret}}","note":"ok"}""",
                        System.Text.Encoding.UTF8, "application/json")
                };
                resp.Headers.TryAddWithoutValidation("X-Echo-Key", req.Headers.TryGetValues("X-Api-Key", out var k) ? string.Join(",", k) : "");
                return Task.FromResult(resp);
            }
        };

        var scenario = ScenarioBuilder.Scenario(
            headers: new() { ["X-Api-Key"] = "{{secret:RLAB_REDACT_TEST}}" },
            secretRefs: new() { "RLAB_REDACT_TEST" });
        var run = ScenarioBuilder.Run();

        var store = new MemoryRunStore(run, new() { scenario }, TestRules.Create());
        var runner = new RunRunner(store, () => handler);
        await runner.ExecuteAsync(run.Id, CancellationToken.None);

        var result = Assert.Single(store.Results);
        Assert.Equal(OutcomeKind.Match, result.Outcome);
        Assert.DoesNotContain(secret, result.Baseline.BodyText ?? "");
        Assert.DoesNotContain(secret, result.Candidate.BodyText ?? "");
        Assert.Equal("***", result.Baseline.Headers.GetValueOrDefault("x-echo-key"));
        Assert.Equal("***", result.Candidate.Headers.GetValueOrDefault("x-echo-key"));
    }

    [Fact]
    public void Raw_secret_value_in_submitted_payload_is_detected()
    {
        var known = new Dictionary<string, string?> { ["RLAB_TOKEN"] = "topsecret-xyz" };
        Assert.Throws<RawSecretSubmittedException>(() =>
            SecretRedactor.AssertNoRawSecrets(
                new[] { "header", "Bearer topsecret-xyz" }, known));
        // A reference placeholder is fine.
        SecretRedactor.AssertNoRawSecrets(
            new[] { "{{secret:RLAB_TOKEN}}" }, known);
    }

    [Fact]
    public void ExtractRefs_finds_unique_names()
    {
        var refs = SecretResolver.ExtractRefs(
            "{{secret:A}} and {{secret:B}} again {{secret:A}}");
        Assert.Equal(new[] { "A", "B" }, refs.OrderBy(x => x).ToArray());
    }

    [Fact]
    public async Task Missing_secret_produces_request_error_not_http_failure()
    {
        Environment.SetEnvironmentVariable("RLAB_MISSING_TEST", null);
        var handler = new FakeHttpHandler();
        var scenario = ScenarioBuilder.Scenario(
            headers: new() { ["X-Api-Key"] = "{{secret:RLAB_MISSING_TEST}}" },
            secretRefs: new() { "RLAB_MISSING_TEST" });
        var run = ScenarioBuilder.Run();
        var store = new MemoryRunStore(run, new() { scenario }, TestRules.Create());
        var runner = new RunRunner(store, () => handler);

        await runner.ExecuteAsync(run.Id, CancellationToken.None);

        var result = Assert.Single(store.Results);
        Assert.Equal(OutcomeKind.RequestError, result.Outcome);
        Assert.Contains("RLAB_MISSING_TEST", result.Summary);
        Assert.False(result.Baseline.Reached);
        Assert.Empty(handler.Requests); // no HTTP call attempted
    }
}
