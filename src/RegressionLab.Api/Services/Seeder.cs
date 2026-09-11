using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RegressionLab.Comparison;
using RegressionLab.Data;
using RegressionLab.Domain;

namespace RegressionLab.Services;

/// <summary>Idempotent seed: default rule set + stable/diff example scenarios.</summary>
public static class Seeder
{
    public const string DefaultRuleName = "default";

    public static readonly Guid DefaultRuleId = Guid.Parse("0a1a2a3a-0001-4000-8000-000000000001");

    public static readonly Guid StablePing = Guid.Parse("0a1a2a3a-1000-4000-8000-000000000001");
    public static readonly Guid StableUser = Guid.Parse("0a1a2a3a-1000-4000-8000-000000000002");
    public static readonly Guid StableList = Guid.Parse("0a1a2a3a-1000-4000-8000-000000000003");
    public static readonly Guid StableEcho = Guid.Parse("0a1a2a3a-1000-4000-8000-000000000004");
    public static readonly Guid DiffStatus = Guid.Parse("0a1a2a3a-2000-4000-8000-000000000001");
    public static readonly Guid DiffHeader = Guid.Parse("0a1a2a3a-2000-4000-8000-000000000002");
    public static readonly Guid DiffBody = Guid.Parse("0a1a2a3a-2000-4000-8000-000000000003");
    public static readonly Guid DiffNumber = Guid.Parse("0a1a2a3a-2000-4000-8000-000000000004");
    public static readonly Guid DiffArray = Guid.Parse("0a1a2a3a-2000-4000-8000-000000000005");
    public static readonly Guid DiffAuth = Guid.Parse("0a1a2a3a-2000-4000-8000-000000000006");

    public static async Task SeedAsync(LabDbContext db)
    {
        if (!await db.RuleSets.AnyAsync(r => r.Id == DefaultRuleId))
        {
            var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var rule = new RuleSet
            {
                Id = DefaultRuleId,
                Name = DefaultRuleName,
                Version = 1,
                IsActive = true,
                CreatedAt = now,
                IgnorePaths = new()
                {
                    "$.meta.request_id",
                    "$.meta.server_time"
                },
                ArraySortKeys = new()
                {
                    // The /orders list comes back in different order between targets;
                    // sort by id before comparing.
                    ["$.items"] = "id"
                },
                NumericAbsTolerance = 0.01,
                NumericRelTolerance = 0.0,
                DynamicPatterns = new()
                {
                    // ISO timestamps embedded in payloads are dynamic values.
                    ["$.generatedAt"] = @"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?Z?"
                },
                IgnoreHeaders = new() { "x-fixture-instance" }
            };
            db.RuleSets.Add(rule);

            db.Scenarios.AddRange(StableScenarios(now));
            db.Scenarios.AddRange(DiffScenarios(now));
            await db.SaveChangesAsync();
        }
    }

    private static IEnumerable<Scenario> StableScenarios(DateTime now)
    {
        Scenario Make(Guid id, string name, string desc, HttpMethodCode method,
            string path, Dictionary<string, string>? pathParams = null,
            Dictionary<string, string>? query = null,
            Dictionary<string, string>? headers = null,
            string? body = null, List<string>? refs = null) => new()
            {
                Id = id,
                RuleSetId = DefaultRuleId,
                Name = name,
                Description = desc,
                Method = method,
                PathTemplate = path,
                PathParameters = pathParams ?? new(),
                QueryParameters = query ?? new(),
                Headers = headers ?? new(),
                JsonBody = body,
                SecretRefs = refs ?? new(),
                Enabled = true,
                CreatedAt = now,
                UpdatedAt = now
            };

        yield return Make(StablePing, "stable: health ping",
            "Both targets return an identical health document.",
            HttpMethodCode.GET, "/health");

        yield return Make(StableUser, "stable: user by id (path param)",
            "Path parameter substitution; identical user JSON.",
            HttpMethodCode.GET, "/users/{id}",
            pathParams: new() { ["id"] = "42" });

        yield return Make(StableList, "stable: search (query params)",
            "Query parameters; identical list payload.",
            HttpMethodCode.GET, "/users",
            query: new() { ["role"] = "admin", ["active"] = "true" });

        yield return Make(StableEcho, "stable: echo with secret header",
            "Header value comes from a server env var; both targets see the same resolved value.",
            HttpMethodCode.POST, "/echo",
            headers: new() { ["X-Api-Key"] = "{{secret:RLAB_SECRET_DEMO_API_KEY}}" },
            body: """{"message":"hello","n":1}""",
            refs: new() { "RLAB_SECRET_DEMO_API_KEY" });
    }

    private static IEnumerable<Scenario> DiffScenarios(DateTime now)
    {
        Scenario Make(Guid id, string name, string desc, HttpMethodCode method,
            string path, Dictionary<string, string>? pathParams = null,
            Dictionary<string, string>? query = null,
            Dictionary<string, string>? headers = null,
            string? body = null, List<string>? refs = null) => new()
            {
                Id = id,
                RuleSetId = DefaultRuleId,
                Name = name,
                Description = desc,
                Method = method,
                PathTemplate = path,
                PathParameters = pathParams ?? new(),
                QueryParameters = query ?? new(),
                Headers = headers ?? new(),
                JsonBody = body,
                SecretRefs = refs ?? new(),
                Enabled = true,
                CreatedAt = now,
                UpdatedAt = now
            };

        yield return Make(DiffStatus, "diff: candidate returns 201 instead of 200",
            "Status code mismatch on user creation.",
            HttpMethodCode.POST, "/users",
            body: """{"name":"Alice","role":"viewer"}""");

        yield return Make(DiffHeader, "diff: X-Rate-Limit header",
            "Candidate advertises a different rate limit header.",
            HttpMethodCode.GET, "/users/{id}",
            pathParams: new() { ["id"] = "7" });

        yield return Make(DiffBody, "diff: user email field changed",
            "JSON body value mismatch at $.email.",
            HttpMethodCode.GET, "/users/{id}",
            pathParams: new() { ["id"] = "9" });

        yield return Make(DiffNumber, "diff: computed score drifts beyond tolerance",
            "Numeric delta larger than the 0.01 absolute tolerance.",
            HttpMethodCode.GET, "/score",
            query: new() { ["q"] = "widgets" });

        yield return Make(DiffArray, "diff: orders list item differs",
            "Arrays are sorted by id first; one item's amount differs.",
            HttpMethodCode.GET, "/orders");

        yield return Make(DiffAuth, "diff: wrong secret value produces 401 on candidate",
            "Both calls carry a secret header; the fixture compares it against its own env, " +
            "and the candidate instance has a different expected key.",
            HttpMethodCode.GET, "/secure/whoami",
            headers: new() { ["X-Api-Key"] = "{{secret:RLAB_SECRET_DEMO_API_KEY}}" },
            refs: new() { "RLAB_SECRET_DEMO_API_KEY" });
    }
}
