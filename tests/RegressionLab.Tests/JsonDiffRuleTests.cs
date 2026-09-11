using RegressionLab.Comparison;
using RegressionLab.Domain;
using Xunit;

namespace RegressionLab.Tests;

public class JsonDiffRuleTests
{
    private static List<DiffEntry> Diff(string a, string b, RuleSettings rule) =>
        new JsonDiffer(rule).CompareBodies(a, true, b, true);

    [Fact]
    public void Identical_documents_have_no_diffs()
    {
        var rule = TestRules.Create();
        Assert.Empty(Diff("""{"a":1,"b":[1,2,3],"c":{"x":"y"}}""",
                         """{"a":1,"b":[1,2,3],"c":{"x":"y"}}""", rule));
    }

    // ---------- ignore paths ----------

    [Fact]
    public void IgnorePath_skips_value_difference_at_that_path()
    {
        var rule = TestRules.Create(ignore: new() { "$.meta.request_id" });
        var diffs = Diff(
            """{"meta":{"request_id":"aaa"},"ok":true}""",
            """{"meta":{"request_id":"bbb"},"ok":true}""", rule);
        Assert.Empty(diffs);
    }

    [Fact]
    public void IgnorePath_with_wildcard_covers_all_array_elements()
    {
        var rule = TestRules.Create(ignore: new() { "$.items[*].ts" });
        var diffs = Diff(
            """{"items":[{"id":1,"ts":"t1"},{"id":2,"ts":"t2"}]}""",
            """{"items":[{"id":1,"ts":"x1"},{"id":2,"ts":"x2"}]}""", rule);
        Assert.Empty(diffs);
    }

    [Fact]
    public void IgnorePath_prefix_ignores_whole_subtree_including_missing_children()
    {
        var rule = TestRules.Create(ignore: new() { "$.meta" });
        var diffs = Diff(
            """{"meta":{"a":1,"b":2},"v":1}""",
            """{"meta":{"a":999},"v":1}""", rule);
        Assert.Empty(diffs);
    }

    [Fact]
    public void Differences_outside_ignore_path_are_still_reported()
    {
        var rule = TestRules.Create(ignore: new() { "$.meta.request_id" });
        var diffs = Diff(
            """{"meta":{"request_id":"a"},"v":1}""",
            """{"meta":{"request_id":"b"},"v":2}""", rule);
        Assert.Single(diffs);
        Assert.Equal("$.v", diffs[0].Path);
    }

    // ---------- array key sorting ----------

    [Fact]
    public void Arrays_sorted_by_key_ignore_insertion_order()
    {
        var rule = TestRules.Create(sortKeys: new() { ["$.items"] = "id" });
        var diffs = Diff(
            """{"items":[{"id":3,"v":"c"},{"id":1,"v":"a"},{"id":2,"v":"b"}]}""",
            """{"items":[{"id":1,"v":"a"},{"id":2,"v":"b"},{"id":3,"v":"c"}]}""", rule);
        Assert.Empty(diffs);
    }

    [Fact]
    public void Sorted_arraries_still_report_real_value_diffs_with_sorted_index_path()
    {
        var rule = TestRules.Create(sortKeys: new() { ["$.items"] = "id" });
        var diffs = Diff(
            """{"items":[{"id":1,"v":"a"},{"id":2,"v":"b"}]}""",
            """{"items":[{"id":2,"v":"DIFF"},{"id":1,"v":"a"}]}""", rule);
        Assert.Single(diffs);
        Assert.Equal("$.items[1].v", diffs[0].Path);
        Assert.Equal("b", diffs[0].Expected);
        Assert.Equal("DIFF", diffs[0].Actual);
    }

    [Fact]
    public void Without_sort_key_array_order_matters()
    {
        var rule = TestRules.Create();
        var diffs = Diff(
            """[{"id":1},{"id":2}]""", """[{"id":2},{"id":1}]""", rule);
        Assert.Contains(diffs, d => d.Path == "$[0].id");
    }

    [Fact]
    public void Array_length_difference_is_reported_even_when_sorted()
    {
        var rule = TestRules.Create(sortKeys: new() { ["$.items"] = "id" });
        var diffs = Diff(
            """{"items":[{"id":1},{"id":2}]}""",
            """{"items":[{"id":1}]}""", rule);
        Assert.Contains(diffs, d => d.Kind == "array_length");
    }

    // ---------- numeric tolerance ----------

    [Fact]
    public void Float_difference_within_absolute_tolerance_matches()
    {
        var rule = TestRules.Create(absTol: 0.01);
        Assert.Empty(Diff("""{"v":72.500}""", """{"v":72.505}""", rule));
    }

    [Fact]
    public void Float_difference_beyond_absolute_tolerance_is_numeric_delta()
    {
        var rule = TestRules.Create(absTol: 0.01);
        var diffs = Diff("""{"v":98.60}""", """{"v":98.75}""", rule);
        Assert.Single(diffs);
        Assert.Equal("numeric_delta", diffs[0].Kind);
        Assert.Equal("$.v", diffs[0].Path);
    }

    [Fact]
    public void Relative_tolerance_scales_with_magnitude()
    {
        var rule = TestRules.Create(absTol: 0, relTol: 0.01);
        // 0.5% drift at magnitude 1000 -> tolerance 10 -> match
        Assert.Empty(Diff("""{"v":1000.0}""", """{"v":1005.0}""", rule));
        // Same absolute drift at magnitude 1 -> tolerance 0.01 -> diff
        var diffs = Diff("""{"v":1.0}""", """{"v":5.0}""", rule);
        Assert.Single(diffs);
    }

    [Fact]
    public void Integer_vs_float_that_differ_are_reported()
    {
        var rule = TestRules.Create(absTol: 0.0001);
        var diffs = Diff("""{"v":1}""", """{"v":2}""", rule);
        Assert.Single(diffs);
    }

    // ---------- dynamic value masking ----------

    [Fact]
    public void Dynamic_regex_masks_values_matching_on_both_sides()
    {
        var rule = TestRules.Create(dynamic: new()
        {
            ["$.generated_at"] = @"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z"
        });
        var diffs = Diff(
            """{"generated_at":"2026-01-01T00:00:00Z"}""",
            """{"generated_at":"2026-09-10T12:34:56Z"}""", rule);
        Assert.Empty(diffs);
    }

    [Fact]
    public void Value_matching_regex_on_only_one_side_is_a_diff()
    {
        var rule = TestRules.Create(dynamic: new()
        {
            ["$.token"] = "token-[a-z]+"
        });
        var diffs = Diff(
            """{"token":"token-abc"}""",
            """{"token":"not-a-token"}""", rule);
        Assert.Single(diffs);
        Assert.Equal("dynamic_mismatch", diffs[0].Kind);
    }

    [Fact]
    public void Dynamic_pattern_does_not_apply_to_other_paths()
    {
        var rule = TestRules.Create(dynamic: new() { ["$.a"] = ".*" });
        var diffs = Diff("""{"a":"x","b":"1"}""", """{"a":"y","b":"2"}""", rule);
        Assert.Single(diffs);
        Assert.Equal("$.b", diffs[0].Path);
    }

    // ---------- structural ----------

    [Fact]
    public void Missing_and_extra_properties_are_distinct_kinds()
    {
        var rule = TestRules.Create();
        var diffs = Diff("""{"a":1,"b":2}""", """{"a":1,"c":3}""", rule);
        Assert.Contains(diffs, d => d.Path == "$.b" && d.Kind == "missing");
        Assert.Contains(diffs, d => d.Path == "$.c" && d.Kind == "extra");
    }

    [Fact]
    public void Type_mismatch_string_vs_number_is_reported()
    {
        var rule = TestRules.Create();
        var diffs = Diff("""{"v":"1"}""", """{"v":1}""", rule);
        Assert.Single(diffs);
        Assert.Equal("type_mismatch", diffs[0].Kind);
    }

    [Fact]
    public void Null_comparisons_are_consistent()
    {
        var rule = TestRules.Create();
        Assert.Empty(Diff("""{"v":null}""", """{"v":null}""", rule));
        var diffs = Diff("""{"v":null}""", """{"v":"x"}""", rule);
        Assert.Single(diffs);
    }

    [Fact]
    public void Non_json_bodies_are_compared_as_text()
    {
        var rule = TestRules.Create();
        var diffs = new JsonDiffer(rule)
            .CompareBodies("hello", false, "helly", false);
        Assert.Single(diffs);
        Assert.Equal("text_mismatch", diffs[0].Kind);
    }

    [Fact]
    public void Mixed_json_and_text_bodies_report_format_mismatch()
    {
        var rule = TestRules.Create();
        var diffs = new JsonDiffer(rule)
            .CompareBodies("{}", true, "plain text", false);
        Assert.Single(diffs);
        Assert.Equal("type_mismatch", diffs[0].Kind);
    }

    // ---------- versioning ----------

    [Fact]
    public void Rule_settings_round_trip_through_snapshot()
    {
        var original = TestRules.Create(
            ignore: new() { "$.a", "$.b[*].c" },
            sortKeys: new() { ["$.items"] = "id" },
            absTol: 0.25, relTol: 0.02,
            dynamic: new() { ["$.ts"] = "\\d+" },
            ignoreHeaders: new() { "x-custom" });

        var entity = new RuleSet
        {
            Version = 7,
            IgnorePaths = new() { "$.a", "$.b[*].c" },
            ArraySortKeys = new() { ["$.items"] = "id" },
            NumericAbsTolerance = 0.25,
            NumericRelTolerance = 0.02,
            DynamicPatterns = new() { ["$.ts"] = "\\d+" },
            IgnoreHeaders = new() { "x-custom" }
        };
        var json = RuleSettingsFactory.ToSnapshot(entity);
        var restored = RuleSettingsFactory.FromSnapshot(json);

        Assert.Equal(7, restored.Version);
        Assert.Equal(2, restored.IgnorePaths.Count);
        Assert.Equal("id", restored.ArraySortKeys.Single().Value);
        Assert.Equal(0.25, restored.NumericAbsTolerance);
        Assert.True(restored.IgnoreHeaders.Contains("x-custom"));
        // Hop-by-hop defaults are always present.
        Assert.True(restored.IgnoreHeaders.Contains("date"));
    }

    [Fact]
    public void Invalid_path_pattern_throws_format_exception()
    {
        Assert.Throws<FormatException>(() => PathPattern.Parse("items[0]"));
        Assert.Throws<FormatException>(() => PathPattern.Parse("$.items["));
    }
}
