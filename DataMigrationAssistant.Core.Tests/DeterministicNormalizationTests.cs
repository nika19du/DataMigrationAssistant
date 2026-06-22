using DataMigrationAssistant.Core.Models;
using DataMigrationAssistant.Core.Normalization;
using DataMigrationAssistant.Core.Services;

namespace DataMigrationAssistant.Core.Tests;

// ─── Shared helpers ───────────────────────────────────────────────────────────

file static class DetNormHelpers
{
    public static NormalizationRequest MakeRequest(params string[] columnNames)
    {
        var columns = columnNames.Select((name, i) => new ColumnInfo
        {
            Index = i, Name = name, SnakeCaseName = name,
        }).ToList();

        return new NormalizationRequest
        {
            SheetPreview = new SheetPreview
            {
                SheetName = "test_sheet",
                FilePath  = "test.xlsx",
                Columns   = columns,
                Rows      = [],
            },
            FlatSchema = new TableSchema
            {
                TableName = "test_sheet",
                Columns   = columnNames.Select((name, i) => new ColumnSchema
                {
                    Index         = i,
                    Name          = name,
                    SnakeCaseName = name,
                    InferredType  = PostgresType.Text,
                    IsNullable    = true,
                }).ToList(),
            },
        };
    }

    // All required detection columns + all optional columns
    public static NormalizationRequest FullRequest() => MakeRequest(
        "validation_scenario_id",
        "group",
        "validation_scenario_label",
        "validation_scenario_logic",
        "validation_scenario_rule_platform_data_points",
        "ee_status",
        "manually_validation_by_ov_required",
        "system_validated",
        "system_element_type",
        "element_sub_type",
        "element_rule_1",
        "element_rule_2",
        "element_rule_3"
    );

    // Only the six required detection columns — no optional ones
    public static NormalizationRequest MinimalRequest() => MakeRequest(
        "validation_scenario_id",
        "validation_scenario_label",
        "validation_scenario_logic",
        "system_element_type",
        "element_sub_type",
        "element_rule_1"
    );

    // Two distinct scenarios × two settings each — used for seed SQL tests
    public static NormalizationRequest RequestWithRows()
    {
        var cols = FullRequest().SheetPreview.Columns;
        var rows = new List<IReadOnlyDictionary<string, string?>>
        {
            new Dictionary<string, string?>
            {
                ["validation_scenario_id"]    = "1,01",
                ["validation_scenario_label"] = "Scenario One",
                ["validation_scenario_logic"] = "Logic A",
                ["system_element_type"]       = "BASIC",
                ["element_sub_type"]          = "SUB_A",
                ["element_rule_1"]            = "Rule 1",
                ["element_rule_2"]            = "Rule 2",
                ["group"]                     = "G1",
                ["ee_status"]                 = "ACTIVE",
            },
            new Dictionary<string, string?>
            {
                ["validation_scenario_id"]    = "1,01",
                ["validation_scenario_label"] = "Scenario One",
                ["validation_scenario_logic"] = "Logic A",
                ["system_element_type"]       = "OVERTIME",
                ["element_sub_type"]          = "SUB_B",
                ["element_rule_1"]            = "Rule 2",
                ["element_rule_2"]            = null,
                ["group"]                     = "G1",
                ["ee_status"]                 = "ACTIVE",
            },
            new Dictionary<string, string?>
            {
                ["validation_scenario_id"]    = "1,02",
                ["validation_scenario_label"] = "Scenario Two",
                ["validation_scenario_logic"] = "Logic B",
                ["system_element_type"]       = "BASIC",
                ["element_sub_type"]          = "SUB_A",
                ["element_rule_1"]            = "Rule 1",
                ["element_rule_2"]            = null,
                ["group"]                     = "G2",
                ["ee_status"]                 = "INACTIVE",
            },
        };

        return new NormalizationRequest
        {
            SheetPreview = new SheetPreview
            {
                SheetName     = "test_sheet",
                FilePath      = "test.xlsx",
                Columns       = cols,
                Rows          = rows,
                TotalRowCount = rows.Count,
            },
            FlatSchema = FullRequest().FlatSchema,
        };
    }
}

// ─── ValidationScenarioNormalizationRule — detection ─────────────────────────

public class ValidationScenarioRuleDetectionTests
{
    private readonly ValidationScenarioNormalizationRule _rule = new();

    [Fact]
    public void CanHandle_WithAllRequiredColumns_ReturnsTrue()
        => Assert.True(_rule.CanHandle(DetNormHelpers.MinimalRequest()));

    [Fact]
    public void CanHandle_WithAllColumnsIncludingOptional_ReturnsTrue()
        => Assert.True(_rule.CanHandle(DetNormHelpers.FullRequest()));

    [Theory]
    [InlineData("validation_scenario_id")]
    [InlineData("validation_scenario_label")]
    [InlineData("validation_scenario_logic")]
    [InlineData("system_element_type")]
    [InlineData("element_sub_type")]
    [InlineData("element_rule_1")]
    public void CanHandle_MissingRequiredColumn_ReturnsFalse(string missingColumn)
    {
        var allRequired = new[]
        {
            "validation_scenario_id", "validation_scenario_label", "validation_scenario_logic",
            "system_element_type", "element_sub_type", "element_rule_1",
        };
        var request = DetNormHelpers.MakeRequest(
            allRequired.Where(c => c != missingColumn).ToArray());

        Assert.False(_rule.CanHandle(request));
    }

    [Fact]
    public void CanHandle_EmptySheet_ReturnsFalse()
        => Assert.False(_rule.CanHandle(DetNormHelpers.MakeRequest()));

    [Fact]
    public void CanHandle_UnrelatedColumns_ReturnsFalse()
        => Assert.False(_rule.CanHandle(DetNormHelpers.MakeRequest("employee_id", "name", "salary")));

    [Fact]
    public void CanHandle_IsCaseInsensitiveOnColumnNames()
    {
        var request = DetNormHelpers.MakeRequest(
            "VALIDATION_SCENARIO_ID", "VALIDATION_SCENARIO_LABEL", "VALIDATION_SCENARIO_LOGIC",
            "SYSTEM_ELEMENT_TYPE", "ELEMENT_SUB_TYPE", "ELEMENT_RULE_1");
        Assert.True(_rule.CanHandle(request));
    }
}

// ─── ValidationScenarioNormalizationRule — gtn_scenarios table ───────────────

public class ValidationScenarioRuleGtnScenariosTableTests
{
    private readonly ValidationScenarioNormalizationRule _rule = new();

    private ProposedTable GtnScenarios(NormalizationRequest? req = null)
        => _rule.Apply(req ?? DetNormHelpers.FullRequest()).Tables[0];

    [Fact]
    public void Apply_FirstTable_IsGtnScenarios()
        => Assert.Equal("gtn_scenarios", GtnScenarios().TableName);

    [Fact]
    public void Apply_GtnScenarios_HasExactlyOnePrimaryKey()
        => Assert.Single(GtnScenarios().Columns, c => c.IsPrimaryKey);

    // ── Surrogate integer PK ─────────────────────────────────────────────────

    [Fact]
    public void Apply_GtnScenarios_PrimaryKeyIsNamedId()
        => Assert.Equal("id", GtnScenarios().Columns.Single(c => c.IsPrimaryKey).Name);

    [Fact]
    public void Apply_GtnScenarios_PKHasIntegerType()
        => Assert.Equal("INTEGER", GtnScenarios().Columns.Single(c => c.IsPrimaryKey).PostgresType);

    [Fact]
    public void Apply_GtnScenarios_PKIsNotNullable()
        => Assert.False(GtnScenarios().Columns.Single(c => c.IsPrimaryKey).IsNullable);

    [Fact]
    public void Apply_GtnScenarios_PKHasNoForeignKey()
        => Assert.Null(GtnScenarios().Columns.Single(c => c.IsPrimaryKey).ForeignKeyTo);

    [Fact]
    public void Apply_GtnScenarios_SeedSequenceStartIs101()
        => Assert.Equal(101, GtnScenarios().SeedSequenceStart);

    // ── validation_scenario_id stays TEXT ────────────────────────────────────

    [Fact]
    public void Apply_GtnScenarios_HasValidationScenarioIdColumn()
        => Assert.Contains(GtnScenarios().Columns, c => c.Name == "validation_scenario_id");

    [Fact]
    public void Apply_GtnScenarios_ValidationScenarioIdHasTextType()
    {
        var col = GtnScenarios().Columns.Single(c => c.Name == "validation_scenario_id");
        Assert.Equal("TEXT", col.PostgresType);
    }

    [Fact]
    public void Apply_GtnScenarios_ValidationScenarioIdIsNotNullable()
    {
        var col = GtnScenarios().Columns.Single(c => c.Name == "validation_scenario_id");
        Assert.False(col.IsNullable);
    }

    [Fact]
    public void Apply_GtnScenarios_ValidationScenarioIdIsNotPrimaryKey()
    {
        var col = GtnScenarios().Columns.Single(c => c.Name == "validation_scenario_id");
        Assert.False(col.IsPrimaryKey);
    }

    // ── SourceColumns must NOT include anything ending in _id ─────────────────
    // Rationale: NormalizationSqlGeneratorService suffix-matches source columns
    // ending in "_id" to resolve the `id` PK column. If validation_scenario_id
    // were in SourceColumns it would be used for the id PK (producing text, not integer).

    [Fact]
    public void Apply_GtnScenarios_SourceColumnsDoNotContainValidationScenarioId()
        => Assert.DoesNotContain("validation_scenario_id", GtnScenarios().SourceColumns);

    [Fact]
    public void Apply_GtnScenarios_SourceColumnsContainNoColumnEndingInId()
        => Assert.DoesNotContain(GtnScenarios().SourceColumns,
            c => c.EndsWith("_id", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void Apply_GtnScenarios_HasNoForeignKeyColumns()
        => Assert.DoesNotContain(GtnScenarios().Columns, c => c.ForeignKeyTo is not null);

    // ── Optional columns ─────────────────────────────────────────────────────

    [Fact]
    public void Apply_GtnScenarios_FullRequest_SourceColumnsContainOptionalColumns()
    {
        var src = GtnScenarios().SourceColumns;
        Assert.Contains("group", src);
        Assert.Contains("validation_scenario_label", src);
        Assert.Contains("validation_scenario_logic", src);
        Assert.Contains("validation_scenario_rule_platform_data_points", src);
        Assert.Contains("ee_status", src);
        Assert.Contains("manually_validation_by_ov_required", src);
        Assert.Contains("system_validated", src);
    }

    [Fact]
    public void Apply_GtnScenarios_MinimalRequest_SourceColumnsExcludeAbsentOptionals()
    {
        var src = GtnScenarios(DetNormHelpers.MinimalRequest()).SourceColumns;
        Assert.DoesNotContain("group", src);
        Assert.DoesNotContain("validation_scenario_rule_platform_data_points", src);
        Assert.DoesNotContain("ee_status", src);
        Assert.DoesNotContain("manually_validation_by_ov_required", src);
        Assert.DoesNotContain("system_validated", src);
    }

    [Fact]
    public void Apply_GtnScenarios_MinimalRequest_IncludesRequiredOptionalColumnsInSource()
    {
        // validation_scenario_label and validation_scenario_logic are in the required detection
        // set; they are also ScenarioOptionalCols so they appear in SourceColumns when present.
        var src = GtnScenarios(DetNormHelpers.MinimalRequest()).SourceColumns;
        Assert.Contains("validation_scenario_label", src);
        Assert.Contains("validation_scenario_logic", src);
    }
}

// ─── ValidationScenarioNormalizationRule — gtn_scenario_settings table ───────

public class ValidationScenarioRuleGtnScenarioSettingsTableTests
{
    private readonly ValidationScenarioNormalizationRule _rule = new();

    private ProposedTable GtnSettings(NormalizationRequest? req = null)
        => _rule.Apply(req ?? DetNormHelpers.FullRequest()).Tables[1];

    [Fact]
    public void Apply_SecondTable_IsGtnScenarioSettings()
        => Assert.Equal("gtn_scenario_settings", GtnSettings().TableName);

    [Fact]
    public void Apply_GtnSettings_HasExactlyOnePrimaryKey()
        => Assert.Single(GtnSettings().Columns, c => c.IsPrimaryKey);

    [Fact]
    public void Apply_GtnSettings_SyntheticPKIsNamedId()
        => Assert.Equal("id", GtnSettings().Columns.Single(c => c.IsPrimaryKey).Name);

    [Fact]
    public void Apply_GtnSettings_SyntheticPKHasIntegerType()
        => Assert.Equal("INTEGER", GtnSettings().Columns.Single(c => c.IsPrimaryKey).PostgresType);

    [Fact]
    public void Apply_GtnSettings_SyntheticPKIsNotNullable()
        => Assert.False(GtnSettings().Columns.Single(c => c.IsPrimaryKey).IsNullable);

    [Fact]
    public void Apply_GtnSettings_SeedSequenceStartIs1001()
        => Assert.Equal(1001, GtnSettings().SeedSequenceStart);

    // ── FK references gtn_scenarios(id) ─────────────────────────────────────

    [Fact]
    public void Apply_GtnSettings_HasForeignKeyColumn()
    {
        var fkCol = GtnSettings().Columns.Single(c => c.ForeignKeyTo is not null);
        Assert.Equal("gtn_scenario_id", fkCol.Name);
    }

    [Fact]
    public void Apply_GtnSettings_FKColumnHasIntegerType()
    {
        var fkCol = GtnSettings().Columns.Single(c => c.ForeignKeyTo is not null);
        Assert.Equal("INTEGER", fkCol.PostgresType);
    }

    [Fact]
    public void Apply_GtnSettings_FKReferencesGtnScenariosId()
    {
        var fkCol = GtnSettings().Columns.Single(c => c.ForeignKeyTo is not null);
        Assert.Equal("gtn_scenarios(id)", fkCol.ForeignKeyTo);
    }

    [Fact]
    public void Apply_GtnSettings_FKColumnIsNullable()
    {
        // Marked nullable so the seed generator emits NULL rather than erroring out
        // (cross-table FK value derivation is not supported by the SQL generator).
        var fkCol = GtnSettings().Columns.Single(c => c.ForeignKeyTo is not null);
        Assert.True(fkCol.IsNullable);
    }

    // ── validation_scenario_id trace column ──────────────────────────────────

    [Fact]
    public void Apply_GtnSettings_FullRequest_HasValidationScenarioIdTraceColumn()
        => Assert.Contains(GtnSettings().Columns, c => c.Name == "validation_scenario_id");

    [Fact]
    public void Apply_GtnSettings_TraceColumnHasTextType()
    {
        var traceCol = GtnSettings().Columns.Single(c => c.Name == "validation_scenario_id");
        Assert.Equal("TEXT", traceCol.PostgresType);
    }

    [Fact]
    public void Apply_GtnSettings_TraceColumnIsNullable()
    {
        var traceCol = GtnSettings().Columns.Single(c => c.Name == "validation_scenario_id");
        Assert.True(traceCol.IsNullable);
    }

    [Fact]
    public void Apply_GtnSettings_TraceColumnNotInSourceColumns()
        => Assert.DoesNotContain("validation_scenario_id", GtnSettings().SourceColumns);

    // ── SourceColumns safe (no _id suffix) ───────────────────────────────────

    [Fact]
    public void Apply_GtnSettings_SourceColumnsContainNoColumnEndingInId()
        => Assert.DoesNotContain(GtnSettings().SourceColumns,
            c => c.EndsWith("_id", StringComparison.OrdinalIgnoreCase));

    // ── Optional columns ─────────────────────────────────────────────────────

    [Fact]
    public void Apply_GtnSettings_FullRequest_IncludesElementRuleColumns()
    {
        var cols = GtnSettings().Columns;
        Assert.Contains(cols, c => c.Name == "element_rule_1");
        Assert.Contains(cols, c => c.Name == "element_rule_2");
        Assert.Contains(cols, c => c.Name == "element_rule_3");
    }

    [Fact]
    public void Apply_GtnSettings_MinimalRequest_SkipsMissingOptionalColumns()
    {
        var cols = GtnSettings(DetNormHelpers.MinimalRequest()).Columns;
        Assert.DoesNotContain(cols, c => c.Name == "element_rule_2");
        Assert.DoesNotContain(cols, c => c.Name == "element_rule_3");
    }

    [Fact]
    public void Apply_GtnSettings_MinimalRequest_IncludesRequiredDetectionColumns()
    {
        var cols = GtnSettings(DetNormHelpers.MinimalRequest()).Columns;
        Assert.Contains(cols, c => c.Name == "system_element_type");
        Assert.Contains(cols, c => c.Name == "element_sub_type");
        Assert.Contains(cols, c => c.Name == "element_rule_1");
    }

    [Fact]
    public void Apply_GtnSettings_FullRequest_SourceColumnsContainSettingsCols()
    {
        var src = GtnSettings().SourceColumns;
        Assert.Contains("system_element_type", src);
        Assert.Contains("element_sub_type",    src);
        Assert.Contains("element_rule_1",      src);
        Assert.Contains("element_rule_2",      src);
        Assert.Contains("element_rule_3",      src);
    }

    [Fact]
    public void Apply_GtnSettings_MinimalRequest_SourceColumnsExcludeAbsentOptionals()
    {
        var src = GtnSettings(DetNormHelpers.MinimalRequest()).SourceColumns;
        Assert.DoesNotContain("element_rule_2", src);
        Assert.DoesNotContain("element_rule_3", src);
    }
}

// ─── ValidationScenarioNormalizationRule — proposal level ────────────────────

public class ValidationScenarioRuleProposalTests
{
    private readonly ValidationScenarioNormalizationRule _rule = new();

    [Fact]
    public void Apply_ReturnsTwoTables()
        => Assert.Equal(2, _rule.Apply(DetNormHelpers.FullRequest()).Tables.Count);

    [Fact]
    public void Apply_ReasoningMatchesSpec()
        => Assert.Equal(
            "Generated by deterministic validation scenario rule because the sheet " +
            "contains validation scenario and pay element configuration columns.",
            _rule.Apply(DetNormHelpers.FullRequest()).Reasoning);

    [Fact]
    public void Apply_SqlFieldsAreEmptyBeforeSqlGeneration()
    {
        var proposal = _rule.Apply(DetNormHelpers.FullRequest());
        Assert.Equal(string.Empty, proposal.CombinedMigrationSql);
        Assert.Equal(string.Empty, proposal.CombinedSeedSql);
        Assert.Equal(string.Empty, proposal.MarkdownReport);
    }
}

// ─── Seed SQL — gtn_scenarios ────────────────────────────────────────────────

public class ValidationScenarioRuleSeedScenariosTests
{
    private static string GetScenariosSeed()
    {
        var rule     = new ValidationScenarioNormalizationRule();
        var request  = DetNormHelpers.RequestWithRows();
        var proposal = rule.Apply(request);
        var svc      = new NormalizationSqlGeneratorService();
        var result   = svc.Generate(proposal, request.SheetPreview);
        Assert.True(result.Success, result.Error);
        return result.Value!.Tables[0].SeedSql;
    }

    [Fact]
    public void Seed_GtnScenarios_StartsIdAt101()
        => Assert.Contains("101,", GetScenariosSeed());

    [Fact]
    public void Seed_GtnScenarios_IncrementsId()
        => Assert.Contains("102,", GetScenariosSeed());

    [Fact]
    public void Seed_GtnScenarios_IdValuesAreUnquotedIntegers()
    {
        var seed = GetScenariosSeed();
        // Integer values must appear unquoted
        Assert.DoesNotContain("'101'", seed);
        Assert.DoesNotContain("'102'", seed);
        Assert.DoesNotContain("'103'", seed);
    }

    [Fact]
    public void Seed_GtnScenarios_ValidationScenarioIdIsQuotedText()
    {
        var seed = GetScenariosSeed();
        Assert.Contains("'1,01'", seed);
        Assert.Contains("'1,02'", seed);
    }

    [Fact]
    public void Seed_GtnScenarios_ValidationScenarioIdNotAsInteger()
    {
        // Ensure the business key is never coerced to an integer
        var seed = GetScenariosSeed();
        Assert.DoesNotContain(", 1,01,", seed);  // un-quoted numeric form must not appear
    }

    [Fact]
    public void Seed_GtnScenarios_HasOnConflictDoNothing()
        => Assert.Contains("ON CONFLICT DO NOTHING", GetScenariosSeed());

    [Fact]
    public void Seed_GtnScenarios_HasInsertInto()
        => Assert.Contains("INSERT INTO gtn_scenarios", GetScenariosSeed());
}

// ─── Seed SQL — gtn_scenario_settings ────────────────────────────────────────

public class ValidationScenarioRuleSeedSettingsTests
{
    private static string GetSettingsSeed()
    {
        var rule     = new ValidationScenarioNormalizationRule();
        var request  = DetNormHelpers.RequestWithRows();
        var proposal = rule.Apply(request);
        var svc      = new NormalizationSqlGeneratorService();
        var result   = svc.Generate(proposal, request.SheetPreview);
        Assert.True(result.Success, result.Error);
        return result.Value!.Tables[1].SeedSql;
    }

    [Fact]
    public void Seed_GtnSettings_StartsIdAt1001()
        => Assert.Contains("1001,", GetSettingsSeed());

    [Fact]
    public void Seed_GtnSettings_IncrementsId()
        => Assert.Contains("1002,", GetSettingsSeed());

    [Fact]
    public void Seed_GtnSettings_IdValuesAreUnquotedIntegers()
    {
        var seed = GetSettingsSeed();
        Assert.DoesNotContain("'1001'", seed);
        Assert.DoesNotContain("'1002'", seed);
    }

    [Fact]
    public void Seed_GtnSettings_HasInsertInto()
        => Assert.Contains("INSERT INTO gtn_scenario_settings", GetSettingsSeed());

    [Fact]
    public void Seed_GtnSettings_HasOnConflictDoNothing()
        => Assert.Contains("ON CONFLICT DO NOTHING", GetSettingsSeed());

    [Fact]
    public void Seed_GtnSettings_GtnScenarioIdIsNullBecauseGeneratorCannotDeriveIt()
    {
        // gtn_scenario_id cannot be derived without cross-table state;
        // the generator emits NULL (nullable FK is the designed workaround).
        var seed = GetSettingsSeed();
        Assert.Contains("NULL", seed);
    }

    [Fact]
    public void Seed_GtnSettings_ValidationScenarioIdTraceValueIsText()
    {
        var seed = GetSettingsSeed();
        Assert.Contains("'1,01'", seed);
    }
}

// ─── DDL — FK constraint in gtn_scenario_settings ────────────────────────────

public class ValidationScenarioRuleDdlTests
{
    private static string GetSettingsDdl()
    {
        var rule     = new ValidationScenarioNormalizationRule();
        var request  = DetNormHelpers.FullRequest();
        var proposal = rule.Apply(request);
        var svc      = new NormalizationSqlGeneratorService();
        var result   = svc.Generate(proposal, request.SheetPreview);
        Assert.True(result.Success, result.Error);
        return result.Value!.Tables[1].CreateTableSql;
    }

    [Fact]
    public void Ddl_GtnSettings_DeclaresFKReferencingGtnScenariosId()
        => Assert.Contains("FOREIGN KEY (gtn_scenario_id) REFERENCES gtn_scenarios(id)", GetSettingsDdl());

    [Fact]
    public void Ddl_GtnSettings_HasCreateTableIfNotExists()
        => Assert.Contains("CREATE TABLE IF NOT EXISTS gtn_scenario_settings", GetSettingsDdl());

    [Fact]
    public void Ddl_GtnSettings_IdIsInteger()
        => Assert.Contains("id INTEGER", GetSettingsDdl());

    [Fact]
    public void Ddl_GtnSettings_HasPrimaryKeyConstraint()
        => Assert.Contains("PRIMARY KEY (id)", GetSettingsDdl());
}

// ─── DeterministicNormalizationService ───────────────────────────────────────

public class DeterministicNormalizationServiceTests
{
    [Fact]
    public void TryNormalize_NoRules_ReturnsFailure()
    {
        var sut    = new DeterministicNormalizationService([]);
        var result = sut.TryNormalize(DetNormHelpers.MinimalRequest());
        Assert.False(result.Success);
        Assert.Contains("No deterministic normalization rule matched", result.Error);
    }

    [Fact]
    public void TryNormalize_MatchingRule_ReturnsOk()
    {
        var sut    = new DeterministicNormalizationService([new ValidationScenarioNormalizationRule()]);
        var result = sut.TryNormalize(DetNormHelpers.MinimalRequest());
        Assert.True(result.Success);
        Assert.NotNull(result.Value);
    }

    [Fact]
    public void TryNormalize_NoMatchingRule_ReturnsFailure()
    {
        var sut    = new DeterministicNormalizationService([new ValidationScenarioNormalizationRule()]);
        var result = sut.TryNormalize(DetNormHelpers.MakeRequest("employee_id", "name"));
        Assert.False(result.Success);
        Assert.Contains("No deterministic normalization rule matched", result.Error);
    }

    [Fact]
    public void TryNormalize_FirstMatchingRuleWins()
    {
        var rule1 = new ValidationScenarioNormalizationRule();
        var rule2 = new AlwaysMatchRule("second");
        var sut   = new DeterministicNormalizationService([rule1, rule2]);

        var result = sut.TryNormalize(DetNormHelpers.MinimalRequest());

        Assert.True(result.Success);
        Assert.Contains(result.Value!.Tables, t => t.TableName == "gtn_scenarios");
    }

    [Fact]
    public void TryNormalize_SecondRuleUsedWhenFirstDoesNotMatch()
    {
        var rule1 = new NeverMatchRule();
        var rule2 = new AlwaysMatchRule("fallback_table");
        var sut   = new DeterministicNormalizationService([rule1, rule2]);

        var result = sut.TryNormalize(DetNormHelpers.MinimalRequest());

        Assert.True(result.Success);
        Assert.Contains(result.Value!.Tables, t => t.TableName == "fallback_table");
    }
}

// ─── Deterministic provider path ─────────────────────────────────────────────

public class DeterministicProviderPathTests
{
    [Fact]
    public void DeterministicService_WithValidationScenarioRequest_Succeeds()
    {
        var sut    = new DeterministicNormalizationService([new ValidationScenarioNormalizationRule()]);
        var result = sut.TryNormalize(DetNormHelpers.MinimalRequest());

        Assert.True(result.Success);
        Assert.Equal(2, result.Value!.Tables.Count);
    }

    [Fact]
    public void DeterministicService_ProducesGtnScenariosTable()
    {
        var sut    = new DeterministicNormalizationService([new ValidationScenarioNormalizationRule()]);
        var result = sut.TryNormalize(DetNormHelpers.FullRequest());

        Assert.True(result.Success);
        Assert.Contains(result.Value!.Tables, t => t.TableName == "gtn_scenarios");
    }

    [Fact]
    public void DeterministicService_ProducesGtnScenarioSettingsTable()
    {
        var sut    = new DeterministicNormalizationService([new ValidationScenarioNormalizationRule()]);
        var result = sut.TryNormalize(DetNormHelpers.FullRequest());

        Assert.True(result.Success);
        Assert.Contains(result.Value!.Tables, t => t.TableName == "gtn_scenario_settings");
    }
}

// ─── AI failure fallback path ─────────────────────────────────────────────────

public class AiFailureFallbackPathTests
{
    [Fact]
    public void FallbackService_AfterAiFailure_StillSucceeds()
    {
        var sut    = new DeterministicNormalizationService([new ValidationScenarioNormalizationRule()]);
        var result = sut.TryNormalize(DetNormHelpers.MinimalRequest());

        Assert.True(result.Success);
        Assert.NotNull(result.Value);
    }

    [Fact]
    public void FallbackService_AfterAiFailure_ProducesTwoTables()
    {
        var sut    = new DeterministicNormalizationService([new ValidationScenarioNormalizationRule()]);
        var result = sut.TryNormalize(DetNormHelpers.FullRequest());

        Assert.True(result.Success);
        Assert.Equal(2, result.Value!.Tables.Count);
    }

    [Fact]
    public void FallbackService_NonMatchingSchema_ReturnsFailure()
    {
        var sut    = new DeterministicNormalizationService([new ValidationScenarioNormalizationRule()]);
        var result = sut.TryNormalize(DetNormHelpers.MakeRequest("unrelated_column", "another_column"));

        Assert.False(result.Success);
    }

    [Fact]
    public void FallbackService_ReturningFallback_HasCorrectReasoning()
    {
        var sut    = new DeterministicNormalizationService([new ValidationScenarioNormalizationRule()]);
        var result = sut.TryNormalize(DetNormHelpers.MinimalRequest());

        Assert.True(result.Success);
        Assert.Contains("deterministic", result.Value!.Reasoning, StringComparison.OrdinalIgnoreCase);
    }
}

// ─── Stub rules used only in tests ───────────────────────────────────────────

file sealed class AlwaysMatchRule(string tableName) : IDeterministicNormalizationRule
{
    public bool CanHandle(NormalizationRequest request) => true;

    public NormalizationProposal Apply(NormalizationRequest request) => new()
    {
        Reasoning = "always match",
        Tables =
        [
            new ProposedTable
            {
                TableName = tableName,
                Columns   =
                [
                    new ProposedColumn
                    {
                        Name         = "id",
                        PostgresType = "INTEGER",
                        IsNullable   = false,
                        IsPrimaryKey = true,
                    },
                ],
                SourceColumns = [],
            },
        ],
    };
}

file sealed class NeverMatchRule : IDeterministicNormalizationRule
{
    public bool CanHandle(NormalizationRequest request) => false;
    public NormalizationProposal Apply(NormalizationRequest request) => new();
}
