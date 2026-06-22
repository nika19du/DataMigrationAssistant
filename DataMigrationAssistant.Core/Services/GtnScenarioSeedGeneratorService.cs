using System.Text;
using DataMigrationAssistant.Core.Models;

namespace DataMigrationAssistant.Core.Services;

public sealed class GtnScenarioSeedGeneratorService : IGtnScenarioSeedGeneratorService
{
    // Deterministic lookup tables — map Excel cell values (trimmed, case-insensitive) to integer IDs.
    // IDs align with the natural group numbering visible in scenario IDs (group 1 → scenarios 1.xx, etc.).

    internal static readonly IReadOnlyDictionary<string, int> ValidationGroupMap =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["RECURRING SALARY"]   = 1,
            ["RECURRING STANDARD"] = 2,
            ["RECURRING %"]        = 3,
            ["RECURRING HOURLY"]   = 4,
            ["VARIABLE UNIT"]      = 5,
            ["VARIABLE HOURLY"]    = 6,
            ["VARIABLE VALUE"]     = 7,
            ["ABSENCE"]            = 8,
            ["RPE IS MISSING IPE"] = 9,
            ["CALCULATED"]         = 10,
            ["NO INPUT"]           = 11,
            ["Net Pay"]            = 12,
            ["Multi IPE Mapping"]  = 13,
            ["Advance Pay"]        = 14,
        };

    internal static readonly IReadOnlyDictionary<string, int> SystemPayElementMap =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Earning"]                              = 1,
            ["Earnings"]                             = 1,   // alias — same canonical ID
            ["Notional Pay"]                         = 2,
            ["Notional"]                             = 2,   // alias — same canonical ID
            ["Employee Deduction (by ER)"]           = 3,
            ["Employee Deduction (by Third Party)"]  = 4,
            ["Employee Pension"]                     = 5,
            ["Employer Net Pay Adjustment"]          = 6,
            ["Third Party Net Pay Adjustment"]       = 7,
            ["Employer Other"]                       = 8,
            ["Employer Pension"]                     = 9,
            ["Additional Payroll Taxes"]             = 10,
            ["Employee Social Security"]             = 11,
            ["Employee Tax"]                         = 12,
            ["Employer Social Security"]             = 13,
            ["Employer Tax"]                         = 14,
            ["Net Pay"]                              = 15,
        };

    internal static readonly IReadOnlyDictionary<string, int> AssignmentStatusMap =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Active"]           = 1,
            ["Inactive"]         = 2,
            ["Leave of Absence"] = 3,
            ["Terminated"]       = 4,
            ["Suspended"]        = 5,
            ["Joiner"]           = 6,
            ["Leaver"]           = 7,
        };

    internal static readonly IReadOnlyDictionary<string, int> ElementSubtypeMap =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Recurring"]  = 1,
            ["Variable"]   = 2,
            ["Calculated"] = 3,
        };

    internal static readonly IReadOnlyDictionary<string, int> ElementRule1Map =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Salary"]   = 1,
            ["Standard"] = 2,
            ["Percent"]  = 3,
            ["Hourly"]   = 4,
            ["Unit"]     = 5,
            ["Value"]    = 6,
        };

    internal static readonly IReadOnlyDictionary<string, int> ElementRule2Map =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Derived"]          = 1,
            ["Specified Rate"]   = 2,
            ["Employee Rate"]    = 3,
            ["Bonus/Commission"] = 4,
        };

    internal static readonly IReadOnlyDictionary<string, int> ElementRule3Map =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Absence"]        = 1,
            ["Allow Override"] = 2,
            ["Gross Up"]       = 3,
        };

    internal static readonly IReadOnlyDictionary<string, int> SystemValidatedMap =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Yes"]     = 1,
            ["Partial"] = 2,
            ["No"]      = 0,
        };

    public GtnSeedGenerationResult Generate(SheetPreview sheetPreview)
    {
        var warnings  = new List<GtnSeedWarning>();
        var scenarios = new List<NormalizedGtnScenario>();

        for (int i = 0; i < sheetPreview.Rows.Count; i++)
        {
            var row         = sheetPreview.Rows[i];
            var excelRowNum = sheetPreview.HeaderRowNumber + 1 + i;
            var rawId       = Cell(row, "validation_scenario_id");

            if (string.IsNullOrWhiteSpace(rawId))
                continue;

            var normalizedId = NormalizeScenarioId(rawId.Trim());

            if (!TryParseId(normalizedId, out var id))
            {
                warnings.Add(MakeWarning(excelRowNum, rawId, "validation_scenario_id", rawId,
                    $"Cannot generate integer ID from '{rawId}' (normalized: '{normalizedId}'). Row skipped."));
                continue;
            }

            var rowWarnings = new List<GtnSeedWarning>();

            var validationGroup   = MapSingle(row, "group",            normalizedId, excelRowNum, ValidationGroupMap, rowWarnings);
            var elementSubtype    = MapSingle(row, "element_sub_type", normalizedId, excelRowNum, ElementSubtypeMap,  rowWarnings);
            var elementRule1      = MapSingle(row, "element_rule_1",   normalizedId, excelRowNum, ElementRule1Map,    rowWarnings);
            var elementRule2      = MapSingle(row, "element_rule_2",   normalizedId, excelRowNum, ElementRule2Map,    rowWarnings);
            var elementRule3      = MapSingle(row, "element_rule_3",   normalizedId, excelRowNum, ElementRule3Map,    rowWarnings);
            var systemValidated   = MapSingle(row, "system_validated", normalizedId, excelRowNum, SystemValidatedMap, rowWarnings);
            var systemPayElements = MapMulti( row, "system_element_type", normalizedId, excelRowNum, SystemPayElementMap, rowWarnings);
            var assignmentStatus  = MapMulti( row, "ee_status",           normalizedId, excelRowNum, AssignmentStatusMap, rowWarnings);

            var manualRequired = ParseManualValidation(Cell(row, "manually_validation_by_ov_required"));

            warnings.AddRange(rowWarnings);

            scenarios.Add(new NormalizedGtnScenario
            {
                Id                       = id,
                ValidationScenarioId     = normalizedId,
                ValidationGroup          = validationGroup,
                ValidationScenarioLabel  = Cell(row, "validation_scenario_label"),
                ValidationScenarioLogic  = Cell(row, "validation_scenario_logic"),
                ValidationScenarioRule   = Cell(row, "validation_scenario_rule_platform_data_points"),
                ElementSubtype           = elementSubtype,
                ElementRule1             = elementRule1,
                ElementRule2             = elementRule2,
                ElementRule3             = elementRule3,
                SystemPayElements        = systemPayElements,
                AssignmentStatus         = assignmentStatus,
                SystemValidated          = systemValidated,
                ManualValidationRequired = manualRequired,
            });
        }

        return new GtnSeedGenerationResult
        {
            ScenariosSql  = BuildSql(scenarios),
            ScenarioCount = scenarios.Count,
            Warnings      = warnings,
        };
    }

    // Normalizes "1,01" → "1.01" (Excel sometimes uses comma as decimal separator).
    internal static string NormalizeScenarioId(string rawId) => rawId.Replace(',', '.');

    // "1.01" → 101, "14.02" → 1402. Removes the dot and parses as integer.
    internal static bool TryParseId(string normalizedId, out int id)
    {
        var digits = normalizedId.Replace(".", string.Empty, StringComparison.Ordinal);
        return int.TryParse(digits, out id);
    }

    // ── Mapping helpers ────────────────────────────────────────────────────────

    private static int? MapSingle(
        IReadOnlyDictionary<string, string?> row,
        string column,
        string scenarioId,
        int rowNum,
        IReadOnlyDictionary<string, int> map,
        List<GtnSeedWarning> warnings)
    {
        var value = Cell(row, column);
        if (string.IsNullOrWhiteSpace(value)) return null;

        if (map.TryGetValue(value, out var mapped)) return mapped;

        warnings.Add(MakeWarning(rowNum, scenarioId, column, value,
            $"No mapping found for '{value}' in column '{column}'. NULL will be used."));
        return null;
    }

    private static IReadOnlyList<int> MapMulti(
        IReadOnlyDictionary<string, string?> row,
        string column,
        string scenarioId,
        int rowNum,
        IReadOnlyDictionary<string, int> map,
        List<GtnSeedWarning> warnings)
    {
        var value = Cell(row, column);
        if (string.IsNullOrWhiteSpace(value)) return [];

        var result = new List<int>();
        foreach (var token in value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (map.TryGetValue(token, out var mapped))
                result.Add(mapped);
            else
                warnings.Add(MakeWarning(rowNum, scenarioId, column, token,
                    $"No mapping found for '{token}' in column '{column}'. Entry skipped."));
        }
        return result;
    }

    internal static bool ParseManualValidation(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Trim().Equals("Yes", StringComparison.OrdinalIgnoreCase);

    private static string? Cell(IReadOnlyDictionary<string, string?> row, string key)
    {
        var raw = row.TryGetValue(key, out var v) ? v : null;
        return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
    }

    private static GtnSeedWarning MakeWarning(int rowNum, string? scenarioId, string column, string? value, string message) =>
        new() { RowNumber = rowNum, ScenarioId = scenarioId, Column = column, Value = value, Message = message };

    // ── SQL generation ─────────────────────────────────────────────────────────

    private static string BuildSql(IReadOnlyList<NormalizedGtnScenario> scenarios)
    {
        if (scenarios.Count == 0)
            return "-- No GTN scenarios found to generate.";

        var sb = new StringBuilder();
        sb.AppendLine("BEGIN;");

        foreach (var s in scenarios)
        {
            sb.AppendLine();
            sb.AppendLine("INSERT INTO nomenclature.gtn_scenarios");
            sb.AppendLine("(id, validation_scenario_id, validation_group, validation_scenario_label,");
            sb.AppendLine(" validation_scenario_logic, validation_scenario_rule,");
            sb.AppendLine(" element_subtype, element_rule1, element_rule2, element_rule3,");
            sb.AppendLine(" system_pay_elements, assignment_status, system_validated)");
            sb.AppendLine("VALUES");
            sb.AppendLine(
                $"({s.Id}, {SqlText(s.ValidationScenarioId)}, {SqlInt(s.ValidationGroup)}, {SqlEscape(s.ValidationScenarioLabel)},");
            sb.AppendLine(
                $" {SqlEscape(s.ValidationScenarioLogic)}, {SqlEscape(s.ValidationScenarioRule)},");
            sb.AppendLine(
                $" {SqlInt(s.ElementSubtype)}, {SqlInt(s.ElementRule1)}, {SqlInt(s.ElementRule2)}, {SqlInt(s.ElementRule3)},");
            sb.AppendLine(
                $" {SqlJsonb(s.SystemPayElements)}, {SqlJsonb(s.AssignmentStatus)}, {SqlInt(s.SystemValidated)})");
            sb.AppendLine("ON CONFLICT DO NOTHING;");

            sb.AppendLine();
            sb.AppendLine("INSERT INTO business.gtn_scenarios_settings");
            sb.AppendLine("(gtn_scenario_id, pay_group_id, manual_validation_required, updated_on, updated_by)");
            sb.AppendLine("VALUES");
            sb.AppendLine($"({s.Id}, NULL, {(s.ManualValidationRequired ? "TRUE" : "FALSE")}, NULL, NULL)");
            sb.AppendLine("ON CONFLICT DO NOTHING;");
        }

        sb.AppendLine();
        sb.Append("COMMIT;");
        return sb.ToString();
    }

    private static string SqlText(string value) =>
        $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";

    private static string SqlInt(int? value) =>
        value.HasValue ? value.Value.ToString() : "NULL";

    internal static string SqlEscape(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "NULL";

        var v = value.Trim();
        var needsEscape = v.IndexOf('\n') >= 0 || v.IndexOf('\r') >= 0 || v.IndexOf('\\') >= 0;

        if (!needsEscape)
            return $"'{v.Replace("'", "''", StringComparison.Ordinal)}'";

        var escaped = v
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("'",  "''",   StringComparison.Ordinal)
            .Replace("\r\n", "\\n", StringComparison.Ordinal)
            .Replace("\n",  "\\n",  StringComparison.Ordinal)
            .Replace("\r",  "\\n",  StringComparison.Ordinal)
            .Replace("\t",  "\\t",  StringComparison.Ordinal);

        return $"E'{escaped}'";
    }

    internal static string SqlJsonb(IReadOnlyList<int> values) =>
        values.Count == 0
            ? "'[]'::jsonb"
            : $"'[{string.Join(",", values)}]'::jsonb";
}
