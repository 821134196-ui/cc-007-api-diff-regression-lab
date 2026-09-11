using System.ComponentModel.DataAnnotations;

namespace RegressionLab.Domain;

public enum HttpMethodCode
{
    GET = 0,
    POST = 1,
    PUT = 2,
    PATCH = 3,
    DELETE = 4
}

/// <summary>
/// A request scenario. Path/query/header/body templates may contain {{secret:NAME}}
/// placeholders; the placeholder itself is persisted, never the resolved value.
/// </summary>
public class Scenario
{
    public Guid Id { get; set; }

    [Required, StringLength(120)]
    public string Name { get; set; } = "";

    [StringLength(500)]
    public string? Description { get; set; }

    public HttpMethodCode Method { get; set; } = HttpMethodCode.GET;

    /// <summary>Request path WITH query string, e.g. /users/{id}?verbose=true. {id} is a path parameter.</summary>
    [Required, StringLength(1000)]
    public string PathTemplate { get; set; } = "";

    /// <summary>Path parameter values keyed by name (may contain {{secret:NAME}}).</summary>
    public Dictionary<string, string> PathParameters { get; set; } = new();

    /// <summary>Extra query parameters merged onto PathTemplate (may contain secrets).</summary>
    public Dictionary<string, string> QueryParameters { get; set; } = new();

    /// <summary>Request headers (values may contain {{secret:NAME}}).</summary>
    public Dictionary<string, string> Headers { get; set; } = new();

    /// <summary>JSON request body as raw text (may contain secrets inside strings).</summary>
    public string? JsonBody { get; set; }

    /// <summary>Names of secrets this scenario references (server-side allow-list for resolution).</summary>
    public List<string> SecretRefs { get; set; } = new();

    public Guid RuleSetId { get; set; }
    public RuleSet? RuleSet { get; set; }

    public bool Enabled { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
