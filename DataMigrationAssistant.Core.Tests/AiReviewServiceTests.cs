using DataMigrationAssistant.Core.Models;
using DataMigrationAssistant.Core.Services;

namespace DataMigrationAssistant.Core.Tests;

public class NullAiReviewServiceTests
{
    private readonly IAiReviewService _sut = new NullAiReviewService();

    [Fact]
    public async Task ReviewAsync_ReturnsEmptySummary()
    {
        var result = await _sut.ReviewAsync(new AiReviewRequest());
        Assert.Equal(string.Empty, result.Summary);
    }

    [Fact]
    public async Task ReviewAsync_ReturnsEmptyRisks()
    {
        var result = await _sut.ReviewAsync(new AiReviewRequest());
        Assert.Empty(result.Risks);
    }

    [Fact]
    public async Task ReviewAsync_ReturnsEmptyRecommendations()
    {
        var result = await _sut.ReviewAsync(new AiReviewRequest());
        Assert.Empty(result.Recommendations);
    }
}

public class AiReviewPromptBuilderTests
{
    [Fact]
    public void BuildUserMessage_LimitsSampleRowsToTwenty()
    {
        var rows = Enumerable.Range(1, 30)
            .Select(i => (IReadOnlyDictionary<string, string?>)new Dictionary<string, string?> { ["id"] = i.ToString() })
            .ToList();

        var preview = new SheetPreview
        {
            SheetName = "Sheet1",
            FilePath  = "test.xlsx",
            Columns   = [new ColumnInfo { Index = 0, Name = "id", SnakeCaseName = "id" }],
            Rows      = rows,
            TotalRowCount = 30,
        };

        var request = new AiReviewRequest { SheetPreview = preview };
        var message = AiReviewPromptBuilder.BuildUserMessage(request);

        Assert.Contains("count=\"20\"", message);
        Assert.DoesNotContain("Row 21:", message);
    }

    [Fact]
    public void BuildUserMessage_IncludesTableName()
    {
        var schema = new TableSchema { TableName = "employees", SheetName = "Sheet1" };
        var request = new AiReviewRequest { TableSchema = schema };

        var message = AiReviewPromptBuilder.BuildUserMessage(request);

        Assert.Contains("employees", message);
    }

    [Fact]
    public void BuildUserMessage_IncludesValidationWarnings()
    {
        var validation = new ValidationResult
        {
            Warnings = [new ValidationWarning { Message = "Missing primary key", Severity = ValidationSeverity.Warning }],
        };
        var request = new AiReviewRequest { ValidationResult = validation };

        var message = AiReviewPromptBuilder.BuildUserMessage(request);

        Assert.Contains("Missing primary key", message);
    }

    [Fact]
    public void BuildUserMessage_ShowsNoneWhenNoWarnings()
    {
        var request = new AiReviewRequest { ValidationResult = new ValidationResult() };
        var message = AiReviewPromptBuilder.BuildUserMessage(request);
        Assert.Contains("(none)", message);
    }

    [Fact]
    public void BuildUserMessage_IncludesDiffSummaryWhenPresent()
    {
        var diffResult = new SeedDiffResult
        {
            TableName    = "employees",
            KeyColumnName = "id",
            Rows =
            [
                new SeedDiffRow { Status = SeedDiffStatus.Added },
                new SeedDiffRow { Status = SeedDiffStatus.Removed },
                new SeedDiffRow { Status = SeedDiffStatus.Changed },
            ],
        };
        var request = new AiReviewRequest { SeedDiffResult = diffResult };

        var message = AiReviewPromptBuilder.BuildUserMessage(request);

        Assert.Contains("<diff_summary>", message);
        Assert.Contains("Added rows", message);
    }

    [Fact]
    public void BuildUserMessage_IncludesMigrationSqlWhenPresent()
    {
        var request = new AiReviewRequest { MigrationSql = "INSERT INTO employees VALUES (1);" };
        var message = AiReviewPromptBuilder.BuildUserMessage(request);
        Assert.Contains("INSERT INTO employees", message);
    }

    [Fact]
    public void BuildUserMessage_OmitsMigrationSqlWhenNull()
    {
        var request = new AiReviewRequest { MigrationSql = null };
        var message = AiReviewPromptBuilder.BuildUserMessage(request);
        // The preamble mentions <migration_sql> by name; check for the closing tag
        // which only appears when the actual SQL block is emitted.
        Assert.DoesNotContain("</migration_sql>", message);
    }

    [Fact]
    public void BuildUserMessage_NullValuesRenderedAsNULL()
    {
        var rows = new List<IReadOnlyDictionary<string, string?>>
        {
            new Dictionary<string, string?> { ["name"] = null }
        };
        var preview = new SheetPreview
        {
            SheetName = "Sheet1", FilePath = "f.xlsx",
            Columns = [new ColumnInfo { Index = 0, Name = "name", SnakeCaseName = "name" }],
            Rows = rows, TotalRowCount = 1,
        };
        var request = new AiReviewRequest { SheetPreview = preview };

        var message = AiReviewPromptBuilder.BuildUserMessage(request);

        Assert.Contains("name=NULL", message);
    }
}

public class AiReviewResponseParserTests
{
    [Fact]
    public void Parse_ValidJson_ReturnsMappedResult()
    {
        var json = """
            {
              "summary": "Migration looks good.",
              "risks": [{"level":"HIGH","description":"Large row count"}],
              "recommendations": [{"priority":"MEDIUM","description":"Add index","action":"CREATE INDEX ..."}]
            }
            """;

        var result = AiReviewResponseParser.Parse(json);

        Assert.Equal("Migration looks good.", result.Summary);
        Assert.Single(result.Risks);
        Assert.Equal("HIGH", result.Risks[0].Level);
        Assert.Equal("Large row count", result.Risks[0].Description);
        Assert.Single(result.Recommendations);
        Assert.Equal("MEDIUM", result.Recommendations[0].Priority);
        Assert.Equal("Add index", result.Recommendations[0].Description);
        Assert.Equal("CREATE INDEX ...", result.Recommendations[0].Action);
    }

    [Fact]
    public void Parse_EmptyArrays_ReturnsEmptyLists()
    {
        var json = """{"summary":"OK","risks":[],"recommendations":[]}""";

        var result = AiReviewResponseParser.Parse(json);

        Assert.Equal("OK", result.Summary);
        Assert.Empty(result.Risks);
        Assert.Empty(result.Recommendations);
    }

    [Fact]
    public void Parse_NullOrWhitespace_ReturnsFallback()
    {
        var result = AiReviewResponseParser.Parse(string.Empty);
        Assert.Equal("No response from AI.", result.Summary);
    }

    [Fact]
    public void Parse_InvalidJson_ReturnsFallback()
    {
        var result = AiReviewResponseParser.Parse("not valid json {{{");
        Assert.Equal("Could not parse AI response.", result.Summary);
    }

    [Fact]
    public void Parse_RecommendationWithNullAction_MapsCorrectly()
    {
        var json = """
            {
              "summary":"s",
              "risks":[],
              "recommendations":[{"priority":"LOW","description":"desc","action":null}]
            }
            """;

        var result = AiReviewResponseParser.Parse(json);

        Assert.Null(result.Recommendations[0].Action);
    }

    [Fact]
    public void Parse_MissingFields_DoesNotThrow()
    {
        var json = """{"summary":"partial"}""";

        var result = AiReviewResponseParser.Parse(json);

        Assert.Equal("partial", result.Summary);
        Assert.Empty(result.Risks);
        Assert.Empty(result.Recommendations);
    }
}

public class ClaudeAiReviewServiceTests
{
    [Fact]
    public async Task ReviewAsync_MissingApiKey_ThrowsInvalidOperationException()
    {
        var sut = new ClaudeAiReviewService(null);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.ReviewAsync(new AiReviewRequest()));
    }

    [Fact]
    public async Task ReviewAsync_EmptyApiKey_ThrowsInvalidOperationException()
    {
        var sut = new ClaudeAiReviewService(string.Empty);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.ReviewAsync(new AiReviewRequest()));
    }

    [Fact]
    public async Task ReviewAsync_WhitespaceApiKey_ThrowsInvalidOperationException()
    {
        var sut = new ClaudeAiReviewService("   ");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.ReviewAsync(new AiReviewRequest()));
    }
}

public class AiReviewSystemPromptTests
{
    [Fact]
    public void SystemPrompt_InstructsToInspectSqlBeforeRecommending()
    {
        Assert.Contains("inspect the <migration_sql>", AiReviewPromptBuilder.SystemPrompt);
    }

    [Fact]
    public void SystemPrompt_InstructsNotToRepeatExistingClauses()
    {
        Assert.Contains(
            "do not suggest adding SQL clauses that are already present in it",
            AiReviewPromptBuilder.SystemPrompt);
    }

    [Fact]
    public void SystemPrompt_InstructsNotToRepeatOnConflictDoNothing()
    {
        Assert.Contains("ON CONFLICT DO NOTHING", AiReviewPromptBuilder.SystemPrompt);
    }

    [Fact]
    public void SystemPrompt_ContainsSqlCommentsInstruction()
    {
        Assert.Contains(
            "Lines starting with -- are SQL comments and must not be treated as executable SQL.",
            AiReviewPromptBuilder.SystemPrompt);
    }

    [Fact]
    public void SystemPrompt_ExplainsRemovedRowsAreNotDeleted()
    {
        Assert.Contains(
            "Removed rows appear as commented-out lines in the SQL",
            AiReviewPromptBuilder.SystemPrompt);
    }

    [Fact]
    public void SystemPrompt_PrefersStaginRecommendation()
    {
        Assert.Contains("staging", AiReviewPromptBuilder.SystemPrompt);
    }

    [Fact]
    public void SystemPrompt_PrefersTransactionRecommendation()
    {
        Assert.Contains("transaction", AiReviewPromptBuilder.SystemPrompt);
    }

    [Fact]
    public void SystemPrompt_PrefersRowCountCheckRecommendation()
    {
        Assert.Contains("row counts", AiReviewPromptBuilder.SystemPrompt);
    }

    [Fact]
    public void SystemPrompt_PrefersWhereClauseVerificationRecommendation()
    {
        Assert.Contains("WHERE clauses", AiReviewPromptBuilder.SystemPrompt);
    }

    [Fact]
    public void SystemPrompt_RequiresConcreteActionValues()
    {
        Assert.Contains("concrete", AiReviewPromptBuilder.SystemPrompt);
    }

    [Fact]
    public void SystemPrompt_InstructsToReviewRemovedRowComments()
    {
        Assert.Contains("removed-row comments", AiReviewPromptBuilder.SystemPrompt);
    }
}

public class AiReviewUserMessagePreambleTests
{
    [Fact]
    public void BuildUserMessage_ContainsInspectionPreamble()
    {
        var message = AiReviewPromptBuilder.BuildUserMessage(new AiReviewRequest());
        Assert.Contains("Inspect the <migration_sql>", message);
    }

    [Fact]
    public void BuildUserMessage_InstructsNotToRepeatExistingClauses()
    {
        var message = AiReviewPromptBuilder.BuildUserMessage(new AiReviewRequest());
        Assert.Contains("Do not suggest adding any SQL clause that already appears in it.", message);
    }

    [Fact]
    public void BuildUserMessage_PreambleAppearsBeforeSchema()
    {
        var message = AiReviewPromptBuilder.BuildUserMessage(new AiReviewRequest());
        var preambleIndex = message.IndexOf("Inspect the <migration_sql>", StringComparison.Ordinal);
        var schemaIndex   = message.IndexOf("<schema>", StringComparison.Ordinal);
        Assert.True(preambleIndex < schemaIndex, "Preamble should appear before <schema>");
    }
}

public class AiReviewModeTests
{
    [Fact]
    public void DefaultAiReviewRequest_HasMigrationMode()
    {
        Assert.Equal(AiReviewMode.Migration, new AiReviewRequest().Mode);
    }

    [Fact]
    public void DatasetSystemPrompt_IsDatasetFocused()
    {
        Assert.Contains("data quality analyst", AiReviewPromptBuilder.DatasetSystemPrompt);
    }

    [Fact]
    public void DatasetSystemPrompt_DoesNotMentionMigrationReviewer()
    {
        Assert.DoesNotContain("migration reviewer", AiReviewPromptBuilder.DatasetSystemPrompt);
    }

    [Fact]
    public void GetSystemPrompt_Migration_ReturnsMigrationSystemPrompt()
    {
        Assert.Equal(AiReviewPromptBuilder.SystemPrompt, AiReviewPromptBuilder.GetSystemPrompt(AiReviewMode.Migration));
    }

    [Fact]
    public void GetSystemPrompt_Dataset_ReturnsDatasetSystemPrompt()
    {
        Assert.Equal(AiReviewPromptBuilder.DatasetSystemPrompt, AiReviewPromptBuilder.GetSystemPrompt(AiReviewMode.Dataset));
    }

    [Fact]
    public void BuildUserMessage_Dataset_OmitsMigrationSqlBlock()
    {
        var request = new AiReviewRequest
        {
            Mode         = AiReviewMode.Dataset,
            MigrationSql = "INSERT INTO foo VALUES (1);",
        };
        var message = AiReviewPromptBuilder.BuildUserMessage(request);
        Assert.DoesNotContain("<migration_sql>", message);
    }

    [Fact]
    public void BuildUserMessage_Dataset_OmitsDiffSummary()
    {
        var diffResult = new SeedDiffResult
        {
            TableName     = "foo",
            KeyColumnName = "id",
            Rows          = [new SeedDiffRow { Status = SeedDiffStatus.Added }],
        };
        var request = new AiReviewRequest
        {
            Mode           = AiReviewMode.Dataset,
            SeedDiffResult = diffResult,
        };
        var message = AiReviewPromptBuilder.BuildUserMessage(request);
        Assert.DoesNotContain("<diff_summary>", message);
    }

    [Fact]
    public void BuildUserMessage_Dataset_IncludesSampleRows()
    {
        var rows = new List<IReadOnlyDictionary<string, string?>>
        {
            new Dictionary<string, string?> { ["name"] = "Alice" }
        };
        var preview = new SheetPreview
        {
            SheetName     = "Sheet1",
            FilePath      = "f.xlsx",
            Columns       = [new ColumnInfo { Index = 0, Name = "name", SnakeCaseName = "name" }],
            Rows          = rows,
            TotalRowCount = 1,
        };
        var request = new AiReviewRequest { Mode = AiReviewMode.Dataset, SheetPreview = preview };

        var message = AiReviewPromptBuilder.BuildUserMessage(request);

        Assert.Contains("<sample_rows", message);
        Assert.Contains("name=Alice", message);
    }

    [Fact]
    public void BuildUserMessage_Dataset_IncludesSchema()
    {
        var schema  = new TableSchema { TableName = "employees", SheetName = "Sheet1" };
        var request = new AiReviewRequest { Mode = AiReviewMode.Dataset, TableSchema = schema };

        var message = AiReviewPromptBuilder.BuildUserMessage(request);

        Assert.Contains("<schema>", message);
        Assert.Contains("employees", message);
    }

    [Fact]
    public void BuildUserMessage_Dataset_IncludesValidationWarnings()
    {
        var validation = new ValidationResult
        {
            Warnings = [new ValidationWarning { Message = "Duplicate key", Severity = ValidationSeverity.Warning }],
        };
        var request = new AiReviewRequest { Mode = AiReviewMode.Dataset, ValidationResult = validation };

        var message = AiReviewPromptBuilder.BuildUserMessage(request);

        Assert.Contains("<validation_warnings>", message);
        Assert.Contains("Duplicate key", message);
    }

    [Fact]
    public void BuildUserMessage_Migration_IncludesMigrationSqlWhenProvided()
    {
        var request = new AiReviewRequest
        {
            Mode         = AiReviewMode.Migration,
            MigrationSql = "INSERT INTO foo VALUES (1);",
        };

        var message = AiReviewPromptBuilder.BuildUserMessage(request);

        Assert.Contains("<migration_sql>", message);
        Assert.Contains("INSERT INTO foo", message);
    }
}

public class DatasetSystemPromptCandidateKeyRulesTests
{
    [Fact]
    public void DatasetSystemPrompt_SaysMultipleCandidateKeysAreNotCompositeKey()
    {
        Assert.Contains("NOT a composite key", AiReviewPromptBuilder.DatasetSystemPrompt);
    }

    [Fact]
    public void DatasetSystemPrompt_ForbidsUniqueConstraintCombiningAllCandidates()
    {
        Assert.Contains(
            "combines all candidate key columns together",
            AiReviewPromptBuilder.DatasetSystemPrompt);
    }

    [Fact]
    public void DatasetSystemPrompt_SaysFirstCandidateIsRecommendedPrimaryKey()
    {
        Assert.Contains(
            "first listed candidate key column is the system-selected recommended primary key",
            AiReviewPromptBuilder.DatasetSystemPrompt);
    }

    [Fact]
    public void DatasetSystemPrompt_SaysValueColumnsNotKeyOrUnique()
    {
        Assert.Contains("score, amount, or rate", AiReviewPromptBuilder.DatasetSystemPrompt);
        Assert.Contains(
            "should not be recommended as keys or UNIQUE constraints",
            AiReviewPromptBuilder.DatasetSystemPrompt);
    }
}

public class DatasetSystemPromptEvidenceRulesTests
{
    [Fact]
    public void DatasetSystemPrompt_OnlyReportsEvidenceBackedRisks()
    {
        Assert.Contains(
            "Only report risks and recommendations that are directly supported by the provided schema, validation warnings, or sample rows.",
            AiReviewPromptBuilder.DatasetSystemPrompt);
    }

    [Fact]
    public void DatasetSystemPrompt_StatesIfEvidenceAbsentDoNotReport()
    {
        Assert.Contains(
            "If evidence is not present in context, do not report the issue.",
            AiReviewPromptBuilder.DatasetSystemPrompt);
    }

    [Fact]
    public void DatasetSystemPrompt_StatesAbsenceOfEvidenceIsNotRisk()
    {
        Assert.Contains(
            "Absence of evidence is not evidence of risk.",
            AiReviewPromptBuilder.DatasetSystemPrompt);
    }

    [Fact]
    public void DatasetSystemPrompt_RequiresEvidenceCitationForEveryRisk()
    {
        Assert.Contains(
            "Every risk must cite its evidence from the schema, validation warnings, or sample rows.",
            AiReviewPromptBuilder.DatasetSystemPrompt);
    }

    [Fact]
    public void DatasetSystemPrompt_ForbidsBooleanConversionForBooleanColumns()
    {
        Assert.Contains(
            "If the schema already inferred BOOLEAN for a column, do not recommend boolean conversion",
            AiReviewPromptBuilder.DatasetSystemPrompt);
    }

    [Fact]
    public void DatasetSystemPrompt_ForbidsDecimalNormalizationWithoutFormattingWarning()
    {
        Assert.Contains(
            "no decimal or formatting warning exists in validation_warnings, do not recommend decimal normalization.",
            AiReviewPromptBuilder.DatasetSystemPrompt);
    }

    [Fact]
    public void DatasetSystemPrompt_ForbidsDuplicateRiskForCandidateKeyWithoutDetection()
    {
        Assert.Contains(
            "Candidate key columns should not be reported as duplicate risks unless duplicate values are explicitly detected",
            AiReviewPromptBuilder.DatasetSystemPrompt);
    }

    [Fact]
    public void DatasetSystemPrompt_RequiresEvidenceFieldInRisksJson()
    {
        Assert.Contains("\"evidence\":string", AiReviewPromptBuilder.DatasetSystemPrompt);
    }

    [Fact]
    public void DatasetSystemPrompt_RequiresEvidenceFieldInRecommendationsJson()
    {
        Assert.Contains("\"evidence\":string", AiReviewPromptBuilder.DatasetSystemPrompt);
    }

    [Fact]
    public void DatasetSystemPrompt_InstructsToOmitItemsWithNoContextEvidence()
    {
        Assert.Contains(
            "If no context evidence exists for an item, omit that item entirely.",
            AiReviewPromptBuilder.DatasetSystemPrompt);
    }

    [Fact]
    public void DatasetSystemPrompt_HasFactualAccuracyRules()
    {
        Assert.Contains("Factual accuracy rules:", AiReviewPromptBuilder.DatasetSystemPrompt);
    }

    [Fact]
    public void DatasetSystemPrompt_ForbidsTextClaimForNumericOrBooleanColumn()
    {
        Assert.Contains(
            "Do not claim a column stores values as text if the schema infers NUMERIC or BOOLEAN for that column.",
            AiReviewPromptBuilder.DatasetSystemPrompt);
    }

    [Fact]
    public void DatasetSystemPrompt_ForbidsCommaDecimalClaimWithoutExplicitEvidence()
    {
        Assert.Contains(
            "Do not claim comma decimal issues unless sample rows or validation warnings explicitly contain comma-formatted numbers.",
            AiReviewPromptBuilder.DatasetSystemPrompt);
    }

    [Fact]
    public void DatasetSystemPrompt_ForbidsNullabilityClaimWithoutEvidence()
    {
        Assert.Contains(
            "Do not claim nullability risk unless the schema marks the column nullable or validation warnings report missing values.",
            AiReviewPromptBuilder.DatasetSystemPrompt);
    }

    [Fact]
    public void DatasetSystemPrompt_StatesFalseIsNotNullabilityEvidence()
    {
        Assert.Contains(
            "A non-null value such as FALSE or 0 is not evidence of nullability.",
            AiReviewPromptBuilder.DatasetSystemPrompt);
    }

    [Fact]
    public void DatasetSystemPrompt_StatesCommaDecimalAloneNotProofOfTextStorage()
    {
        Assert.Contains(
            "The presence of a comma in a value alone does not prove the column is stored as text.",
            AiReviewPromptBuilder.DatasetSystemPrompt);
    }

    [Fact]
    public void DatasetSystemPrompt_StatesEvidenceMustLogicallySupportClaim()
    {
        Assert.Contains(
            "Evidence must logically support the claim.",
            AiReviewPromptBuilder.DatasetSystemPrompt);
    }

    [Fact]
    public void DatasetSystemPrompt_StatesHavingEvidenceIsNotSufficient()
    {
        Assert.Contains(
            "Having evidence is not sufficient",
            AiReviewPromptBuilder.DatasetSystemPrompt);
    }

    [Fact]
    public void DatasetSystemPrompt_ForbidsIncorrectTypeInferenceForNumericColumn()
    {
        Assert.Contains(
            "do not report incorrect type inference",
            AiReviewPromptBuilder.DatasetSystemPrompt);
    }

    [Fact]
    public void DatasetSystemPrompt_StatesCommaSeparatorDoesNotImplyIncorrectTypeInference()
    {
        Assert.Contains(
            "do not imply incorrect type inference",
            AiReviewPromptBuilder.DatasetSystemPrompt);
    }

    [Fact]
    public void DatasetSystemPrompt_RequiresTypeInferenceRiskOnlyForTextColumns()
    {
        Assert.Contains(
            "Only report type inference risk when the schema inferred TEXT",
            AiReviewPromptBuilder.DatasetSystemPrompt);
    }
}

public class DatasetEvidenceFilterTests
{
    private static AiReviewRisk MakeRisk(string? evidence) =>
        new() { Level = "HIGH", Description = "Test risk", Evidence = evidence };

    private static AiReviewRecommendation MakeRec(string? evidence) =>
        new() { Priority = "HIGH", Description = "Test rec", Evidence = evidence };

    private static AiReviewResult MakeResult(
        IReadOnlyList<AiReviewRisk>? risks = null,
        IReadOnlyList<AiReviewRecommendation>? recs = null) =>
        new()
        {
            Summary         = "Test",
            Risks           = risks ?? [],
            Recommendations = recs  ?? [],
        };

    [Fact]
    public void DatasetMode_FiltersRisk_WhenEvidenceIsNull()
    {
        var result   = MakeResult(risks: [MakeRisk(null)]);
        var filtered = AiReviewEvidenceFilter.Apply(result, AiReviewMode.Dataset);
        Assert.Empty(filtered.Risks);
    }

    [Fact]
    public void DatasetMode_FiltersRisk_WhenEvidenceIsEmpty()
    {
        var result   = MakeResult(risks: [MakeRisk("")]);
        var filtered = AiReviewEvidenceFilter.Apply(result, AiReviewMode.Dataset);
        Assert.Empty(filtered.Risks);
    }

    [Fact]
    public void DatasetMode_FiltersRisk_WhenEvidenceHasNoContextWords()
    {
        var result   = MakeResult(risks: [MakeRisk("This might be an issue.")]);
        var filtered = AiReviewEvidenceFilter.Apply(result, AiReviewMode.Dataset);
        Assert.Empty(filtered.Risks);
    }

    [Fact]
    public void DatasetMode_FiltersRecommendation_WhenEvidenceIsNull()
    {
        var result   = MakeResult(recs: [MakeRec(null)]);
        var filtered = AiReviewEvidenceFilter.Apply(result, AiReviewMode.Dataset);
        Assert.Empty(filtered.Recommendations);
    }

    [Fact]
    public void DatasetMode_FiltersRecommendation_WhenEvidenceIsEmpty()
    {
        var result   = MakeResult(recs: [MakeRec("")]);
        var filtered = AiReviewEvidenceFilter.Apply(result, AiReviewMode.Dataset);
        Assert.Empty(filtered.Recommendations);
    }

    [Fact]
    public void DatasetMode_KeepsRisk_WhenEvidenceReferencesValidation()
    {
        var result   = MakeResult(risks: [MakeRisk("Validation warning: duplicate id values found")]);
        var filtered = AiReviewEvidenceFilter.Apply(result, AiReviewMode.Dataset);
        Assert.Single(filtered.Risks);
    }

    [Fact]
    public void DatasetMode_KeepsRisk_WhenEvidenceReferencesSchema()
    {
        var result   = MakeResult(risks: [MakeRisk("Schema marks column 'name' as nullable (NULL)")]);
        var filtered = AiReviewEvidenceFilter.Apply(result, AiReviewMode.Dataset);
        Assert.Single(filtered.Risks);
    }

    [Fact]
    public void DatasetMode_KeepsRisk_WhenEvidenceReferencesSampleRows()
    {
        var result   = MakeResult(risks: [MakeRisk("Sample rows 3 and 7 both have id=42")]);
        var filtered = AiReviewEvidenceFilter.Apply(result, AiReviewMode.Dataset);
        Assert.Single(filtered.Risks);
    }

    [Fact]
    public void DatasetMode_KeepsRecommendation_WhenEvidenceReferencesAnalysis()
    {
        var result   = MakeResult(recs: [MakeRec("Data analysis shows 40% null rate in column 'notes'")]);
        var filtered = AiReviewEvidenceFilter.Apply(result, AiReviewMode.Dataset);
        Assert.Single(filtered.Recommendations);
    }

    [Fact]
    public void MigrationMode_DoesNotFilterRisk_WithoutEvidence()
    {
        var result   = MakeResult(risks: [MakeRisk(null)]);
        var filtered = AiReviewEvidenceFilter.Apply(result, AiReviewMode.Migration);
        Assert.Single(filtered.Risks);
    }

    [Fact]
    public void MigrationMode_DoesNotFilterRecommendation_WithoutEvidence()
    {
        var result   = MakeResult(recs: [MakeRec(null)]);
        var filtered = AiReviewEvidenceFilter.Apply(result, AiReviewMode.Migration);
        Assert.Single(filtered.Recommendations);
    }

    [Fact]
    public void DatasetMode_PreservesSummary_AfterFiltering()
    {
        var result   = MakeResult(risks: [MakeRisk(null)]);
        var filtered = AiReviewEvidenceFilter.Apply(result, AiReviewMode.Dataset);
        Assert.Equal("Test", filtered.Summary);
    }

    [Fact]
    public void DatasetMode_ReturnsSameInstance_WhenNothingFiltered()
    {
        var risk   = MakeRisk("Schema column 'id' is NOT NULL");
        var result = MakeResult(risks: [risk]);
        var filtered = AiReviewEvidenceFilter.Apply(result, AiReviewMode.Dataset);
        Assert.Same(result, filtered);
    }
}

public class CommaDecimalNumericColumnTests
{
    private static AiReviewRequest MakeRequest(ColumnSchema col) =>
        new()
        {
            Mode             = AiReviewMode.Dataset,
            TableSchema      = new TableSchema { Columns = [col] },
            SheetPreview     = new SheetPreview { Rows = [] },
            ValidationResult = new ValidationResult(),
        };

    private static ColumnSchema NumericScore =>
        new() { Name = "score", SnakeCaseName = "score", InferredType = PostgresType.Numeric };

    // ── 1. HIGH type risk for comma decimal on NUMERIC is filtered ────────────

    [Fact]
    public void HighTypeRisk_CommaDecimalOnNumeric_IsFiltered()
    {
        var risk = new AiReviewRisk
        {
            Level       = "HIGH",
            Description = "Inconsistent data types detected: score has non-numeric values due to comma separators",
            Evidence    = "sample rows: score=9,5",
        };

        var result   = new AiReviewResult { Summary = "s", Risks = [risk], Recommendations = [] };
        var filtered = AiReviewClaimValidator.Apply(result, MakeRequest(NumericScore));

        Assert.Empty(filtered.Risks);
    }

    // ── 2. "text storage" claim on NUMERIC is filtered ────────────────────────

    [Fact]
    public void TextStorageClaim_NumericColumn_IsFiltered()
    {
        var risk = new AiReviewRisk
        {
            Level       = "MEDIUM",
            Description = "score column uses text storage — values contain comma separators",
            Evidence    = "sample rows: score=9,5",
        };

        var result   = new AiReviewResult { Summary = "s", Risks = [risk], Recommendations = [] };
        var filtered = AiReviewClaimValidator.Apply(result, MakeRequest(NumericScore));

        Assert.Empty(filtered.Risks);
    }

    // ── 3. "non-numeric values" claim on NUMERIC is filtered ─────────────────

    [Fact]
    public void NonNumericValuesClaim_NumericColumn_IsFiltered()
    {
        var risk = new AiReviewRisk
        {
            Level       = "HIGH",
            Description = "non-numeric values detected in score column due to comma decimal formatting",
            Evidence    = "sample rows: score=9,5",
        };

        var result   = new AiReviewResult { Summary = "s", Risks = [risk], Recommendations = [] };
        var filtered = AiReviewClaimValidator.Apply(result, MakeRequest(NumericScore));

        Assert.Empty(filtered.Risks);
    }

    // ── 4. LOW formatting note on NUMERIC is allowed ──────────────────────────

    [Fact]
    public void LowFormattingNote_NumericColumn_IsAllowed()
    {
        var risk = new AiReviewRisk
        {
            Level       = "LOW",
            Description = "score uses comma decimal notation — normalize before insert",
            Evidence    = "sample rows: score=9,5",
        };

        var result   = new AiReviewResult { Summary = "s", Risks = [risk], Recommendations = [] };
        var filtered = AiReviewClaimValidator.Apply(result, MakeRequest(NumericScore));

        Assert.Single(filtered.Risks);
    }

    // ── 5. Cast-to-numeric recommendation on NUMERIC is filtered ─────────────

    [Fact]
    public void CastToNumericRec_SchemaAlreadyNumeric_IsFiltered()
    {
        var rec = new AiReviewRecommendation
        {
            Priority    = "MEDIUM",
            Description = "Cast score to numeric before inserting to avoid type errors",
            Evidence    = "schema: score (NUMERIC, NOT NULL); sample rows: score=9,5",
        };

        var result   = new AiReviewResult { Summary = "s", Risks = [], Recommendations = [rec] };
        var filtered = AiReviewClaimValidator.Apply(result, MakeRequest(NumericScore));

        Assert.Empty(filtered.Recommendations);
    }
}

public class AiReviewClaimValidatorTests
{
    private static AiReviewRequest MakeRequest(
        IReadOnlyList<ColumnSchema>? columns = null,
        IReadOnlyList<IReadOnlyDictionary<string, string?>>? rows = null,
        IReadOnlyList<ValidationWarning>? warnings = null) =>
        new()
        {
            Mode             = AiReviewMode.Dataset,
            TableSchema      = new TableSchema { Columns = columns ?? [] },
            SheetPreview     = new SheetPreview { Rows = rows ?? [] },
            ValidationResult = new ValidationResult { Warnings = warnings ?? [] },
        };

    private static AiReviewResult MakeResult(
        IReadOnlyList<AiReviewRisk>? risks = null,
        IReadOnlyList<AiReviewRecommendation>? recs = null) =>
        new()
        {
            Summary         = "Test",
            Risks           = risks ?? [],
            Recommendations = recs  ?? [],
        };

    // ── Nullable risk ───────────────────────────────────────────────────────

    [Fact]
    public void NullableRisk_FalseIsNotNullabilityEvidence_IsFiltered()
    {
        var col = new ColumnSchema
        {
            Name = "active", SnakeCaseName = "active",
            InferredType = PostgresType.Boolean, IsNullable = false,
        };
        var request = MakeRequest(columns: [col]);
        var risk    = new AiReviewRisk
        {
            Level       = "MEDIUM",
            Description = "Nullable risk in active column",
            Evidence    = "active=FALSE",
        };

        var filtered = AiReviewClaimValidator.Apply(MakeResult(risks: [risk]), request);

        Assert.Empty(filtered.Risks);
    }

    [Fact]
    public void NullableRisk_NullableColumn_IsKept()
    {
        var col = new ColumnSchema
        {
            Name = "active", SnakeCaseName = "active",
            InferredType = PostgresType.Boolean, IsNullable = true,
        };
        var request = MakeRequest(columns: [col]);
        var risk    = new AiReviewRisk
        {
            Level       = "MEDIUM",
            Description = "Nullable risk in active column",
            Evidence    = "schema: active (BOOLEAN, NULL)",
        };

        var filtered = AiReviewClaimValidator.Apply(MakeResult(risks: [risk]), request);

        Assert.Single(filtered.Risks);
    }

    [Fact]
    public void NullableRisk_NullValueInSampleRow_IsKept()
    {
        var col = new ColumnSchema
        {
            Name = "notes", SnakeCaseName = "notes",
            InferredType = PostgresType.Text, IsNullable = false,
        };
        var rows = new List<IReadOnlyDictionary<string, string?>>
        {
            new Dictionary<string, string?> { ["notes"] = null },
        };
        var request = MakeRequest(columns: [col], rows: rows);
        var risk    = new AiReviewRisk
        {
            Level       = "LOW",
            Description = "Nullable risk in notes column",
            Evidence    = "Row 1: notes=NULL",
        };

        var filtered = AiReviewClaimValidator.Apply(MakeResult(risks: [risk]), request);

        Assert.Single(filtered.Risks);
    }

    [Fact]
    public void NullableRisk_ValidationWarningMentionsMissing_IsKept()
    {
        var col = new ColumnSchema
        {
            Name = "email", SnakeCaseName = "email",
            InferredType = PostgresType.Text, IsNullable = false,
        };
        var warnings = new List<ValidationWarning>
        {
            new() { ColumnName = "email", Message = "Column has missing values", Severity = ValidationSeverity.Warning },
        };
        var request = MakeRequest(columns: [col], warnings: warnings);
        var risk    = new AiReviewRisk
        {
            Level       = "MEDIUM",
            Description = "Nullable risk in email column",
            Evidence    = "Validation warning: Column has missing values",
        };

        var filtered = AiReviewClaimValidator.Apply(MakeResult(risks: [risk]), request);

        Assert.Single(filtered.Risks);
    }

    // ── Boolean conversion risk ─────────────────────────────────────────────

    [Fact]
    public void BooleanConversionRisk_BooleanColumn_IsFiltered()
    {
        var col = new ColumnSchema
        {
            Name = "active", SnakeCaseName = "active",
            InferredType = PostgresType.Boolean,
        };
        var request = MakeRequest(columns: [col]);
        var risk    = new AiReviewRisk
        {
            Level       = "MEDIUM",
            Description = "active column stores boolean values as text — consider boolean conversion",
            Evidence    = "sample rows: active=TRUE, active=FALSE",
        };

        var filtered = AiReviewClaimValidator.Apply(MakeResult(risks: [risk]), request);

        Assert.Empty(filtered.Risks);
    }

    [Fact]
    public void BooleanConversionRisk_TextColumn_IsKept()
    {
        var col = new ColumnSchema
        {
            Name = "active", SnakeCaseName = "active",
            InferredType = PostgresType.Text,
        };
        var request = MakeRequest(columns: [col]);
        var risk    = new AiReviewRisk
        {
            Level       = "MEDIUM",
            Description = "active column stores boolean values as text — consider boolean conversion",
            Evidence    = "schema: active (TEXT, NOT NULL); sample rows: active=TRUE, active=FALSE",
        };

        var filtered = AiReviewClaimValidator.Apply(MakeResult(risks: [risk]), request);

        Assert.Single(filtered.Risks);
    }

    // ── Duplicate risk ──────────────────────────────────────────────────────

    [Fact]
    public void DuplicateRisk_CandidateKeyWithoutActualDuplicates_IsFiltered()
    {
        var col = new ColumnSchema
        {
            Name = "id", SnakeCaseName = "id", IsCandidateKey = true,
        };
        var rows = new List<IReadOnlyDictionary<string, string?>>
        {
            new Dictionary<string, string?> { ["id"] = "1" },
            new Dictionary<string, string?> { ["id"] = "2" },
            new Dictionary<string, string?> { ["id"] = "3" },
        };
        var request = MakeRequest(columns: [col], rows: rows);
        var risk    = new AiReviewRisk
        {
            Level       = "MEDIUM",
            Description = "Duplicate risk in id column",
            Evidence    = "schema: id (TEXT, NOT NULL, candidate key)",
        };

        var filtered = AiReviewClaimValidator.Apply(MakeResult(risks: [risk]), request);

        Assert.Empty(filtered.Risks);
    }

    [Fact]
    public void DuplicateRisk_CandidateKeyWithActualDuplicates_IsKept()
    {
        var col = new ColumnSchema
        {
            Name = "id", SnakeCaseName = "id", IsCandidateKey = true,
        };
        var rows = new List<IReadOnlyDictionary<string, string?>>
        {
            new Dictionary<string, string?> { ["id"] = "1" },
            new Dictionary<string, string?> { ["id"] = "1" },
        };
        var request = MakeRequest(columns: [col], rows: rows);
        var risk    = new AiReviewRisk
        {
            Level       = "HIGH",
            Description = "Duplicate risk in id column",
            Evidence    = "Row 1 and Row 2 both have id=1",
        };

        var filtered = AiReviewClaimValidator.Apply(MakeResult(risks: [risk]), request);

        Assert.Single(filtered.Risks);
    }

    [Fact]
    public void DuplicateRisk_CandidateKeyWithDuplicateWarning_IsKept()
    {
        var col = new ColumnSchema
        {
            Name = "id", SnakeCaseName = "id", IsCandidateKey = true,
        };
        var warnings = new List<ValidationWarning>
        {
            new() { ColumnName = "id", Message = "Duplicate values detected", Severity = ValidationSeverity.Warning },
        };
        var request = MakeRequest(columns: [col], warnings: warnings);
        var risk    = new AiReviewRisk
        {
            Level       = "HIGH",
            Description = "Duplicate risk in id column",
            Evidence    = "Validation: Duplicate values detected",
        };

        var filtered = AiReviewClaimValidator.Apply(MakeResult(risks: [risk]), request);

        Assert.Single(filtered.Risks);
    }

    // ── Text storage risk ───────────────────────────────────────────────────

    [Fact]
    public void TextStorageRisk_NumericColumn_CommaDecimalAlone_IsFiltered()
    {
        var col = new ColumnSchema
        {
            Name = "score", SnakeCaseName = "score",
            InferredType = PostgresType.Numeric,
        };
        var request = MakeRequest(columns: [col]);
        var risk    = new AiReviewRisk
        {
            Level       = "MEDIUM",
            Description = "score stored as text",
            Evidence    = "score=9,5",
        };

        var filtered = AiReviewClaimValidator.Apply(MakeResult(risks: [risk]), request);

        Assert.Empty(filtered.Risks);
    }

    [Fact]
    public void TextStorageRisk_TextColumn_IsKept()
    {
        var col = new ColumnSchema
        {
            Name = "score", SnakeCaseName = "score",
            InferredType = PostgresType.Text,
        };
        var request = MakeRequest(columns: [col]);
        var risk    = new AiReviewRisk
        {
            Level       = "MEDIUM",
            Description = "score stored as text, should be numeric",
            Evidence    = "schema: score (TEXT, NOT NULL); sample rows: score=9,5",
        };

        var filtered = AiReviewClaimValidator.Apply(MakeResult(risks: [risk]), request);

        Assert.Single(filtered.Risks);
    }

    // ── Mode guard ──────────────────────────────────────────────────────────

    [Fact]
    public void MigrationMode_ClaimValidationNotApplied()
    {
        var col = new ColumnSchema
        {
            Name = "active", SnakeCaseName = "active",
            InferredType = PostgresType.Boolean, IsNullable = false,
        };
        var request = new AiReviewRequest
        {
            Mode        = AiReviewMode.Migration,
            TableSchema = new TableSchema { Columns = [col] },
        };
        var risk = new AiReviewRisk
        {
            Level       = "MEDIUM",
            Description = "Nullable risk in active column",
            Evidence    = "active=FALSE",
        };

        var filtered = AiReviewClaimValidator.Apply(MakeResult(risks: [risk]), request);

        Assert.Single(filtered.Risks);
    }

    // ── Recommendation filtering ────────────────────────────────────────────

    [Fact]
    public void BooleanConversionRecommendation_BooleanColumn_IsFiltered()
    {
        var col = new ColumnSchema
        {
            Name = "active", SnakeCaseName = "active",
            InferredType = PostgresType.Boolean,
        };
        var request = MakeRequest(columns: [col]);
        var rec     = new AiReviewRecommendation
        {
            Priority    = "MEDIUM",
            Description = "Convert active column from text to boolean",
            Evidence    = "sample rows: active=TRUE stored as text",
        };

        var filtered = AiReviewClaimValidator.Apply(MakeResult(recs: [rec]), request);

        Assert.Empty(filtered.Recommendations);
    }

    [Fact]
    public void NullableRecommendation_FalseEvidence_IsFiltered()
    {
        var col = new ColumnSchema
        {
            Name = "active", SnakeCaseName = "active",
            InferredType = PostgresType.Boolean, IsNullable = false,
        };
        var request = MakeRequest(columns: [col]);
        var rec     = new AiReviewRecommendation
        {
            Priority    = "LOW",
            Description = "Consider marking active nullable",
            Evidence    = "active=FALSE in sample row 1",
        };

        var filtered = AiReviewClaimValidator.Apply(MakeResult(recs: [rec]), request);

        Assert.Empty(filtered.Recommendations);
    }

    // ── Incorrect type inference risk ────────────────────────────────────────

    [Fact]
    public void IncorrectTypeInferenceRisk_NumericColumnWithCommaDecimal_IsFiltered()
    {
        var col = new ColumnSchema
        {
            Name = "score", SnakeCaseName = "score",
            InferredType = PostgresType.Numeric,
        };
        var request = MakeRequest(columns: [col]);
        var risk    = new AiReviewRisk
        {
            Level       = "MEDIUM",
            Description = "score column: incorrect type inference — comma values suggest text storage",
            Evidence    = "sample rows: score=9,5",
        };

        var filtered = AiReviewClaimValidator.Apply(MakeResult(risks: [risk]), request);

        Assert.Empty(filtered.Risks);
    }

    [Fact]
    public void IncorrectTypeInferenceRisk_NumericColumnWithCommaDecimal_SourceFormattingNoteIsKept()
    {
        var col = new ColumnSchema
        {
            Name = "score", SnakeCaseName = "score",
            InferredType = PostgresType.Numeric,
        };
        var request = MakeRequest(columns: [col]);
        var risk    = new AiReviewRisk
        {
            Level       = "LOW",
            Description = "score column uses comma as decimal separator — ensure source formatting is normalized before insert",
            Evidence    = "sample rows: score=9,5",
        };

        var filtered = AiReviewClaimValidator.Apply(MakeResult(risks: [risk]), request);

        Assert.Single(filtered.Risks);
    }

    [Fact]
    public void TextStorageRisk_BooleanColumn_IsFiltered()
    {
        var col = new ColumnSchema
        {
            Name = "active", SnakeCaseName = "active",
            InferredType = PostgresType.Boolean,
        };
        var request = MakeRequest(columns: [col]);
        var risk    = new AiReviewRisk
        {
            Level       = "MEDIUM",
            Description = "active is stored as text instead of boolean",
            Evidence    = "sample rows: active=TRUE, active=FALSE",
        };

        var filtered = AiReviewClaimValidator.Apply(MakeResult(risks: [risk]), request);

        Assert.Empty(filtered.Risks);
    }

    [Fact]
    public void TextStorageRisk_NumericColumn_IsFiltered()
    {
        var col = new ColumnSchema
        {
            Name = "price", SnakeCaseName = "price",
            InferredType = PostgresType.Numeric,
        };
        var request = MakeRequest(columns: [col]);
        var risk    = new AiReviewRisk
        {
            Level       = "MEDIUM",
            Description = "price is stored as text and should be numeric",
            Evidence    = "schema: price (NUMERIC, NOT NULL); sample: price=9.99",
        };

        var filtered = AiReviewClaimValidator.Apply(MakeResult(risks: [risk]), request);

        Assert.Empty(filtered.Risks);
    }
}

public class ContradictionDetectionTests
{
    private static AiReviewRequest MakeRequest(
        IReadOnlyList<ColumnSchema>? columns = null,
        IReadOnlyList<ValidationWarning>? warnings = null) =>
        new()
        {
            Mode             = AiReviewMode.Dataset,
            TableSchema      = new TableSchema { Columns = columns ?? [] },
            SheetPreview     = new SheetPreview { Rows = [] },
            ValidationResult = new ValidationResult { Warnings = warnings ?? [] },
        };

    private static AiReviewResult MakeResult(
        IReadOnlyList<AiReviewRisk>? risks = null,
        IReadOnlyList<AiReviewRecommendation>? recs = null) =>
        new() { Summary = "Test", Risks = risks ?? [], Recommendations = recs ?? [] };

    // ── 1. Nullability recommendation contradicted by non-null evidence ────────

    [Fact]
    public void NullabilityRec_ContradictedByNonNullEvidence_IsFiltered()
    {
        // Column IS marked nullable in the schema, so NullabilityEvidenceExists passes,
        // but the evidence field itself shows only non-null values while the description
        // claims "does not contain a value" — a direct contradiction.
        var col = new ColumnSchema
        {
            Name = "active", SnakeCaseName = "active",
            InferredType = PostgresType.Boolean, IsNullable = true,
        };
        var rec = new AiReviewRecommendation
        {
            Priority    = "LOW",
            Description = "Ensure nullability intent for active column. Sample data does not contain a value.",
            Evidence    = "Sample rows: active=TRUE, active=FALSE, active=TRUE",
        };

        var filtered = AiReviewClaimValidator.Apply(MakeResult(recs: [rec]), MakeRequest(columns: [col]));

        Assert.Empty(filtered.Recommendations);
    }

    // ── 2. Boolean conversion contradicted by BOOLEAN schema ──────────────────

    [Fact]
    public void BooleanConversionRec_ContradictedByBooleanSchema_IsFiltered()
    {
        var col = new ColumnSchema
        {
            Name = "active", SnakeCaseName = "active",
            InferredType = PostgresType.Boolean,
        };
        var rec = new AiReviewRecommendation
        {
            Priority    = "MEDIUM",
            Description = "Consider boolean conversion for active column to ensure type safety.",
            Evidence    = "sample rows: active=TRUE, active=FALSE",
        };

        var filtered = AiReviewClaimValidator.Apply(MakeResult(recs: [rec]), MakeRequest(columns: [col]));

        Assert.Empty(filtered.Recommendations);
    }

    // ── 3. Type mismatch contradicted by NUMERIC schema ───────────────────────

    [Fact]
    public void TypeMismatchRisk_ContradictedByNumericSchema_IsFiltered()
    {
        var col = new ColumnSchema
        {
            Name = "score", SnakeCaseName = "score",
            InferredType = PostgresType.Numeric,
        };
        var risk = new AiReviewRisk
        {
            Level       = "MEDIUM",
            Description = "type mismatch in score column — values appear to be stored as text",
            Evidence    = "schema: score (NUMERIC, NOT NULL); sample rows: score=9.5",
        };

        var filtered = AiReviewClaimValidator.Apply(MakeResult(risks: [risk]), MakeRequest(columns: [col]));

        Assert.Empty(filtered.Risks);
    }

    // ── 4. Duplicate claim contradicted by unique evidence ────────────────────

    [Fact]
    public void DuplicateRisk_ContradictedByUniqueEvidenceValues_IsFiltered()
    {
        var col = new ColumnSchema
        {
            Name = "id", SnakeCaseName = "id", IsCandidateKey = true,
        };
        var risk = new AiReviewRisk
        {
            Level       = "HIGH",
            Description = "Duplicate risk in id column — uniqueness cannot be guaranteed",
            Evidence    = "sample rows: id=1, id=2, id=3",
        };

        var filtered = AiReviewClaimValidator.Apply(MakeResult(risks: [risk]), MakeRequest(columns: [col]));

        Assert.Empty(filtered.Risks);
    }
}

public class AiReviewGroundedFallbackTests
{
    private static AiReviewRequest MakeDatasetRequest(
        string tableName = "users",
        string sheetName = "Sheet1",
        IReadOnlyList<ColumnSchema>? columns = null) =>
        new()
        {
            Mode             = AiReviewMode.Dataset,
            TableSchema      = new TableSchema { TableName = tableName, SheetName = sheetName, Columns = columns ?? [] },
            SheetPreview     = new SheetPreview { SheetName = sheetName },
            ValidationResult = new ValidationResult(),
        };

    private static AiReviewResult EmptyResult(string summary = "vague summary") =>
        new() { Summary = summary, Risks = [], Recommendations = [] };

    // ── Trigger condition ───────────────────────────────────────────────────

    [Fact]
    public void AllFiltered_DatasetMode_ReturnsGroundedSummary()
    {
        var col     = new ColumnSchema { Name = "id", SnakeCaseName = "id", IsCandidateKey = true };
        var request = MakeDatasetRequest(tableName: "users", columns: [col]);

        var result = AiReviewGroundedFallback.Apply(EmptyResult(), request);

        Assert.Contains("users", result.Summary);
        Assert.Contains("id", result.Summary);
        Assert.Contains("recommended primary key", result.Summary);
        Assert.Contains("No evidence-backed", result.Summary);
    }

    [Fact]
    public void AllFiltered_NoUnsupportedRisksAdded()
    {
        var col     = new ColumnSchema { Name = "id", SnakeCaseName = "id", IsCandidateKey = true };
        var request = MakeDatasetRequest(columns: [col]);

        var result = AiReviewGroundedFallback.Apply(EmptyResult(), request);

        Assert.Empty(result.Risks);
    }

    [Fact]
    public void AllFiltered_PrimaryKeyRecommendationAdded()
    {
        var col     = new ColumnSchema { Name = "id", SnakeCaseName = "id", IsCandidateKey = true };
        var request = MakeDatasetRequest(columns: [col]);

        var result = AiReviewGroundedFallback.Apply(EmptyResult(), request);

        Assert.Contains(result.Recommendations, r =>
            r.Description.Contains("id") && r.Description.Contains("primary key"));
    }

    [Fact]
    public void AllFiltered_PrimaryKeyRecommendation_IsLowPriority()
    {
        var col     = new ColumnSchema { Name = "id", SnakeCaseName = "id", IsCandidateKey = true };
        var request = MakeDatasetRequest(columns: [col]);

        var result = AiReviewGroundedFallback.Apply(EmptyResult(), request);

        var rec = result.Recommendations.First(r => r.Description.Contains("primary key"));
        Assert.Equal("LOW", rec.Priority);
    }

    [Fact]
    public void AllFiltered_PrimaryKeyRecommendation_HasGroundedEvidence()
    {
        var col     = new ColumnSchema { Name = "id", SnakeCaseName = "id", IsCandidateKey = true };
        var request = MakeDatasetRequest(columns: [col]);

        var result = AiReviewGroundedFallback.Apply(EmptyResult(), request);

        var rec = result.Recommendations.First(r => r.Description.Contains("primary key"));
        Assert.False(string.IsNullOrWhiteSpace(rec.Evidence));
        Assert.Contains("id", rec.Evidence);
    }

    // ── Business identifier UNIQUE recommendation ───────────────────────────

    [Fact]
    public void AllFiltered_UsernameCandidate_AddsUniqueRecommendation()
    {
        var idCol       = new ColumnSchema { Name = "id",       SnakeCaseName = "id",       IsCandidateKey = true };
        var usernameCol = new ColumnSchema { Name = "username", SnakeCaseName = "username", IsCandidateKey = true };
        var request     = MakeDatasetRequest(columns: [idCol, usernameCol]);

        var result = AiReviewGroundedFallback.Apply(EmptyResult(), request);

        Assert.Contains(result.Recommendations, r =>
            r.Description.Contains("username") && r.Description.Contains("UNIQUE"));
    }

    [Fact]
    public void AllFiltered_EmailCandidate_AddsUniqueRecommendation()
    {
        var idCol    = new ColumnSchema { Name = "id",    SnakeCaseName = "id",    IsCandidateKey = true };
        var emailCol = new ColumnSchema { Name = "email", SnakeCaseName = "email", IsCandidateKey = true };
        var request  = MakeDatasetRequest(columns: [idCol, emailCol]);

        var result = AiReviewGroundedFallback.Apply(EmptyResult(), request);

        Assert.Contains(result.Recommendations, r =>
            r.Description.Contains("email") && r.Description.Contains("UNIQUE"));
    }

    [Fact]
    public void AllFiltered_NonIdentifierSecondCandidate_NoUniqueRecommendation()
    {
        var idCol    = new ColumnSchema { Name = "id",    SnakeCaseName = "id",    IsCandidateKey = true };
        var scoreCol = new ColumnSchema { Name = "score", SnakeCaseName = "score", IsCandidateKey = true };
        var request  = MakeDatasetRequest(columns: [idCol, scoreCol]);

        var result = AiReviewGroundedFallback.Apply(EmptyResult(), request);

        Assert.DoesNotContain(result.Recommendations, r =>
            r.Description.Contains("UNIQUE") && r.Description.Contains("score"));
    }

    // ── Multiple candidate keys in summary ──────────────────────────────────

    [Fact]
    public void AllFiltered_MultipleCandidateKeys_SummaryMentionsAdditionalKeys()
    {
        var idCol    = new ColumnSchema { Name = "id",    SnakeCaseName = "id",    IsCandidateKey = true };
        var emailCol = new ColumnSchema { Name = "email", SnakeCaseName = "email", IsCandidateKey = true };
        var request  = MakeDatasetRequest(columns: [idCol, emailCol]);

        var result = AiReviewGroundedFallback.Apply(EmptyResult(), request);

        Assert.Contains("email", result.Summary);
        Assert.Contains("candidate key", result.Summary);
    }

    // ── Guard conditions ────────────────────────────────────────────────────

    [Fact]
    public void MigrationMode_FallbackNotApplied()
    {
        var col     = new ColumnSchema { Name = "id", SnakeCaseName = "id", IsCandidateKey = true };
        var request = new AiReviewRequest
        {
            Mode        = AiReviewMode.Migration,
            TableSchema = new TableSchema { Columns = [col] },
        };
        var original = EmptyResult("The dataset 'users' contains potential issues.");

        var result = AiReviewGroundedFallback.Apply(original, request);

        Assert.Same(original, result);
    }

    [Fact]
    public void RemainingRisk_FallbackNotApplied()
    {
        var col     = new ColumnSchema { Name = "id", SnakeCaseName = "id", IsCandidateKey = true };
        var request = MakeDatasetRequest(columns: [col]);
        var remaining = new AiReviewResult
        {
            Summary         = "original AI summary",
            Risks           = [new AiReviewRisk { Level = "HIGH", Description = "Risk A", Evidence = "schema: X" }],
            Recommendations = [],
        };

        var result = AiReviewGroundedFallback.Apply(remaining, request);

        Assert.Same(remaining, result);
    }

    [Fact]
    public void RemainingRecommendation_FallbackNotApplied()
    {
        var col     = new ColumnSchema { Name = "id", SnakeCaseName = "id", IsCandidateKey = true };
        var request = MakeDatasetRequest(columns: [col]);
        var remaining = new AiReviewResult
        {
            Summary         = "original AI summary",
            Risks           = [],
            Recommendations = [new AiReviewRecommendation { Priority = "LOW", Description = "Rec A", Evidence = "schema: X" }],
        };

        var result = AiReviewGroundedFallback.Apply(remaining, request);

        Assert.Same(remaining, result);
    }

    [Fact]
    public void NoCandidateKeys_NoRecommendationsAdded()
    {
        var col     = new ColumnSchema { Name = "name", SnakeCaseName = "name", IsCandidateKey = false };
        var request = MakeDatasetRequest(columns: [col]);

        var result = AiReviewGroundedFallback.Apply(EmptyResult(), request);

        Assert.Empty(result.Recommendations);
    }
}

public class ClassifyClaimTests
{
    [Theory]
    [InlineData("incorrect type inference in score column")]
    [InlineData("inconsistent data types found in price column")]
    [InlineData("type mismatch detected in amount column")]
    [InlineData("score stored as text")]
    [InlineData("wrong type detected for column age")]
    [InlineData("value was inferred incorrectly")]
    [InlineData("type conversion issue in column total")]
    [InlineData("incorrect type assigned to column")]
    [InlineData("data type inconsistency in rating column")]
    [InlineData("active column requires boolean conversion")]
    public void ClassifyClaim_TypeInferencePhrase_ReturnsTypeInference(string description)
    {
        Assert.Equal(RiskCategory.TypeInference, AiReviewClaimValidator.ClassifyClaim(description, null));
    }

    [Theory]
    [InlineData("score uses comma decimal formatting")]
    [InlineData("culture-specific decimal separator detected in amount")]
    [InlineData("locale formatting inconsistency found")]
    [InlineData("decimal formatting varies across rows")]
    [InlineData("comma versus dot separator in price column")]
    public void ClassifyClaim_FormattingPhrase_ReturnsFormatting(string description)
    {
        Assert.Equal(RiskCategory.Formatting, AiReviewClaimValidator.ClassifyClaim(description, null));
    }

    [Fact]
    public void ClassifyClaim_NullableRisk_ReturnsNullability()
    {
        Assert.Equal(RiskCategory.Nullability,
            AiReviewClaimValidator.ClassifyClaim("nullable risk in column email", null));
    }

    [Fact]
    public void ClassifyClaim_DuplicateRisk_ReturnsDuplicate()
    {
        Assert.Equal(RiskCategory.Duplicate,
            AiReviewClaimValidator.ClassifyClaim("duplicate values in id column", null));
    }

    [Fact]
    public void ClassifyClaim_MixedTypeInferenceAndFormatting_ReturnsTypeInference()
    {
        // TypeInference takes priority when both signals are present
        Assert.Equal(RiskCategory.TypeInference,
            AiReviewClaimValidator.ClassifyClaim(
                "incorrect type inference — values use comma decimal format", null));
    }

    [Fact]
    public void ClassifyClaim_UnrecognisedClaim_ReturnsOther()
    {
        Assert.Equal(RiskCategory.Other,
            AiReviewClaimValidator.ClassifyClaim("some general dataset remark", null));
    }

    [Fact]
    public void ClassifyClaim_EvidenceContainsPhrase_ClassifiesFromEvidence()
    {
        Assert.Equal(RiskCategory.TypeInference,
            AiReviewClaimValidator.ClassifyClaim(
                "review score column",
                "schema shows stored as text but inferred as NUMERIC"));
    }
}

public class RiskCategoryClaimValidatorTests
{
    private static AiReviewRequest MakeRequest(
        IReadOnlyList<ColumnSchema>? columns = null,
        IReadOnlyList<IReadOnlyDictionary<string, string?>>? rows = null,
        IReadOnlyList<ValidationWarning>? warnings = null) =>
        new()
        {
            Mode             = AiReviewMode.Dataset,
            TableSchema      = new TableSchema { Columns = columns ?? [] },
            SheetPreview     = new SheetPreview { Rows = rows ?? [] },
            ValidationResult = new ValidationResult { Warnings = warnings ?? [] },
        };

    private static AiReviewResult MakeResult(
        IReadOnlyList<AiReviewRisk>? risks = null,
        IReadOnlyList<AiReviewRecommendation>? recs = null) =>
        new()
        {
            Summary         = "Test",
            Risks           = risks ?? [],
            Recommendations = recs  ?? [],
        };

    [Fact]
    public void InconsistentDataTypes_NumericColumn_IsFiltered()
    {
        var col = new ColumnSchema
        {
            Name = "score", SnakeCaseName = "score",
            InferredType = PostgresType.Numeric,
        };
        var request = MakeRequest(columns: [col]);
        var risk    = new AiReviewRisk
        {
            Level       = "MEDIUM",
            Description = "score column has inconsistent data types",
            Evidence    = "schema: score (NUMERIC, NOT NULL); sample rows include mixed values",
        };

        var filtered = AiReviewClaimValidator.Apply(MakeResult(risks: [risk]), request);

        Assert.Empty(filtered.Risks);
    }

    [Fact]
    public void TypeMismatch_NumericColumn_IsFiltered()
    {
        var col = new ColumnSchema
        {
            Name = "score", SnakeCaseName = "score",
            InferredType = PostgresType.Numeric,
        };
        var request = MakeRequest(columns: [col]);
        var risk    = new AiReviewRisk
        {
            Level       = "MEDIUM",
            Description = "type mismatch in score column",
            Evidence    = "schema: score (NUMERIC, NOT NULL)",
        };

        var filtered = AiReviewClaimValidator.Apply(MakeResult(risks: [risk]), request);

        Assert.Empty(filtered.Risks);
    }

    [Fact]
    public void StoredAsText_NumericColumn_IsFiltered()
    {
        var col = new ColumnSchema
        {
            Name = "price", SnakeCaseName = "price",
            InferredType = PostgresType.Numeric,
        };
        var request = MakeRequest(columns: [col]);
        var risk    = new AiReviewRisk
        {
            Level       = "HIGH",
            Description = "price is stored as text",
            Evidence    = "schema: price (NUMERIC, NOT NULL)",
        };

        var filtered = AiReviewClaimValidator.Apply(MakeResult(risks: [risk]), request);

        Assert.Empty(filtered.Risks);
    }

    [Fact]
    public void IncorrectTypeInference_BooleanColumn_IsFiltered()
    {
        var col = new ColumnSchema
        {
            Name = "active", SnakeCaseName = "active",
            InferredType = PostgresType.Boolean,
        };
        var request = MakeRequest(columns: [col]);
        var risk    = new AiReviewRisk
        {
            Level       = "MEDIUM",
            Description = "active column: incorrect type inference detected",
            Evidence    = "schema: active (BOOLEAN, NOT NULL)",
        };

        var filtered = AiReviewClaimValidator.Apply(MakeResult(risks: [risk]), request);

        Assert.Empty(filtered.Risks);
    }

    [Fact]
    public void FormattingOnly_CommaDecimal_IsPreserved()
    {
        var col = new ColumnSchema
        {
            Name = "score", SnakeCaseName = "score",
            InferredType = PostgresType.Numeric,
        };
        var request = MakeRequest(columns: [col]);
        var risk    = new AiReviewRisk
        {
            Level       = "LOW",
            Description = "score uses comma decimal notation — normalize before insert",
            Evidence    = "sample rows: score=9,5",
        };

        var filtered = AiReviewClaimValidator.Apply(MakeResult(risks: [risk]), request);

        Assert.Single(filtered.Risks);
    }

    [Fact]
    public void Mixed_FormattingAndTypeInference_TypeInferenceWins_IsFiltered()
    {
        var col = new ColumnSchema
        {
            Name = "score", SnakeCaseName = "score",
            InferredType = PostgresType.Numeric,
        };
        var request = MakeRequest(columns: [col]);
        var risk    = new AiReviewRisk
        {
            Level       = "MEDIUM",
            Description = "score column: incorrect type inference — values use comma decimal formatting",
            Evidence    = "sample rows: score=9,5",
        };

        var filtered = AiReviewClaimValidator.Apply(MakeResult(risks: [risk]), request);

        Assert.Empty(filtered.Risks);
    }

    [Fact]
    public void MigrationMode_TypeInferenceClaim_IsNotFiltered()
    {
        var col = new ColumnSchema
        {
            Name = "score", SnakeCaseName = "score",
            InferredType = PostgresType.Numeric,
        };
        var request = new AiReviewRequest
        {
            Mode        = AiReviewMode.Migration,
            TableSchema = new TableSchema { Columns = [col] },
        };
        var risk = new AiReviewRisk
        {
            Level       = "MEDIUM",
            Description = "score has incorrect type inference",
            Evidence    = "schema: score (NUMERIC)",
        };

        var filtered = AiReviewClaimValidator.Apply(MakeResult(risks: [risk]), request);

        Assert.Single(filtered.Risks);
    }
}

public class DataAnalysisAuthorityTests
{
    // ── Helpers ─────────────────────────────────────────────────────────────

    private static DataAnalysisResult MakeAnalysis(bool includeScoreDuplicateRisk = false)
    {
        var findings = new List<DataAnalysisFinding>
        {
            new() { Category = "CandidateKey", Severity = "INFO",
                    Description = "'id' is a strong primary key candidate",
                    Detail      = "Type: INTEGER. Non-nullable with all unique values; naming convention confirms key intent." },
            new() { Category = "CandidateKey", Severity = "INFO",
                    Description = "'score' is sample-unique but not recommended as a key",
                    Detail      = "It is not recommended because it is a numeric value column." },
        };

        var risks = new List<DataAnalysisFinding>();
        if (includeScoreDuplicateRisk)
        {
            risks.Add(new DataAnalysisFinding
            {
                Category    = "DuplicateRisk",
                Severity    = "WARNING",
                Description = "'score' contains duplicate values",
                Detail      = "Type: NUMERIC.",
            });
        }

        return new DataAnalysisResult
        {
            Summary         = "This dataset has 10 rows and 2 columns.",
            Findings        = findings,
            Risks           = risks,
            Recommendations =
            [
                new DataAnalysisRecommendation
                {
                    Priority    = "HIGH",
                    Type        = "PrimaryKey",
                    Description = "Designate 'id' as the PRIMARY KEY — it is the strongest candidate key by type and naming convention.",
                },
            ],
        };
    }

    private static IReadOnlyList<ColumnSchema> DefaultColumns() =>
    [
        new() { Name = "id",    SnakeCaseName = "id",    InferredType = PostgresType.Integer,
                IsCandidateKey = true,  CandidateKeyQuality = CandidateKeyQuality.Strong },
        new() { Name = "score", SnakeCaseName = "score", InferredType = PostgresType.Numeric,
                IsCandidateKey = true,  CandidateKeyQuality = CandidateKeyQuality.None },
    ];

    private static AiReviewRequest MakeRequest(
        DataAnalysisResult? analysis,
        IReadOnlyList<ColumnSchema>? columns = null) =>
        new()
        {
            Mode               = AiReviewMode.Dataset,
            TableSchema        = new TableSchema { Columns = columns ?? DefaultColumns() },
            SheetPreview       = new SheetPreview { Rows = [] },
            ValidationResult   = new ValidationResult(),
            DataAnalysisResult = analysis,
        };

    private static AiReviewResult MakeResult(
        IReadOnlyList<AiReviewRisk>? risks = null,
        IReadOnlyList<AiReviewRecommendation>? recs = null) =>
        new() { Summary = "Test", Risks = risks ?? [], Recommendations = recs ?? [] };

    // ── Test 1: DA says score is numeric value column → HIGH type inference risk filtered ─────────
    // This exercises IsContradictsDataAnalysis Rule 2 (numeric value column path).
    // The description "type inference risk" is NOT in TypeInferencePhrases so the existing
    // IsTypeInferenceClaimValid path does not catch it — the new DA authority check must.

    [Fact]
    public void DataAnalysis_NumericValueColumn_HighTypeInferenceRisk_IsFiltered()
    {
        var request = MakeRequest(MakeAnalysis());
        var risk = new AiReviewRisk
        {
            Level       = "HIGH",
            Description = "score has type inference risk due to ambiguous format",
            Evidence    = "schema: score (NUMERIC, NOT NULL)",
        };

        var filtered = AiReviewClaimValidator.Apply(MakeResult(risks: [risk]), request);

        Assert.Empty(filtered.Risks);
    }

    // ── Test 2: DA says no duplicate risk → AI duplicate risk filtered ────────────────────────────
    // score is NOT a candidate key here, so IsDuplicateClaimValid returns true (allow) — the
    // existing validator does not filter this. The new DA authority check must.

    [Fact]
    public void DataAnalysis_NoDuplicateRisk_AiDuplicateRiskOnNonCandidateKey_IsFiltered()
    {
        var columns = new List<ColumnSchema>
        {
            new() { Name = "id",    SnakeCaseName = "id",    InferredType = PostgresType.Integer,
                    IsCandidateKey = true,  CandidateKeyQuality = CandidateKeyQuality.Strong },
            new() { Name = "score", SnakeCaseName = "score", InferredType = PostgresType.Numeric,
                    IsCandidateKey = false, HasDuplicates = false },
        };
        var request = MakeRequest(MakeAnalysis(includeScoreDuplicateRisk: false), columns);
        var risk = new AiReviewRisk
        {
            Level       = "MEDIUM",
            Description = "Duplicate values detected in score column",
            Evidence    = "schema: score (NUMERIC, NOT NULL)",
        };

        var filtered = AiReviewClaimValidator.Apply(MakeResult(risks: [risk]), request);

        Assert.Empty(filtered.Risks);
    }

    // ── Test 3: DA says id is recommended PK → AI cannot recommend score as PK ──────────────────
    // The existing validator classifies "Use score as the primary key" as Other → passes through.
    // The new DA authority check (Rule 5) must filter it.

    [Fact]
    public void DataAnalysis_RecommendsPkId_AiRecommendScoreAsPk_IsFiltered()
    {
        var request = MakeRequest(MakeAnalysis());
        var rec = new AiReviewRecommendation
        {
            Priority    = "HIGH",
            Description = "Use score as the primary key — it appears unique in the sample",
            Evidence    = "schema: score (NUMERIC, NOT NULL, candidate key)",
        };

        var filtered = AiReviewClaimValidator.Apply(MakeResult(recs: [rec]), request);

        Assert.Empty(filtered.Recommendations);
    }

    // ── Test 4: AI may still add new evidence-backed risk not covered by DA ──────────────────────
    // DA ran and covers id and score. AI adds a risk about a 'notes' column (nullable) that DA
    // did not mention. This is a new, evidence-backed risk → must NOT be filtered.

    [Fact]
    public void DataAnalysis_Ran_AiAddsNewEvidenceBackedRiskForUncoveredColumn_IsKept()
    {
        var columns = new List<ColumnSchema>
        {
            new() { Name = "id",    SnakeCaseName = "id",    InferredType = PostgresType.Integer,
                    IsCandidateKey = true },
            new() { Name = "score", SnakeCaseName = "score", InferredType = PostgresType.Numeric },
            new() { Name = "notes", SnakeCaseName = "notes", InferredType = PostgresType.Text,
                    IsNullable = true },
        };
        var request = MakeRequest(MakeAnalysis(), columns);
        var risk = new AiReviewRisk
        {
            Level       = "LOW",
            Description = "notes column is nullable — confirm whether NULL values are expected",
            Evidence    = "schema: notes (TEXT, NULL)",
        };

        var filtered = AiReviewClaimValidator.Apply(MakeResult(risks: [risk]), request);

        Assert.Single(filtered.Risks);
    }

    // ── Prompt section tests ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void DatasetSystemPrompt_StatesDataAnalysisIsAuthoritative()
    {
        Assert.Contains(
            "Data Analysis is the authoritative source.",
            AiReviewPromptBuilder.DatasetSystemPrompt);
    }

    [Fact]
    public void DatasetSystemPrompt_ForbidsTypeInferenceRiskForNumericValueColumn()
    {
        Assert.Contains(
            "If Data Analysis says a column is a numeric value column, do not raise type inference risk for that column.",
            AiReviewPromptBuilder.DatasetSystemPrompt);
    }

    [Fact]
    public void DatasetSystemPrompt_ForbidsKeyRecommendationForNotRecommendedColumn()
    {
        Assert.Contains(
            "If Data Analysis says a column is not recommended as a key, do not recommend it as a primary key or unique constraint.",
            AiReviewPromptBuilder.DatasetSystemPrompt);
    }

    [Fact]
    public void DatasetSystemPrompt_ForbidsDifferentPkWhenDataAnalysisRecommendsOne()
    {
        Assert.Contains(
            "If Data Analysis recommends a specific column as the primary key, do not recommend a different column as primary key.",
            AiReviewPromptBuilder.DatasetSystemPrompt);
    }

    [Fact]
    public void DatasetSystemPrompt_ForbidsDuplicateRiskWhenDataAnalysisFoundNone()
    {
        Assert.Contains(
            "If Data Analysis found no duplicate risk for a column, do not report duplicate risk for that column.",
            AiReviewPromptBuilder.DatasetSystemPrompt);
    }

    [Fact]
    public void DatasetSystemPrompt_AllowsNewEvidenceBackedRiskNotCoveredByDataAnalysis()
    {
        Assert.Contains(
            "AI Review may only introduce a new risk if: (1) it is not addressed by Data Analysis AND (2) it has direct evidence",
            AiReviewPromptBuilder.DatasetSystemPrompt);
    }

    [Fact]
    public void BuildDatasetUserMessage_WithAnalysis_IncludesDataAnalysisAuthorityBlock()
    {
        var analysis = MakeAnalysis();
        var request  = new AiReviewRequest
        {
            Mode               = AiReviewMode.Dataset,
            DataAnalysisResult = analysis,
        };

        var message = AiReviewPromptBuilder.BuildUserMessage(request);

        Assert.Contains("<data_analysis_authority>", message);
        Assert.Contains("DETERMINISTIC and AUTHORITATIVE", message);
        Assert.Contains(analysis.Summary, message);
    }

    [Fact]
    public void BuildDatasetUserMessage_WithAnalysis_ListsFindings()
    {
        var analysis = MakeAnalysis();
        var request  = new AiReviewRequest
        {
            Mode               = AiReviewMode.Dataset,
            DataAnalysisResult = analysis,
        };

        var message = AiReviewPromptBuilder.BuildUserMessage(request);

        Assert.Contains("'score' is sample-unique but not recommended as a key", message);
        Assert.Contains("numeric value column", message);
    }

    [Fact]
    public void BuildDatasetUserMessage_WithAnalysis_ListsRecommendations()
    {
        var analysis = MakeAnalysis();
        var request  = new AiReviewRequest
        {
            Mode               = AiReviewMode.Dataset,
            DataAnalysisResult = analysis,
        };

        var message = AiReviewPromptBuilder.BuildUserMessage(request);

        Assert.Contains("PrimaryKey", message);
        Assert.Contains("Designate 'id' as the PRIMARY KEY", message);
    }

    [Fact]
    public void BuildDatasetUserMessage_WithAnalysis_NoRisks_ShowsNoneMessage()
    {
        var analysis = MakeAnalysis(includeScoreDuplicateRisk: false);
        var request  = new AiReviewRequest
        {
            Mode               = AiReviewMode.Dataset,
            DataAnalysisResult = analysis,
        };

        var message = AiReviewPromptBuilder.BuildUserMessage(request);

        Assert.Contains("Data Analysis found no duplicate or nullability risks", message);
    }

    [Fact]
    public void BuildDatasetUserMessage_WithoutAnalysis_OmitsDataAnalysisAuthorityBlock()
    {
        var request = new AiReviewRequest
        {
            Mode               = AiReviewMode.Dataset,
            DataAnalysisResult = null,
        };

        var message = AiReviewPromptBuilder.BuildUserMessage(request);

        Assert.DoesNotContain("<data_analysis_authority>", message);
    }

    // ── DA absent → no additional filtering ──────────────────────────────────────────────────────

    [Fact]
    public void DataAnalysisAbsent_DuplicateRisk_NotFilteredByDataAnalysisPath()
    {
        // Without DA, the existing IsDuplicateClaimValid for a candidate key with actual duplicates keeps the risk.
        var columns = new List<ColumnSchema>
        {
            new() { Name = "id", SnakeCaseName = "id", InferredType = PostgresType.Integer,
                    IsCandidateKey = true },
        };
        var rows = new List<IReadOnlyDictionary<string, string?>>
        {
            new Dictionary<string, string?> { ["id"] = "1" },
            new Dictionary<string, string?> { ["id"] = "1" },
        };
        var request = new AiReviewRequest
        {
            Mode               = AiReviewMode.Dataset,
            TableSchema        = new TableSchema { Columns = columns },
            SheetPreview       = new SheetPreview { Rows = rows },
            ValidationResult   = new ValidationResult(),
            DataAnalysisResult = null,
        };
        var risk = new AiReviewRisk
        {
            Level       = "HIGH",
            Description = "Duplicate risk in id column",
            Evidence    = "Row 1 and Row 2 both have id=1",
        };

        var filtered = AiReviewClaimValidator.Apply(MakeResult(risks: [risk]), request);

        Assert.Single(filtered.Risks);
    }

    // ── DA present but column not covered → new risk is kept ────────────────────────────────────

    [Fact]
    public void DataAnalysis_DuplicateRiskPresent_AiDuplicateRisk_IsKept()
    {
        // When DA explicitly HAS a DuplicateRisk for score, AI claim about score duplicates is kept
        var columns = new List<ColumnSchema>
        {
            new() { Name = "id",    SnakeCaseName = "id",    InferredType = PostgresType.Integer,
                    IsCandidateKey = true },
            new() { Name = "score", SnakeCaseName = "score", InferredType = PostgresType.Numeric,
                    IsCandidateKey = false, HasDuplicates = true },
        };
        var request = MakeRequest(MakeAnalysis(includeScoreDuplicateRisk: true), columns);
        var risk = new AiReviewRisk
        {
            Level       = "MEDIUM",
            Description = "Duplicate values detected in score column",
            Evidence    = "schema: score (NUMERIC, NOT NULL); score has duplicate values",
        };

        var filtered = AiReviewClaimValidator.Apply(MakeResult(risks: [risk]), request);

        Assert.Single(filtered.Risks);
    }
}
