using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RegressionLab.Api;
using RegressionLab.Api.Dtos;
using RegressionLab.Comparison;
using RegressionLab.Data;
using RegressionLab.Domain;
using RegressionLab.Security;
using RegressionLab.Services;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("LabDb")
                       ?? "Host=postgres;Database=regression_lab;Username=lab;Password=lab";

// Npgsql 8 requires an explicit opt-in for dynamic JSON (Dictionary<string,string> ↔ jsonb).
var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
dataSourceBuilder.EnableDynamicJson();
var dataSource = dataSourceBuilder.Build();
builder.Services.AddSingleton(dataSource);
builder.Services.AddDbContextFactory<LabDbContext>(o => o.UseNpgsql(dataSource));
builder.Services.AddHostedService<RunQueueService>();
builder.Services.AddSingleton<IRunQueue>(sp => sp.GetServices<IHostedService>()
    .OfType<RunQueueService>().Single());

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
});

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok", utc = DateTime.UtcNow }));

// ----- configuration -----
app.MapGet("/api/config", (IConfiguration config) =>
{
    var secretNames = (config["SECRET_NAMES"] ?? "RLAB_SECRET_DEMO_API_KEY")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    return Results.Ok(new ConfigDto(
        config["BASELINE_URL"] ?? "http://baseline:8080",
        config["CANDIDATE_URL"] ?? "http://candidate:8080",
        secretNames));
});

// ----- rule sets -----
app.MapGet("/api/rules", async (LabDbContext db) =>
    await db.RuleSets.OrderByDescending(r => r.CreatedAt).Select(r => r.ToDto()).ToListAsync());

app.MapPost("/api/rules", async (RuleSetRequest req, LabDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest(new { error = "Name is required" });
    ValidateRulePayload(req, out var error);
    if (error is not null) return Results.BadRequest(new { error });

    // Editing a rule set name creates a NEW version row; runs keep the version they used.
    var version = ((await db.RuleSets.AsNoTracking()
        .Where(r => r.Name == req.Name).Select(r => (int?)r.Version).MaxAsync()) ?? 0) + 1;
    // Remember the previous version BEFORE flipping flags (same unit of work can't
    // read its own uncommitted update through a fresh query).
    var previousId = version > 1
        ? await db.RuleSets.AsNoTracking()
            .Where(r => r.Name == req.Name)
            .OrderByDescending(r => r.Version).Select(r => (Guid?)r.Id).FirstAsync()
        : null;
    foreach (var r in await db.RuleSets.Where(r => r.Name == req.Name).ToListAsync())
        r.IsActive = false;

    var rule = new RuleSet
    {
        Id = Guid.NewGuid(),
        Name = req.Name.Trim(),
        Version = version,
        IsActive = true,
        IgnorePaths = req.IgnorePaths ?? new(),
        ArraySortKeys = req.ArraySortKeys ?? new(),
        NumericAbsTolerance = req.NumericAbsTolerance ?? 0,
        NumericRelTolerance = req.NumericRelTolerance ?? 0,
        DynamicPatterns = req.DynamicPatterns ?? new(),
        IgnoreHeaders = req.IgnoreHeaders ?? new(),
        CreatedAt = DateTime.UtcNow
    };
    // Insert the new version first so the FK target exists when scenarios move.
    db.RuleSets.Add(rule);
    await db.SaveChangesAsync();

    // Scenarios belong to the rule family (name); point them at the new version.
    // Historical runs are unaffected because they carry their own JSON snapshot.
    if (previousId is not null)
    {
        await db.Scenarios.Where(s => s.RuleSetId == previousId.Value)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RuleSetId, rule.Id));
    }
    return Results.Created($"/api/rules/{rule.Id}", rule.ToDto());
});

app.MapGet("/api/rules/{id:guid}", async Task<Results<Ok<RuleSetDto>, NotFound>> (Guid id, LabDbContext db) =>
{
    var r = await db.RuleSets.FindAsync(id);
    return r is null ? TypedResults.NotFound() : TypedResults.Ok(r.ToDto());
});

// ----- scenarios -----
app.MapGet("/api/scenarios", async (LabDbContext db, Guid? ruleSetId) =>
{
    var q = db.Scenarios.AsQueryable();
    if (ruleSetId.HasValue) q = q.Where(s => s.RuleSetId == ruleSetId.Value);
    return await q.OrderBy(s => s.Name).Select(s => s.ToDto()).ToListAsync();
});

app.MapPost("/api/scenarios", async (ScenarioRequest req, LabDbContext db, IConfiguration config) =>
{
    var validate = ValidateScenario(req, db, config, null);
    if (validate is not null) return Results.BadRequest(new { error = validate });

    var ruleSetId = req.RuleSetId ?? await db.RuleSets.Where(r => r.IsActive)
        .OrderByDescending(r => r.Version).Select(r => (Guid?)r.Id).FirstAsync();
    var now = DateTime.UtcNow;
    var s = new Scenario
    {
        Id = Guid.NewGuid(),
        Name = req.Name.Trim(),
        Description = req.Description,
        Method = Enum.Parse<HttpMethodCode>(req.Method ?? "GET", ignoreCase: true),
        PathTemplate = req.PathTemplate,
        PathParameters = req.PathParameters ?? new(),
        QueryParameters = req.QueryParameters ?? new(),
        Headers = req.Headers ?? new(),
        JsonBody = req.JsonBody,
        SecretRefs = CollectRefs(req),
        RuleSetId = ruleSetId!.Value,
        Enabled = req.Enabled ?? true,
        CreatedAt = now,
        UpdatedAt = now
    };
    db.Scenarios.Add(s);
    await db.SaveChangesAsync();
    return Results.Created($"/api/scenarios/{s.Id}", s.ToDto());
});

app.MapPut("/api/scenarios/{id:guid}", async (Guid id, ScenarioRequest req, LabDbContext db, IConfiguration config) =>
{
    var s = await db.Scenarios.FindAsync(id);
    if (s is null) return Results.NotFound();
    var validate = ValidateScenario(req, db, config, s);
    if (validate is not null) return Results.BadRequest(new { error = validate });

    s.Name = req.Name.Trim();
    s.Description = req.Description;
    s.Method = Enum.Parse<HttpMethodCode>(req.Method ?? "GET", ignoreCase: true);
    s.PathTemplate = req.PathTemplate;
    s.PathParameters = req.PathParameters ?? new();
    s.QueryParameters = req.QueryParameters ?? new();
    s.Headers = req.Headers ?? new();
    s.JsonBody = req.JsonBody;
    s.SecretRefs = CollectRefs(req);
    if (req.RuleSetId.HasValue) s.RuleSetId = req.RuleSetId.Value;
    s.Enabled = req.Enabled ?? s.Enabled;
    s.UpdatedAt = DateTime.UtcNow;
    await db.SaveChangesAsync();
    return Results.Ok(s.ToDto());
});

app.MapDelete("/api/scenarios/{id:guid}", async (Guid id, LabDbContext db) =>
{
    var s = await db.Scenarios.FindAsync(id);
    if (s is null) return Results.NotFound();
    db.Scenarios.Remove(s);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// ----- runs -----
app.MapPost("/api/runs", async (StartRunRequest req, LabDbContext db, IRunQueue queue, IConfiguration config) =>
{
    RuleSet? rule = req.RuleSetId.HasValue
        ? await db.RuleSets.FindAsync(req.RuleSetId.Value)
        : await db.RuleSets.Where(r => r.IsActive).OrderByDescending(r => r.Version).FirstAsync();
    if (rule is null) return Results.BadRequest(new { error = "No active rule set" });

    var baseline = req.BaselineBaseUrl ?? config["BASELINE_URL"] ?? "http://baseline:8080";
    var candidate = req.CandidateBaseUrl ?? config["CANDIDATE_URL"] ?? "http://candidate:8080";
    if (!Uri.TryCreate(baseline, UriKind.Absolute, out var bUri) || bUri.Scheme is not ("http" or "https"))
        return Results.BadRequest(new { error = "Invalid baseline URL" });
    if (!Uri.TryCreate(candidate, UriKind.Absolute, out var cUri) || cUri.Scheme is not ("http" or "https"))
        return Results.BadRequest(new { error = "Invalid candidate URL" });

    var opts = new RunOptions
    {
        Concurrency = Math.Clamp(req.Options?.Concurrency ?? 4, 1, 64),
        TimeoutMs = Math.Clamp(req.Options?.TimeoutMs ?? 10_000, 100, 120_000),
        BaselineRateLimit = Math.Max(0, req.Options?.BaselineRateLimit ?? 0),
        CandidateRateLimit = Math.Max(0, req.Options?.CandidateRateLimit ?? 0)
    };

    var scenarioCount = await db.Scenarios.CountAsync(s => s.Enabled && s.RuleSetId == rule.Id);
    var run = new Run
    {
        Id = Guid.NewGuid(),
        Status = RunStatus.Pending,
        RuleSetId = rule.Id,
        RuleSetVersion = rule.Version,
        RuleSetName = rule.Name,
        RuleSnapshotJson = RuleSettingsFactory.ToSnapshot(rule),
        BaselineBaseUrl = baseline.TrimEnd('/'),
        CandidateBaseUrl = candidate.TrimEnd('/'),
        Options = opts,
        TotalScenarios = scenarioCount,
        CreatedAt = DateTime.UtcNow
    };
    db.Runs.Add(run);
    await db.SaveChangesAsync();
    queue.Enqueue(run.Id);
    return Results.Created($"/api/runs/{run.Id}", run.ToSummaryDto());
});

app.MapGet("/api/runs", async (LabDbContext db) =>
{
    var runs = await db.Runs.OrderByDescending(r => r.CreatedAt).Take(100)
        .Select(r => r.ToSummaryDto()).ToListAsync();
    return Results.Ok(runs);
});

app.MapGet("/api/runs/{id:guid}", async Task<Results<Ok<RunSummaryDto>, NotFound>> (Guid id, LabDbContext db) =>
{
    var r = await db.Runs.FindAsync(id);
    return r is null ? TypedResults.NotFound() : TypedResults.Ok(r.ToSummaryDto());
});

app.MapPost("/api/runs/{id:guid}/cancel", (Guid id, IRunQueue queue) =>
    queue.Cancel(id) ? Results.Accepted() : Results.NotFound(new { error = "no active run with that id" }));

app.MapGet("/api/runs/{id:guid}/results", async (Guid id, LabDbContext db) =>
    // Materialize first: projecting out of jsonb-owned types with method calls
    // (enum.ToString) hits an EF Core 8 expression bug.
    (await db.RunResults.Where(r => r.RunId == id).OrderBy(r => r.OrderIndex)
        .ToListAsync())
    .Select(r => r.ToDto()).ToList());

// ----- helpers -----
static void ValidateRulePayload(RuleSetRequest req, out string? error)
{
    error = null;
    foreach (var p in req.IgnorePaths ?? new())
    {
        try { _ = PathPattern.Parse(p); }
        catch (FormatException ex) { error = ex.Message; return; }
    }
    foreach (var key in req.ArraySortKeys?.Keys ?? Enumerable.Empty<string>())
    {
        try { _ = PathPattern.Parse(key); }
        catch (FormatException ex) { error = ex.Message; return; }
    }
    foreach (var pair in req.DynamicPatterns ?? new())
    {
        try
        {
            _ = PathPattern.Parse(pair.Key);
            _ = new System.Text.RegularExpressions.Regex(pair.Value);
        }
        catch (Exception ex) { error = ex.Message; return; }
    }
    if ((req.NumericAbsTolerance ?? 0) < 0 || (req.NumericRelTolerance ?? 0) < 0)
        error = "Tolerances must be non-negative";
}

static string? ValidateScenario(ScenarioRequest req, LabDbContext db, IConfiguration config, Scenario? existing)
{
    if (string.IsNullOrWhiteSpace(req.Name)) return "Name is required";
    if (string.IsNullOrWhiteSpace(req.PathTemplate) || !req.PathTemplate.StartsWith('/'))
        return "PathTemplate must start with '/'";
    if (!string.IsNullOrWhiteSpace(req.JsonBody))
    {
        try { _ = System.Text.Json.JsonDocument.Parse(req.JsonBody); }
        catch (System.Text.Json.JsonException ex) { return $"Invalid JSON body: {ex.Message}"; }
    }
    if (req.Method is not null && !Enum.TryParse<HttpMethodCode>(req.Method, true, out _))
        return $"Unknown method: {req.Method}";

    // Never accept a raw env secret value pasted into the scenario.
    var secretNames = (config["SECRET_NAMES"] ?? "RLAB_SECRET_DEMO_API_KEY")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    var known = secretNames.ToDictionary(n => n, n => Environment.GetEnvironmentVariable(n));
    var fields = new[] { req.PathTemplate, req.JsonBody }
        .Concat(req.PathParameters?.Values ?? Enumerable.Empty<string>())
        .Concat(req.QueryParameters?.Values ?? Enumerable.Empty<string>())
        .Concat(req.Headers?.Values ?? Enumerable.Empty<string>());
    try { SecretRedactor.AssertNoRawSecrets(fields, known); }
    catch (RawSecretSubmittedException ex) { return ex.Message; }

    return null;
}

static List<string> CollectRefs(ScenarioRequest req)
{
    var fields = new[] { req.PathTemplate, req.JsonBody }
        .Concat(req.PathParameters?.Values ?? Enumerable.Empty<string>())
        .Concat(req.QueryParameters?.Values ?? Enumerable.Empty<string>())
        .Concat(req.Headers?.Values ?? Enumerable.Empty<string>());
    return fields.SelectMany(SecretResolver.ExtractRefs).Distinct().OrderBy(x => x).ToList();
}

// ----- startup: wait for DB, migrate, seed -----
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<LabDbContext>();
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    for (var attempt = 1; ; attempt++)
    {
        try
        {
            await db.Database.MigrateAsync();
            // Use a fresh context for seeding: a failed attempt leaves the change tracker dirty.
            await using var seedDb = scope.ServiceProvider.GetRequiredService<IDbContextFactory<LabDbContext>>()
                .CreateDbContext();
            await Seeder.SeedAsync(seedDb);
            break;
        }
        catch (Exception ex) when (attempt < 30)
        {
            logger.LogWarning("Database not ready (attempt {Attempt}): {Message}", attempt, ex.Message);
            await Task.Delay(2000);
        }
    }
}

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program { }
