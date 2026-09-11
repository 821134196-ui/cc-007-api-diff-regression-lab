using System.ComponentModel.DataAnnotations;
using RegressionLab.Domain;

namespace RegressionLab.Api.Dtos;

public record ScenarioDto(
    Guid Id,
    string Name,
    string? Description,
    string Method,
    string PathTemplate,
    Dictionary<string, string> PathParameters,
    Dictionary<string, string> QueryParameters,
    Dictionary<string, string> Headers,
    string? JsonBody,
    List<string> SecretRefs,
    Guid RuleSetId,
    bool Enabled,
    DateTime UpdatedAt);

public record ScenarioRequest(
    [Required] string Name,
    string? Description,
    string? Method,
    [Required] string PathTemplate,
    Dictionary<string, string>? PathParameters,
    Dictionary<string, string>? QueryParameters,
    Dictionary<string, string>? Headers,
    string? JsonBody,
    Guid? RuleSetId,
    bool? Enabled);

public record RuleSetDto(
    Guid Id,
    string Name,
    int Version,
    bool IsActive,
    List<string> IgnorePaths,
    Dictionary<string, string> ArraySortKeys,
    double NumericAbsTolerance,
    double NumericRelTolerance,
    Dictionary<string, string> DynamicPatterns,
    List<string> IgnoreHeaders,
    DateTime CreatedAt);

public record RuleSetRequest(
    [Required] string Name,
    List<string>? IgnorePaths,
    Dictionary<string, string>? ArraySortKeys,
    double? NumericAbsTolerance,
    double? NumericRelTolerance,
    Dictionary<string, string>? DynamicPatterns,
    List<string>? IgnoreHeaders);

public record RunOptionsRequest(
    int? Concurrency,
    int? TimeoutMs,
    double? BaselineRateLimit,
    double? CandidateRateLimit);

public record StartRunRequest(
    Guid? RuleSetId,
    string? BaselineBaseUrl,
    string? CandidateBaseUrl,
    RunOptionsRequest? Options);

public record RunSummaryDto(
    Guid Id,
    string Status,
    Guid RuleSetId,
    int RuleSetVersion,
    string RuleSetName,
    string BaselineBaseUrl,
    string CandidateBaseUrl,
    RunOptions Options,
    int TotalScenarios,
    int CompletedScenarios,
    int DiffCount,
    int NetworkFailureCount,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? FinishedAt);

public record DiffEntryDto(
    string Area,
    string Path,
    string? Expected,
    string? Actual,
    string? Kind);

public record TargetCallDto(
    bool Reached,
    int? StatusCode,
    string? ErrorKind,
    string? ErrorDetail,
    Dictionary<string, string> Headers,
    string? BodyText,
    bool BodyIsJson,
    long ElapsedMs);

public record RunResultDto(
    Guid Id,
    Guid ScenarioId,
    string ScenarioName,
    int OrderIndex,
    string Outcome,
    string? Summary,
    TargetCallDto Baseline,
    TargetCallDto Candidate,
    List<DiffEntryDto> Diffs,
    DateTime CompletedAt);

public record ConfigDto(
    string BaselineBaseUrl,
    string CandidateBaseUrl,
    IReadOnlyList<string> SecretNames);
