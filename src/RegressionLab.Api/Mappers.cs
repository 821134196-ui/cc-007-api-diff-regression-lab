using RegressionLab.Api.Dtos;
using RegressionLab.Domain;

namespace RegressionLab.Api;

public static class Mappers
{
    public static ScenarioDto ToDto(this Scenario s) => new(
        s.Id, s.Name, s.Description, s.Method.ToString(), s.PathTemplate,
        s.PathParameters, s.QueryParameters, s.Headers, s.JsonBody,
        s.SecretRefs, s.RuleSetId, s.Enabled, s.UpdatedAt);

    public static RuleSetDto ToDto(this RuleSet r) => new(
        r.Id, r.Name, r.Version, r.IsActive,
        r.IgnorePaths, r.ArraySortKeys, r.NumericAbsTolerance, r.NumericRelTolerance,
        r.DynamicPatterns, r.IgnoreHeaders, r.CreatedAt);

    public static RunSummaryDto ToSummaryDto(this Run r) => new(
        r.Id, r.Status.ToString(), r.RuleSetId, r.RuleSetVersion, r.RuleSetName,
        r.BaselineBaseUrl, r.CandidateBaseUrl, r.Options,
        r.TotalScenarios, r.CompletedScenarios, r.DiffCount, r.NetworkFailureCount,
        r.CreatedAt, r.StartedAt, r.FinishedAt);

    public static TargetCallDto ToDto(this TargetCall c) => new(
        c.Reached, c.StatusCode, c.ErrorKind, c.ErrorDetail,
        c.Headers, c.BodyText, c.BodyIsJson, c.ElapsedMs);

    public static RunResultDto ToDto(this RunResult r) => new(
        r.Id, r.ScenarioId, r.ScenarioName, r.OrderIndex,
        r.Outcome.ToString(), r.Summary,
        r.Baseline.ToDto(), r.Candidate.ToDto(),
        r.Diffs.Select(d => new DiffEntryDto(
            d.Area.ToString(), d.Path, d.Expected, d.Actual, d.Kind)).ToList(),
        r.CompletedAt);
}
