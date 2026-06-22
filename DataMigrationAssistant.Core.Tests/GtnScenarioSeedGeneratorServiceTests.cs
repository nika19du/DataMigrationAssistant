using DataMigrationAssistant.Core.Models;
using DataMigrationAssistant.Core.Services;

namespace DataMigrationAssistant.Core.Tests;

public sealed class GtnScenarioSeedGeneratorServiceTests
{
    private readonly GtnScenarioSeedGeneratorService _sut = new();

    // ── ID normalisation ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("1,01",  "1.01")]
    [InlineData("14,02", "14.02")]
    [InlineData("1.01",  "1.01")]
    [InlineData("14.02", "14.02")]
    public void NormalizeScenarioId_CommaToDot(string input, string expected)
    {
        Assert.Equal(expected, GtnScenarioSeedGeneratorService.NormalizeScenarioId(input));
    }

    // ── ID integer generation ──────────────────────────────────────────────────

    [Theory]
    [InlineData("1.01",  101)]
    [InlineData("14.02", 1402)]
    [InlineData("2.10",  210)]
    [InlineData("100",   100)]
    public void TryParseId_ValidId_ReturnsTrueAndExpectedInt(string normalizedId, int expected)
    {
        var ok = GtnScenarioSeedGeneratorService.TryParseId(normalizedId, out var id);
        Assert.True(ok);
        Assert.Equal(expected, id);
    }

    [Theory]
    [InlineData("ABC")]
    [InlineData("A.01")]
    [InlineData("")]
    public void TryParseId_InvalidId_ReturnsFalse(string normalizedId)
    {
        Assert.False(GtnScenarioSeedGeneratorService.TryParseId(normalizedId, out _));
    }

    // ── Validation group mapping ───────────────────────────────────────────────

    [Fact]
    public void Generate_KnownGroup_MapsToExpectedInt()
    {
        var (key, value) = GtnScenarioSeedGeneratorService.ValidationGroupMap.First();
        var sql = GenerateSingle(group: key);
        Assert.Contains($", {value},", sql);
    }

    [Theory]
    [InlineData("RECURRING SALARY",   1)]
    [InlineData("RECURRING STANDARD", 2)]
    [InlineData("RECURRING %",        3)]
    [InlineData("RECURRING HOURLY",   4)]
    [InlineData("VARIABLE UNIT",      5)]
    [InlineData("VARIABLE HOURLY",    6)]
    [InlineData("VARIABLE VALUE",     7)]
    [InlineData("ABSENCE",            8)]
    [InlineData("RPE IS MISSING IPE", 9)]
    [InlineData("CALCULATED",         10)]
    [InlineData("NO INPUT",           11)]
    [InlineData("Net Pay",            12)]
    [InlineData("Multi IPE Mapping",  13)]
    [InlineData("Advance Pay",        14)]
    public void ValidationGroupMap_ContainsExpectedEntries(string key, int expectedId)
    {
        Assert.True(GtnScenarioSeedGeneratorService.ValidationGroupMap.TryGetValue(key, out var id));
        Assert.Equal(expectedId, id);
    }

    [Fact]
    public void Generate_UnknownGroup_EmitsNull()
    {
        var sql = GenerateSingle(group: "__not_a_group__");
        Assert.Contains("NULL,", sql);
    }

    [Fact]
    public void Generate_UnknownGroup_AddsWarning()
    {
        var result = Run(group: "__not_a_group__");
        Assert.Single(result.Warnings);
        Assert.Equal("group", result.Warnings[0].Column);
        Assert.Contains("__not_a_group__", result.Warnings[0].Message);
    }

    // ── system_pay_elements JSONB ──────────────────────────────────────────────

    [Fact]
    public void Generate_KnownPayElement_EmitsSingleElementJsonb()
    {
        var (key, value) = GtnScenarioSeedGeneratorService.SystemPayElementMap.First();
        var sql = GenerateSingle(systemElementType: key);
        Assert.Contains($"'[{value}]'::jsonb", sql);
    }

    [Fact]
    public void Generate_TwoKnownPayElements_EmitsTwoElementJsonb()
    {
        // Use "Earning" and "Notional Pay" — known distinct entries
        var sql = GenerateSingle(systemElementType: "Earning, Notional Pay");
        Assert.Contains("'[1,2]'::jsonb", sql);
    }

    [Fact]
    public void Generate_EmptyPayElement_EmitsEmptyJsonb()
    {
        var sql = GenerateSingle(systemElementType: null);
        Assert.Contains("'[]'::jsonb", sql);
    }

    [Fact]
    public void Generate_UnknownPayElement_AddsWarningAndSkipsValue()
    {
        var result = Run(systemElementType: "__bad_element__");
        Assert.Single(result.Warnings);
        Assert.Equal("system_element_type", result.Warnings[0].Column);
        Assert.Contains("'[]'::jsonb", result.ScenariosSql);
    }

    // ── Alias entries in SystemPayElementMap ──────────────────────────────────

    [Theory]
    [InlineData("Earning",  1)]
    [InlineData("Earnings", 1)]   // alias → same ID as "Earning"
    [InlineData("Notional Pay", 2)]
    [InlineData("Notional",     2)]   // alias → same ID as "Notional Pay"
    public void SystemPayElementMap_AliasesMappedToCanonicalId(string key, int expectedId)
    {
        Assert.True(GtnScenarioSeedGeneratorService.SystemPayElementMap.TryGetValue(key, out var id));
        Assert.Equal(expectedId, id);
    }

    [Fact]
    public void SystemPayElementMap_NetPayMapsCorrectly()
    {
        Assert.True(GtnScenarioSeedGeneratorService.SystemPayElementMap.TryGetValue("Net Pay", out var id));
        Assert.Equal(15, id);
    }

    [Fact]
    public void Generate_NetPayElement_EmitsJsonbAndNoWarning()
    {
        var result = Run(systemElementType: "Net Pay");
        Assert.Empty(result.Warnings);
        Assert.Contains("'[15]'::jsonb", result.ScenariosSql);
    }

    [Fact]
    public void Generate_AliasPayElement_ProducesSameJsonbAsCanonical()
    {
        var canonicalSql = GenerateSingle(systemElementType: "Earning");
        var aliasSql     = GenerateSingle(systemElementType: "Earnings");
        Assert.Equal(
            ExtractJsonb(canonicalSql, "system_pay_elements"),
            ExtractJsonb(aliasSql,     "system_pay_elements"));
    }

    // ── assignment_status JSONB ────────────────────────────────────────────────

    [Fact]
    public void Generate_KnownStatus_EmitsStatusJsonb()
    {
        var (key, value) = GtnScenarioSeedGeneratorService.AssignmentStatusMap.First();
        var sql = GenerateSingle(eeStatus: key);
        Assert.Contains($"'[{value}]'::jsonb", sql);
    }

    [Theory]
    [InlineData("Joiner", 6)]
    [InlineData("Leaver", 7)]
    public void AssignmentStatusMap_ContainsRealDataValues(string key, int expectedId)
    {
        Assert.True(GtnScenarioSeedGeneratorService.AssignmentStatusMap.TryGetValue(key, out var id));
        Assert.Equal(expectedId, id);
    }

    [Fact]
    public void Generate_EmptyStatus_EmitsEmptyJsonb()
    {
        var sql = GenerateSingle(eeStatus: null);
        var count = CountOccurrences(sql, "'[]'::jsonb");
        Assert.True(count >= 1);
    }

    [Fact]
    public void Generate_UnknownStatus_AddsWarning()
    {
        var result = Run(eeStatus: "__bad_status__");
        Assert.Single(result.Warnings);
        Assert.Equal("ee_status", result.Warnings[0].Column);
    }

    // ── ElementRule1Map ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Salary",   1)]
    [InlineData("Standard", 2)]
    [InlineData("Percent",  3)]
    [InlineData("Hourly",   4)]
    [InlineData("Unit",     5)]
    [InlineData("Value",    6)]
    public void ElementRule1Map_ContainsExpectedEntries(string key, int expectedId)
    {
        Assert.True(GtnScenarioSeedGeneratorService.ElementRule1Map.TryGetValue(key, out var id));
        Assert.Equal(expectedId, id);
    }

    [Fact]
    public void Generate_KnownElementRule1_EmitsIntAndNoWarning()
    {
        var (key, _) = GtnScenarioSeedGeneratorService.ElementRule1Map.First();
        var result = Run(elementRule1: key);
        Assert.Empty(result.Warnings);
        Assert.Contains("INSERT INTO", result.ScenariosSql);
    }

    [Fact]
    public void Generate_UnknownElementRule1_AddsWarning()
    {
        var result = Run(elementRule1: "__bad_rule__");
        Assert.Single(result.Warnings);
        Assert.Equal("element_rule_1", result.Warnings[0].Column);
    }

    // ── ElementRule2Map ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Derived",          1)]
    [InlineData("Specified Rate",   2)]
    [InlineData("Employee Rate",    3)]
    [InlineData("Bonus/Commission", 4)]
    public void ElementRule2Map_ContainsExpectedEntries(string key, int expectedId)
    {
        Assert.True(GtnScenarioSeedGeneratorService.ElementRule2Map.TryGetValue(key, out var id));
        Assert.Equal(expectedId, id);
    }

    [Fact]
    public void Generate_KnownElementRule2_EmitsIntAndNoWarning()
    {
        var (key, _) = GtnScenarioSeedGeneratorService.ElementRule2Map.First();
        var result = Run(elementRule2: key);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Generate_UnknownElementRule2_AddsWarning()
    {
        var result = Run(elementRule2: "__bad_rule__");
        Assert.Single(result.Warnings);
        Assert.Equal("element_rule_2", result.Warnings[0].Column);
    }

    // ── ElementRule3Map ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Absence",        1)]
    [InlineData("Allow Override", 2)]
    [InlineData("Gross Up",       3)]
    public void ElementRule3Map_ContainsExpectedEntries(string key, int expectedId)
    {
        Assert.True(GtnScenarioSeedGeneratorService.ElementRule3Map.TryGetValue(key, out var id));
        Assert.Equal(expectedId, id);
    }

    [Fact]
    public void Generate_KnownElementRule3_EmitsIntAndNoWarning()
    {
        var (key, _) = GtnScenarioSeedGeneratorService.ElementRule3Map.First();
        var result = Run(elementRule3: key);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Generate_UnknownElementRule3_AddsWarning()
    {
        var result = Run(elementRule3: "__bad_rule__");
        Assert.Single(result.Warnings);
        Assert.Equal("element_rule_3", result.Warnings[0].Column);
    }

    // ── Maps are independent (cross-column values produce warnings) ────────────

    [Fact]
    public void Generate_Rule1ValueInRule2Column_AddsWarning()
    {
        // "Salary" is a Rule1 value — not in Rule2Map
        var result = Run(elementRule2: "Salary");
        Assert.Single(result.Warnings);
        Assert.Equal("element_rule_2", result.Warnings[0].Column);
    }

    [Fact]
    public void Generate_Rule3ValueInRule1Column_AddsWarning()
    {
        // "Absence" is a Rule3 value — not in Rule1Map
        var result = Run(elementRule1: "Absence");
        Assert.Single(result.Warnings);
        Assert.Equal("element_rule_1", result.Warnings[0].Column);
    }

    // ── system_validated ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("Yes",     1)]
    [InlineData("Partial", 2)]
    [InlineData("No",      0)]
    public void SystemValidatedMap_ContainsExpectedEntries(string key, int expectedId)
    {
        Assert.True(GtnScenarioSeedGeneratorService.SystemValidatedMap.TryGetValue(key, out var id));
        Assert.Equal(expectedId, id);
    }

    [Fact]
    public void Generate_UnknownSystemValidated_AddsWarning()
    {
        var result = Run(systemValidated: "__bad__");
        Assert.Single(result.Warnings);
        Assert.Equal("system_validated", result.Warnings[0].Column);
    }

    // ── manual_validation_required ─────────────────────────────────────────────

    [Theory]
    [InlineData("Yes",  "TRUE")]
    [InlineData("yes",  "TRUE")]
    [InlineData("YES",  "TRUE")]
    [InlineData("No",   "FALSE")]
    [InlineData("no",   "FALSE")]
    [InlineData("",     "FALSE")]
    public void Generate_ManualValidation_MapsCorrectly(string input, string expectedSql)
    {
        var sql = GenerateSingle(manualValidation: string.IsNullOrEmpty(input) ? null : input);
        Assert.Contains(expectedSql, sql);
    }

    [Fact]
    public void Generate_NullManualValidation_EmitsFalse()
    {
        var sql = GenerateSingle(manualValidation: null);
        Assert.Contains("FALSE", sql);
    }

    // ── ParseManualValidation unit tests ──────────────────────────────────────

    [Theory]
    [InlineData("Yes",  true)]
    [InlineData("yes",  true)]
    [InlineData("YES",  true)]
    [InlineData("No",   false)]
    [InlineData("no",   false)]
    [InlineData("",     false)]
    [InlineData(null,   false)]
    public void ParseManualValidation_VariousInputs(string? input, bool expected)
    {
        Assert.Equal(expected, GtnScenarioSeedGeneratorService.ParseManualValidation(input));
    }

    // ── Multiline rule escaping ────────────────────────────────────────────────

    [Fact]
    public void Generate_MultilineRule_UsesEscapeStringSyntax()
    {
        var sql = GenerateSingle(rule: "Line 1\nLine 2");
        Assert.Contains("E'Line 1\\nLine 2'", sql);
    }

    [Fact]
    public void Generate_MultilineRuleCRLF_NormalisedToSlashN()
    {
        var sql = GenerateSingle(rule: "Line 1\r\nLine 2");
        Assert.Contains("E'Line 1\\nLine 2'", sql);
    }

    [Fact]
    public void Generate_SingleLineRule_UsesPlainQuotes()
    {
        var sql = GenerateSingle(rule: "Simple rule");
        Assert.Contains("'Simple rule'", sql);
        Assert.DoesNotContain("E'Simple rule", sql);
    }

    [Fact]
    public void Generate_RuleWithBackslash_UsesEscapeStringSyntax()
    {
        var sql = GenerateSingle(rule: @"Rule with \backslash");
        Assert.Contains("E'", sql);
        Assert.Contains("\\\\", sql);
    }

    // ── SqlEscape unit tests ──────────────────────────────────────────────────

    [Fact]
    public void SqlEscape_Null_ReturnsNull()
    {
        Assert.Equal("NULL", GtnScenarioSeedGeneratorService.SqlEscape(null));
    }

    [Fact]
    public void SqlEscape_PlainText_ReturnsQuoted()
    {
        Assert.Equal("'hello'", GtnScenarioSeedGeneratorService.SqlEscape("hello"));
    }

    [Fact]
    public void SqlEscape_SingleQuote_EscapesDoubled()
    {
        Assert.Equal("'it''s'", GtnScenarioSeedGeneratorService.SqlEscape("it's"));
    }

    [Fact]
    public void SqlEscape_Newline_UsesEString()
    {
        Assert.Equal("E'a\\nb'", GtnScenarioSeedGeneratorService.SqlEscape("a\nb"));
    }

    // ── SqlJsonb unit tests ───────────────────────────────────────────────────

    [Fact]
    public void SqlJsonb_EmptyList_ReturnsEmptyJsonb()
    {
        Assert.Equal("'[]'::jsonb", GtnScenarioSeedGeneratorService.SqlJsonb([]));
    }

    [Fact]
    public void SqlJsonb_SingleElement_ReturnsSingleElementJsonb()
    {
        Assert.Equal("'[1]'::jsonb", GtnScenarioSeedGeneratorService.SqlJsonb([1]));
    }

    [Fact]
    public void SqlJsonb_MultipleElements_ReturnsMultiElementJsonb()
    {
        Assert.Equal("'[1,2,3]'::jsonb", GtnScenarioSeedGeneratorService.SqlJsonb([1, 2, 3]));
    }

    // ── Transaction wrapping ──────────────────────────────────────────────────

    [Fact]
    public void Generate_OutputStartsWithBegin()
    {
        var sql = GenerateSingle();
        Assert.StartsWith("BEGIN;", sql.TrimStart());
    }

    [Fact]
    public void Generate_OutputEndsWithCommit()
    {
        var sql = GenerateSingle();
        Assert.EndsWith("COMMIT;", sql.TrimEnd());
    }

    // ── No destructive SQL ────────────────────────────────────────────────────

    [Fact]
    public void Generate_NeverContainsDestructiveSql()
    {
        var sql = GenerateSingle(
            group:             GtnScenarioSeedGeneratorService.ValidationGroupMap.Keys.First(),
            systemElementType: GtnScenarioSeedGeneratorService.SystemPayElementMap.Keys.First(),
            eeStatus:          GtnScenarioSeedGeneratorService.AssignmentStatusMap.Keys.First());

        Assert.DoesNotContain("DROP",     sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TRUNCATE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE",   sql, StringComparison.OrdinalIgnoreCase);
    }

    // ── Both INSERT statements generated ─────────────────────────────────────

    [Fact]
    public void Generate_ProducesInsertIntoNomenclatureGtnScenarios()
    {
        var sql = GenerateSingle();
        Assert.Contains("INSERT INTO nomenclature.gtn_scenarios", sql);
    }

    [Fact]
    public void Generate_ProducesInsertIntoBusinessGtnScenariosSettings()
    {
        var sql = GenerateSingle();
        Assert.Contains("INSERT INTO business.gtn_scenarios_settings", sql);
    }

    [Fact]
    public void Generate_SettingsRow_UpdatedOnIsNull()
    {
        var sql = GenerateSingle();
        Assert.DoesNotContain("CURRENT_TIMESTAMP", sql, StringComparison.OrdinalIgnoreCase);
        // The settings VALUES row must end with …, NULL, NULL)
        Assert.Contains(", NULL, NULL)", sql);
    }

    [Fact]
    public void Generate_SettingsRow_UpdatedByIsNull()
    {
        var sql = GenerateSingle();
        Assert.DoesNotContain("'system'", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_SettingsRow_DoesNotContainCurrentTimestamp()
    {
        Assert.DoesNotContain("CURRENT_TIMESTAMP", GenerateSingle(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_SettingsRow_DoesNotContainSystemLiteral()
    {
        Assert.DoesNotContain("'system'", GenerateSingle(), StringComparison.OrdinalIgnoreCase);
    }

    // ── Skips rows without scenario ID ────────────────────────────────────────

    [Fact]
    public void Generate_RowWithNoScenarioId_IsSkipped()
    {
        var preview = BuildPreview([
            new() { ["validation_scenario_id"] = null },
        ]);
        var result = _sut.Generate(preview);
        Assert.DoesNotContain("INSERT INTO", result.ScenariosSql);
        Assert.Empty(result.Warnings);
    }

    // ── Comma-normalised scenario IDs ─────────────────────────────────────────

    [Fact]
    public void Generate_CommaSeparatedScenarioId_NormalisedInSql()
    {
        var preview = BuildPreview([
            new() { ["validation_scenario_id"] = "1,01" },
        ]);
        var result = _sut.Generate(preview);
        Assert.Contains("'1.01'", result.ScenariosSql);
    }

    [Fact]
    public void Generate_CommaSeparatedScenarioId_GeneratesCorrectIntId()
    {
        var preview = BuildPreview([
            new() { ["validation_scenario_id"] = "1,01" },
        ]);
        var result = _sut.Generate(preview);
        Assert.Contains("(101,", result.ScenariosSql);
    }

    // ── Missing mapping produces no crash ────────────────────────────────────

    [Fact]
    public void Generate_AllUnknownMappings_CompletesWithoutThrowing()
    {
        var result = Run(
            group:            "??",
            systemElementType:"??",
            eeStatus:         "??",
            elementSubType:   "??",
            elementRule1:     "??",
            elementRule2:     "??",
            elementRule3:     "??",
            systemValidated:  "??");

        Assert.NotEmpty(result.Warnings);
        Assert.NotNull(result.ScenariosSql);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string GenerateSingle(
        string? group             = null,
        string? systemElementType = null,
        string? eeStatus          = null,
        string? manualValidation  = null,
        string? rule              = null,
        string? elementSubType    = null,
        string? elementRule1      = null,
        string? elementRule2      = null,
        string? elementRule3      = null,
        string? systemValidated   = null,
        string  scenarioId        = "1.01")
    {
        return Run(group, systemElementType, eeStatus, manualValidation, rule,
            elementSubType, elementRule1, elementRule2, elementRule3, systemValidated, scenarioId).ScenariosSql;
    }

    private GtnSeedGenerationResult Run(
        string? group             = null,
        string? systemElementType = null,
        string? eeStatus          = null,
        string? manualValidation  = null,
        string? rule              = null,
        string? elementSubType    = null,
        string? elementRule1      = null,
        string? elementRule2      = null,
        string? elementRule3      = null,
        string? systemValidated   = null,
        string  scenarioId        = "1.01")
    {
        var row = new Dictionary<string, string?>
        {
            ["validation_scenario_id"]                        = scenarioId,
            ["group"]                                         = group,
            ["validation_scenario_label"]                     = "Test Label",
            ["validation_scenario_logic"]                     = "Test Logic",
            ["validation_scenario_rule_platform_data_points"] = rule,
            ["system_element_type"]                           = systemElementType,
            ["element_sub_type"]                              = elementSubType,
            ["element_rule_1"]                                = elementRule1,
            ["element_rule_2"]                                = elementRule2,
            ["element_rule_3"]                                = elementRule3,
            ["ee_status"]                                     = eeStatus,
            ["manually_validation_by_ov_required"]            = manualValidation,
            ["system_validated"]                              = systemValidated,
        };

        return _sut.Generate(BuildPreview([row]));
    }

    private static SheetPreview BuildPreview(IReadOnlyList<Dictionary<string, string?>> rows) =>
        new()
        {
            SheetName       = "Global PayGroup GTN Validation",
            FilePath        = "/test.xlsx",
            HeaderRowNumber = 2,
            TotalRowCount   = rows.Count,
            Columns         = [],
            Rows            = rows.Select(r => (IReadOnlyDictionary<string, string?>)r).ToList(),
        };

    // Returns the first JSONB literal that follows a given column marker in the SQL.
    // Used to compare canonical vs alias output without full SQL matching.
    private static string ExtractJsonb(string sql, string columnHint)
    {
        _ = columnHint; // hint used for readability only; we find the first jsonb literal
        var start = sql.IndexOf("'[", StringComparison.Ordinal);
        if (start < 0) return string.Empty;
        var end = sql.IndexOf("]'::jsonb", start, StringComparison.Ordinal);
        return end < 0 ? string.Empty : sql[start..(end + "]'::jsonb".Length)];
    }

    private static int CountOccurrences(string text, string pattern) =>
        (text.Length - text.Replace(pattern, string.Empty, StringComparison.Ordinal).Length) / pattern.Length;
}
