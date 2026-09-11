using RegressionLab.Comparison;

namespace RegressionLab.Tests;

internal static class TestRules
{
    public static RuleSettings Create(
        List<string>? ignore = null,
        Dictionary<string, string>? sortKeys = null,
        double absTol = 0, double relTol = 0,
        Dictionary<string, string>? dynamic = null,
        List<string>? ignoreHeaders = null,
        double timingAbsMs = 100, double timingRel = 0.25)
    {
        var rs = new Domain.RuleSet
        {
            Version = 1,
            IgnorePaths = ignore ?? new(),
            ArraySortKeys = sortKeys ?? new(),
            NumericAbsTolerance = absTol,
            NumericRelTolerance = relTol,
            DynamicPatterns = dynamic ?? new(),
            IgnoreHeaders = ignoreHeaders ?? new()
        };
        return RuleSettingsFactory.FromEntity(rs);
    }
}
