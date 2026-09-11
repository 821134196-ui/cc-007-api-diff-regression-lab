using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

// One image, two personalities: FIXTURE_MODE=baseline | candidate.
var mode = Environment.GetEnvironmentVariable("FIXTURE_MODE")?.ToLowerInvariant() switch
{
    "candidate" => Mode.Candidate,
    _ => Mode.Baseline
};
var expectedApiKey = Environment.GetEnvironmentVariable("EXPECTED_API_KEY")
                     ?? "demo-key-baseline-123456";

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);
var app = builder.Build();

app.Use(async (ctx, next) =>
{
    // Identical on both instances for stable scenarios, different instance label
    // (ignored by the default rule set's IgnoreHeaders).
    ctx.Response.Headers["X-Fixture-Instance"] = mode.ToString().ToLowerInvariant();
    ctx.Response.Headers["X-Fixture-Service"] = "regression-lab-fixture";
    await next();
});

// ---------- identical on both ----------

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    service = "regression-lab-fixture",
    version = "1.4.2",
    // Dynamic timestamp: masked by the default rule set's $.generated_at pattern.
    generatedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
    meta = new
    {
        // Ignored paths: different per request/instance, never reported.
        request_id = Guid.NewGuid().ToString("N"),
        server_time = DateTime.UtcNow.ToString("O")
    }
}));

app.MapGet("/users/{id:int}", (int id) =>
{
    if (id == 9)
    {
        // BODY DIFF: candidate migrated to a new email domain.
        return Results.Ok(new
        {
            id,
            name = "Carol Chen",
            role = "billing",
            email = mode == Mode.Candidate ? "carol.chen@newmail.example" : "carol@example.com",
            active = true,
            generatedAt = NowIso(),
            meta = new { request_id = Rand(), server_time = NowIso() }
        });
    }

    // id 7: body identical, header differs below via endpoint-specific post-processing.
    var user = new
    {
        id,
        name = id == 42 ? "Ada Lovelace" : id == 7 ? "Bob Miller" : $"User {id}",
        role = id == 42 ? "admin" : "viewer",
        email = id == 42 ? "ada@example.com" : $"user{id}@example.com",
        active = true,
        // Floating point drift within the 0.01 absolute tolerance => still a match.
        weight = mode == Mode.Candidate ? 72.505 : 72.500,
        generatedAt = NowIso(),
        meta = new { request_id = Rand(), server_time = NowIso() }
    };

    if (id == 7)
    {
        // HEADER DIFF: candidate changed its advertised rate limit.
        return Results.Json(user, statusCode: 200, contentType: "application/json")
            .WithHeader(mode == Mode.Candidate ? "200" : "100");
    }
    return Results.Ok(user);
});

app.MapGet("/users", (string? role, bool? active) => Results.Ok(new
{
    items = new object[]
    {
        new { id = 1, name = "Ada Lovelace", role = role ?? "admin", active = active ?? true },
        new { id = 2, name = "Bob Miller", role = role ?? "admin", active = active ?? true }
    },
    count = 2,
    generatedAt = NowIso(),
    meta = new { request_id = Rand(), server_time = NowIso() }
}));

app.MapPost("/echo", async (HttpContext ctx) =>
{
    using var doc = await JsonDocument.ParseAsync(ctx.Request.Body);
    var key = ctx.Request.Headers["X-Api-Key"].FirstOrDefault();
    // The key value is a resolved server secret; the lab redacts it in stored results.
    return Results.Ok(new
    {
        echo = doc.RootElement.Clone(),
        receivedKey = key,
        generatedAt = NowIso()
    });
});

app.MapGet("/slow/{ms:int}", async (int ms) =>
{
    await Task.Delay(Math.Clamp(ms, 0, 60_000));
    return Results.Ok(new { sleptMs = ms });
});

// ---------- different on candidate ----------

app.MapPost("/users", ([FromBody] JsonElement body) =>
{
    var created = new
    {
        id = mode == Mode.Candidate ? 101 : 100,
        name = body.TryGetProperty("name", out var n) ? n.GetString() : null,
        role = body.TryGetProperty("role", out var r) ? r.GetString() : "viewer",
        generatedAt = NowIso()
    };
    // STATUS DIFF: baseline returns 200 OK, candidate follows the 201 Created convention.
    return mode == Mode.Candidate
        ? Results.Json(created, statusCode: 201)
        : Results.Json(created, statusCode: 200);
});

app.MapGet("/score", (string? q) =>
{
    // NUMERIC DIFF: 0.15 drift, larger than the default 0.01 absolute tolerance.
    var score = mode == Mode.Candidate ? 98.75 : 98.60;
    return Results.Ok(new
    {
        query = q ?? "",
        score,
        // Same value both sides within tolerance to prove tolerance works the other way.
        noise = mode == Mode.Candidate ? 0.002 : 0.001,
        generatedAt = NowIso()
    });
});

app.MapGet("/orders", () =>
{
    object Order(int id, string name, double amount) => new { id, name, amount };

    // ARRAY DIFF: the two services return rows in different insertion order
    // (default rule sorts $.items by id) AND candidate recomputed one amount.
    object[] baseline =
    {
        Order(3, "gamma", 30.00),
        Order(1, "alpha", 10.00),
        Order(2, "beta", 20.00)
    };
    object[] candidate =
    {
        Order(1, "alpha", 10.00),
        Order(3, "gamma", 33.00),
        Order(2, "beta", 20.00)
    };
    return Results.Ok(new
    {
        items = mode == Mode.Candidate ? candidate : baseline,
        generatedAt = NowIso(),
        meta = new { request_id = Rand(), server_time = NowIso() }
    });
});

app.MapGet("/secure/whoami", (HttpContext ctx) =>
{
    var key = ctx.Request.Headers["X-Api-Key"].FirstOrDefault();
    if (key != expectedApiKey)
        return Results.Json(new { error = "invalid api key", generatedAt = NowIso() }, statusCode: 401);
    return Results.Ok(new
    {
        principal = "demo-user",
        scope = new[] { "read", "regression" },
        generatedAt = NowIso()
    });
});

app.Run();

static string NowIso() => DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
static string Rand() => Guid.NewGuid().ToString("N");

enum Mode { Baseline, Candidate }

/// <summary>Tiny helper so the id==7 branch can attach its diff header.</summary>
internal static class ResultExtensions
{
    public static IResult WithHeader(this IResult result, string rateLimit) =>
        new HeaderResult(result, rateLimit);

    private sealed class HeaderResult : IResult
    {
        private readonly IResult _inner;
        private readonly string _rateLimit;
        public HeaderResult(IResult inner, string rateLimit) { _inner = inner; _rateLimit = rateLimit; }
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.Headers["X-Rate-Limit"] = _rateLimit;
            await _inner.ExecuteAsync(httpContext);
        }
    }
}
