using DataMigrationAssistant.Core.Models;
using DataMigrationAssistant.Core.Services;

namespace DataMigrationAssistant.Core.Tests;

// ── Null service ──────────────────────────────────────────────────────────────

public class NullDataAnalysisServiceTests
{
    private readonly IDataAnalysisService _sut = new NullDataAnalysisService();

    [Fact]
    public async Task AnalyzeAsync_ReturnEmptySummary()
    {
        var result = await _sut.AnalyzeAsync(new DataAnalysisRequest());
        Assert.Equal(string.Empty, result.Summary);
    }

    [Fact]
    public async Task AnalyzeAsync_ReturnEmptyFindings()
    {
        var result = await _sut.AnalyzeAsync(new DataAnalysisRequest());
        Assert.Empty(result.Findings);
    }

    [Fact]
    public async Task AnalyzeAsync_ReturnEmptyRisks()
    {
        var result = await _sut.AnalyzeAsync(new DataAnalysisRequest());
        Assert.Empty(result.Risks);
    }

    [Fact]
    public async Task AnalyzeAsync_ReturnEmptyRecommendations()
    {
        var result = await _sut.AnalyzeAsync(new DataAnalysisRequest());
        Assert.Empty(result.Recommendations);
    }
}

// ── Claude service — API key guard ────────────────────────────────────────────

public class ClaudeDataAnalysisServiceTests
{
    [Fact]
    public async Task AnalyzeAsync_NullApiKey_Throws()
    {
        var sut = new ClaudeDataAnalysisService(null);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.AnalyzeAsync(new DataAnalysisRequest()));
    }

    [Fact]
    public async Task AnalyzeAsync_EmptyApiKey_Throws()
    {
        var sut = new ClaudeDataAnalysisService(string.Empty);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.AnalyzeAsync(new DataAnalysisRequest()));
    }

    [Fact]
    public async Task AnalyzeAsync_WhitespaceApiKey_Throws()
    {
        var sut = new ClaudeDataAnalysisService("   ");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.AnalyzeAsync(new DataAnalysisRequest()));
    }
}

// ── Factory ───────────────────────────────────────────────────────────────────

public class DataAnalysisServiceFactoryTests
{
    private static DataAnalysisServiceFactory MakeFactory() =>
        new(new DeterministicDataAnalysisService(),
            new ClaudeDataAnalysisService(null),
            new OllamaDataAnalysisService(new HttpClient()));

    [Fact]
    public void Create_Deterministic_ReturnsDeterministicService()
    {
        var result = MakeFactory().Create("deterministic");
        Assert.IsType<DeterministicDataAnalysisService>(result);
    }

    [Fact]
    public void Create_Claude_ReturnsClaudeService()
    {
        var result = MakeFactory().Create("claude");
        Assert.IsType<ClaudeDataAnalysisService>(result);
    }

    [Fact]
    public void Create_Ollama_ReturnsOllamaService()
    {
        var result = MakeFactory().Create("ollama");
        Assert.IsType<OllamaDataAnalysisService>(result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown")]
    public void Create_UnknownProvider_ReturnsDeterministicService(string? provider)
    {
        var result = MakeFactory().Create(provider);
        Assert.IsType<DeterministicDataAnalysisService>(result);
    }
}

// ── Deterministic service — summary ──────────────────────────────────────────

public class DeterministicDataAnalysisSummaryTests
{
    private static TableSchema MakeSchema(string tableName, params ColumnSchema[] columns) =>
        new() { TableName = tableName, Columns = columns };

    private static SheetPreview MakePreview(int totalRowCount) =>
        new() { TotalRowCount = totalRowCount };

    [Fact]
    public void BuildSummary_IncludesRowAndColumnCount()
    {
        var request = new DataAnalysisRequest
        {
            TableSchema  = MakeSchema("employees", new ColumnSchema { SnakeCaseName = "id" }),
            SheetPreview = MakePreview(100),
        };
        var summary = DeterministicDataAnalysisService.BuildSummary(request);
        Assert.Contains("100", summary);
        Assert.Contains("1", summary);
    }

    [Fact]
    public void BuildSummary_IncludesHumanReadableTableName()
    {
        var request = new DataAnalysisRequest
        {
            TableSchema  = MakeSchema("employee_records"),
            SheetPreview = MakePreview(10),
        };
        var summary = DeterministicDataAnalysisService.BuildSummary(request);
        Assert.Contains("employee records", summary);
    }

    [Fact]
    public void BuildSummary_WithCandidateKey_MentionsIt()
    {
        var request = new DataAnalysisRequest
        {
            TableSchema  = MakeSchema("items",
                new ColumnSchema { SnakeCaseName = "id", IsCandidateKey = true }),
            SheetPreview = MakePreview(5),
        };
        var summary = DeterministicDataAnalysisService.BuildSummary(request);
        Assert.Contains("id", summary);
        Assert.Contains("primary key", summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildSummary_WithNoCandidateKey_MentionsSurrogate()
    {
        var request = new DataAnalysisRequest
        {
            TableSchema  = MakeSchema("items",
                new ColumnSchema { SnakeCaseName = "name", IsNullable = true }),
            SheetPreview = MakePreview(5),
        };
        var summary = DeterministicDataAnalysisService.BuildSummary(request);
        Assert.Contains("surrogate", summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildSummary_WithMultipleCandidateKeys_MentionsCount()
    {
        var request = new DataAnalysisRequest
        {
            TableSchema  = MakeSchema("items",
                new ColumnSchema { SnakeCaseName = "id",   IsCandidateKey = true },
                new ColumnSchema { SnakeCaseName = "code", IsCandidateKey = true }),
            SheetPreview = MakePreview(5),
        };
        var summary = DeterministicDataAnalysisService.BuildSummary(request);
        Assert.Contains("2", summary);
    }

    [Fact]
    public void BuildSummary_WithLookupColumns_MentionsLookupCount()
    {
        var request = new DataAnalysisRequest
        {
            TableSchema  = MakeSchema("orders",
                new ColumnSchema { SnakeCaseName = "status_type" },
                new ColumnSchema { SnakeCaseName = "order_status" }),
            SheetPreview = MakePreview(3),
        };
        var summary = DeterministicDataAnalysisService.BuildSummary(request);
        Assert.Contains("lookup", summary, StringComparison.OrdinalIgnoreCase);
    }
}

// ── Deterministic service — findings ─────────────────────────────────────────

public class DeterministicDataAnalysisFindingsTests
{
    [Fact]
    public void BuildFindings_WithCandidateKey_NoPerColumnInfoFinding()
    {
        var request = new DataAnalysisRequest
        {
            TableSchema = new TableSchema
            {
                Columns = [new ColumnSchema { SnakeCaseName = "id", IsCandidateKey = true, InferredType = PostgresType.Integer, CandidateKeyQuality = CandidateKeyQuality.Strong }],
            },
        };
        var findings = DeterministicDataAnalysisService.BuildFindings(request);
        Assert.DoesNotContain(findings, f => f.Category == "CandidateKey" && f.Severity == "INFO");
    }

    [Fact]
    public void BuildFindings_NoCandidateKey_ProducesWarningFinding()
    {
        var request = new DataAnalysisRequest
        {
            TableSchema = new TableSchema
            {
                Columns = [new ColumnSchema { SnakeCaseName = "name", IsNullable = true }],
            },
        };
        var findings = DeterministicDataAnalysisService.BuildFindings(request);
        Assert.Contains(findings, f => f.Category == "CandidateKey" && f.Severity == "WARNING");
    }

    [Fact]
    public void BuildFindings_ValidationWarning_ProducesDataQualityFinding()
    {
        var request = new DataAnalysisRequest
        {
            TableSchema = new TableSchema(),
            ValidationResult = new ValidationResult
            {
                Warnings = [new ValidationWarning { Message = "Too many nulls", Severity = ValidationSeverity.Warning }],
            },
        };
        var findings = DeterministicDataAnalysisService.BuildFindings(request);
        Assert.Contains(findings, f => f.Category == "DataQuality" && f.Description.Contains("Too many nulls"));
    }

    [Fact]
    public void BuildFindings_LookupCandidate_ProducesNormalizationOpportunity()
    {
        var request = new DataAnalysisRequest
        {
            TableSchema = new TableSchema
            {
                Columns = [new ColumnSchema { SnakeCaseName = "element_type" }],
            },
        };
        var findings = DeterministicDataAnalysisService.BuildFindings(request);
        Assert.Contains(findings, f => f.Category == "NormalizationOpportunity"
                                    && f.Description.Contains("element_type"));
    }

    [Fact]
    public void BuildFindings_FkLikeColumn_ProducesNormalizationOpportunity()
    {
        var request = new DataAnalysisRequest
        {
            TableSchema = new TableSchema
            {
                Columns = [new ColumnSchema { SnakeCaseName = "scenario_id" }],
            },
        };
        var findings = DeterministicDataAnalysisService.BuildFindings(request);
        Assert.Contains(findings, f => f.Category == "NormalizationOpportunity"
                                    && f.Description.Contains("scenario_id"));
    }

    [Fact]
    public void BuildFindings_PrefixGroup_ProducesNormalizationOpportunity()
    {
        var request = new DataAnalysisRequest
        {
            TableSchema = new TableSchema
            {
                Columns =
                [
                    new ColumnSchema { SnakeCaseName = "emp_name" },
                    new ColumnSchema { SnakeCaseName = "emp_code" },
                    new ColumnSchema { SnakeCaseName = "emp_level" },
                ],
            },
        };
        var findings = DeterministicDataAnalysisService.BuildFindings(request);
        Assert.Contains(findings, f => f.Category == "NormalizationOpportunity"
                                    && f.Description.Contains("emp"));
    }
}

// ── Deterministic service — risks ─────────────────────────────────────────────

public class DeterministicDataAnalysisRisksTests
{
    [Fact]
    public void BuildRisks_ColumnWithDuplicates_ProducesDuplicateRisk()
    {
        var request = new DataAnalysisRequest
        {
            TableSchema = new TableSchema
            {
                Columns =
                [
                    new ColumnSchema
                    {
                        SnakeCaseName = "name",
                        InferredType  = PostgresType.Text,   // must be explicit — default is Boolean (0)
                        HasDuplicates = true,
                        IsCandidateKey = false,
                    },
                ],
            },
        };
        var risks = DeterministicDataAnalysisService.BuildRisks(request);
        Assert.Contains(risks, r => r.Category == "DuplicateRisk" && r.Description.Contains("name"));
    }

    [Fact]
    public void BuildRisks_CandidateKeyWithDuplicates_NoDuplicateRisk()
    {
        var request = new DataAnalysisRequest
        {
            TableSchema = new TableSchema
            {
                Columns = [new ColumnSchema { SnakeCaseName = "id", HasDuplicates = true, IsCandidateKey = true }],
            },
        };
        var risks = DeterministicDataAnalysisService.BuildRisks(request);
        Assert.DoesNotContain(risks, r => r.Category == "DuplicateRisk");
    }

    [Fact]
    public void BuildRisks_NullableImportantColumn_ProducesNullableRisk()
    {
        var request = new DataAnalysisRequest
        {
            TableSchema = new TableSchema
            {
                Columns = [new ColumnSchema { SnakeCaseName = "user_id", IsNullable = true }],
            },
        };
        var risks = DeterministicDataAnalysisService.BuildRisks(request);
        Assert.Contains(risks, r => r.Category == "NullableRisk" && r.Description.Contains("user_id"));
    }

    [Fact]
    public void BuildRisks_NullableUnimportantColumn_NoNullableRisk()
    {
        var request = new DataAnalysisRequest
        {
            TableSchema = new TableSchema
            {
                Columns = [new ColumnSchema { SnakeCaseName = "notes", IsNullable = true }],
            },
        };
        var risks = DeterministicDataAnalysisService.BuildRisks(request);
        Assert.DoesNotContain(risks, r => r.Category == "NullableRisk");
    }

    [Fact]
    public void BuildRisks_NullableNameColumn_ProducesNullableRisk()
    {
        var request = new DataAnalysisRequest
        {
            TableSchema = new TableSchema
            {
                Columns = [new ColumnSchema { SnakeCaseName = "name", IsNullable = true }],
            },
        };
        var risks = DeterministicDataAnalysisService.BuildRisks(request);
        Assert.Contains(risks, r => r.Category == "NullableRisk");
    }
}

// ── Deterministic service — recommendations ───────────────────────────────────

public class DeterministicDataAnalysisRecommendationsTests
{
    [Fact]
    public void BuildRecommendations_SingleCandidateKey_HighPriorityPkRec()
    {
        var request = new DataAnalysisRequest
        {
            TableSchema = new TableSchema
            {
                Columns = [new ColumnSchema
                {
                    SnakeCaseName       = "id",
                    IsCandidateKey      = true,
                    CandidateKeyQuality = CandidateKeyQuality.Strong,
                }],
            },
        };
        var recs = DeterministicDataAnalysisService.BuildRecommendations(request);
        Assert.Contains(recs, r => r.Type == "PrimaryKey" && r.Priority == "HIGH"
                                && r.Description.Contains("id"));
    }

    [Fact]
    public void BuildRecommendations_NoCandidateKey_SuggestsSurrogate()
    {
        var request = new DataAnalysisRequest
        {
            TableSchema = new TableSchema
            {
                Columns = [new ColumnSchema { SnakeCaseName = "notes", IsNullable = true }],
            },
        };
        var recs = DeterministicDataAnalysisService.BuildRecommendations(request);
        Assert.Contains(recs, r => r.Type == "PrimaryKey" && r.Priority == "HIGH"
                                && r.Description.Contains("surrogate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildRecommendations_MultipleCandidateKeys_UniqueConstraintForExtras()
    {
        var request = new DataAnalysisRequest
        {
            TableSchema = new TableSchema
            {
                Columns =
                [
                    new ColumnSchema
                    {
                        SnakeCaseName       = "id",
                        IsCandidateKey      = true,
                        CandidateKeyQuality = CandidateKeyQuality.Strong,
                    },
                    new ColumnSchema
                    {
                        SnakeCaseName       = "code",
                        IsCandidateKey      = true,
                        CandidateKeyQuality = CandidateKeyQuality.Strong,
                    },
                ],
            },
        };
        var recs = DeterministicDataAnalysisService.BuildRecommendations(request);
        Assert.Contains(recs, r => r.Type == "UniqueConstraint" && r.Description.Contains("code"));
    }

    [Fact]
    public void BuildRecommendations_FkLikeColumn_SuggestsIndex()
    {
        var request = new DataAnalysisRequest
        {
            TableSchema = new TableSchema
            {
                Columns = [new ColumnSchema { SnakeCaseName = "scenario_id", IsCandidateKey = false }],
            },
        };
        var recs = DeterministicDataAnalysisService.BuildRecommendations(request);
        Assert.Contains(recs, r => r.Type == "Index" && r.Description.Contains("scenario_id"));
    }

    [Fact]
    public void BuildRecommendations_LookupCandidates_SuggestsNormalization()
    {
        var request = new DataAnalysisRequest
        {
            TableSchema = new TableSchema
            {
                Columns = [new ColumnSchema { SnakeCaseName = "element_type" }],
            },
        };
        var recs = DeterministicDataAnalysisService.BuildRecommendations(request);
        Assert.Contains(recs, r => r.Type == "Normalization" && r.Description.Contains("element_type"));
    }

    [Fact]
    public void BuildRecommendations_PrefixGroup_SuggestsNormalization()
    {
        var request = new DataAnalysisRequest
        {
            TableSchema = new TableSchema
            {
                Columns =
                [
                    new ColumnSchema { SnakeCaseName = "emp_name" },
                    new ColumnSchema { SnakeCaseName = "emp_code" },
                    new ColumnSchema { SnakeCaseName = "emp_level" },
                ],
            },
        };
        var recs = DeterministicDataAnalysisService.BuildRecommendations(request);
        Assert.Contains(recs, r => r.Type == "Normalization" && r.Description.Contains("emp"));
    }
}

// ── Helpers ───────────────────────────────────────────────────────────────────

public class DeterministicDataAnalysisHelperTests
{
    [Theory]
    [InlineData("element_type",     true)]
    [InlineData("order_status",     true)]
    [InlineData("pay_category",     true)]
    [InlineData("validation_group", true)]
    [InlineData("name",             false)]
    [InlineData("id",               false)]
    [InlineData("description",      false)]
    public void IsLookupCandidate_ReturnsExpected(string name, bool expected)
    {
        Assert.Equal(expected, DeterministicDataAnalysisService.IsLookupCandidate(name));
    }

    [Theory]
    [InlineData("id",          true)]
    [InlineData("name",        true)]
    [InlineData("code",        true)]
    [InlineData("key",         true)]
    [InlineData("user_id",     true)]
    [InlineData("order_code",  true)]
    [InlineData("api_key",     true)]
    [InlineData("notes",       false)]
    [InlineData("description", false)]
    public void IsImportantColumn_ReturnsExpected(string name, bool expected)
    {
        Assert.Equal(expected, DeterministicDataAnalysisService.IsImportantColumn(name));
    }

    [Fact]
    public void DetectPrefixGroups_ThreeColumnsWithSamePrefix_ReturnsGroup()
    {
        var columns = new List<ColumnSchema>
        {
            new() { SnakeCaseName = "emp_name" },
            new() { SnakeCaseName = "emp_code" },
            new() { SnakeCaseName = "emp_level" },
        };
        var groups = DeterministicDataAnalysisService.DetectPrefixGroups(columns);
        Assert.Single(groups);
        Assert.Equal("emp", groups[0].Prefix);
        Assert.Equal(3, groups[0].Columns.Count);
    }

    [Fact]
    public void DetectPrefixGroups_TwoColumnsWithSamePrefix_NotReturned()
    {
        var columns = new List<ColumnSchema>
        {
            new() { SnakeCaseName = "emp_name" },
            new() { SnakeCaseName = "emp_code" },
        };
        var groups = DeterministicDataAnalysisService.DetectPrefixGroups(columns);
        Assert.Empty(groups);
    }

    [Fact]
    public void DetectPrefixGroups_NoUnderscoreColumns_ReturnsEmpty()
    {
        var columns = new List<ColumnSchema>
        {
            new() { SnakeCaseName = "id" },
            new() { SnakeCaseName = "name" },
        };
        var groups = DeterministicDataAnalysisService.DetectPrefixGroups(columns);
        Assert.Empty(groups);
    }
}

// ── Prompt builder ────────────────────────────────────────────────────────────

public class DataAnalysisPromptBuilderTests
{
    [Fact]
    public void BuildUserMessage_IncludesTableName()
    {
        var request = new DataAnalysisRequest
        {
            TableSchema = new TableSchema { TableName = "validation_scenarios" },
        };
        var msg = DataAnalysisPromptBuilder.BuildUserMessage(request);
        Assert.Contains("validation_scenarios", msg);
    }

    [Fact]
    public void BuildUserMessage_IncludesColumnDetails()
    {
        var request = new DataAnalysisRequest
        {
            TableSchema = new TableSchema
            {
                Columns = [new ColumnSchema
                {
                    SnakeCaseName  = "scenario_id",
                    InferredType   = PostgresType.Integer,
                    IsNullable     = false,
                    IsCandidateKey = true,
                }],
            },
        };
        var msg = DataAnalysisPromptBuilder.BuildUserMessage(request);
        Assert.Contains("scenario_id", msg);
        Assert.Contains("candidate key", msg);
    }

    [Fact]
    public void BuildUserMessage_LimitsSampleRowsToTwenty()
    {
        var rows = Enumerable.Range(1, 30)
            .Select(_ => (IReadOnlyDictionary<string, string?>)
                new Dictionary<string, string?> { ["id"] = "1" })
            .ToList();

        var request = new DataAnalysisRequest
        {
            SheetPreview = new SheetPreview
            {
                Columns       = [new ColumnInfo { SnakeCaseName = "id" }],
                Rows          = rows,
                TotalRowCount = 30,
            },
        };
        var msg = DataAnalysisPromptBuilder.BuildUserMessage(request);
        Assert.Contains("count=\"20\"", msg);
        Assert.DoesNotContain("Row 21:", msg);
    }

    [Fact]
    public void BuildUserMessage_IncludesValidationWarnings()
    {
        var request = new DataAnalysisRequest
        {
            ValidationResult = new ValidationResult
            {
                Warnings = [new ValidationWarning { Message = "High null rate", Severity = ValidationSeverity.Warning }],
            },
        };
        var msg = DataAnalysisPromptBuilder.BuildUserMessage(request);
        Assert.Contains("High null rate", msg);
    }

    [Fact]
    public void BuildUserMessage_NoWarnings_ShowsNone()
    {
        var request = new DataAnalysisRequest { ValidationResult = new ValidationResult() };
        var msg = DataAnalysisPromptBuilder.BuildUserMessage(request);
        Assert.Contains("(none)", msg);
    }

    [Fact]
    public void BuildUserMessage_NullCellValue_RenderedAsNULL()
    {
        var request = new DataAnalysisRequest
        {
            SheetPreview = new SheetPreview
            {
                Columns       = [new ColumnInfo { SnakeCaseName = "col" }],
                Rows          = [new Dictionary<string, string?> { ["col"] = null }],
                TotalRowCount = 1,
            },
        };
        var msg = DataAnalysisPromptBuilder.BuildUserMessage(request);
        Assert.Contains("col=NULL", msg);
    }
}

// ── Response parser ───────────────────────────────────────────────────────────

public class DataAnalysisResponseParserTests
{
    [Fact]
    public void Parse_ValidJson_MapsAllFields()
    {
        const string json = """
            {
              "summary": "Dataset looks good.",
              "findings": [{"category":"CandidateKey","severity":"INFO","description":"id is PK","detail":null}],
              "risks":    [{"category":"DuplicateRisk","severity":"WARNING","description":"name has dupes","detail":"clean up"}],
              "recommendations": [{"priority":"HIGH","type":"PrimaryKey","description":"Use id"}]
            }
            """;

        var result = DataAnalysisResponseParser.Parse(json);

        Assert.Equal("Dataset looks good.", result.Summary);
        Assert.Single(result.Findings);
        Assert.Equal("CandidateKey", result.Findings[0].Category);
        Assert.Equal("INFO", result.Findings[0].Severity);
        Assert.Single(result.Risks);
        Assert.Equal("DuplicateRisk", result.Risks[0].Category);
        Assert.Equal("clean up", result.Risks[0].Detail);
        Assert.Single(result.Recommendations);
        Assert.Equal("HIGH", result.Recommendations[0].Priority);
        Assert.Equal("PrimaryKey", result.Recommendations[0].Type);
    }

    [Fact]
    public void Parse_EmptyJson_ReturnsFallback()
    {
        var result = DataAnalysisResponseParser.Parse(string.Empty);
        Assert.Equal("No response from AI.", result.Summary);
    }

    [Fact]
    public void Parse_InvalidJson_ReturnsFallback()
    {
        var result = DataAnalysisResponseParser.Parse("{{{not valid");
        Assert.Equal("Could not parse AI response.", result.Summary);
    }

    [Fact]
    public void Parse_EmptyArrays_ReturnsEmptyLists()
    {
        const string json = """{"summary":"ok","findings":[],"risks":[],"recommendations":[]}""";
        var result = DataAnalysisResponseParser.Parse(json);
        Assert.Empty(result.Findings);
        Assert.Empty(result.Risks);
        Assert.Empty(result.Recommendations);
    }

    [Fact]
    public void Parse_MissingArrayFields_DefaultsToEmpty()
    {
        const string json = """{"summary":"partial"}""";
        var result = DataAnalysisResponseParser.Parse(json);
        Assert.Equal("partial", result.Summary);
        Assert.Empty(result.Findings);
        Assert.Empty(result.Risks);
        Assert.Empty(result.Recommendations);
    }

    [Fact]
    public void Parse_NullDetailField_MapsToNull()
    {
        const string json = """
            {
              "summary":"s",
              "findings":[{"category":"CandidateKey","severity":"INFO","description":"d","detail":null}],
              "risks":[],
              "recommendations":[]
            }
            """;
        var result = DataAnalysisResponseParser.Parse(json);
        Assert.Null(result.Findings[0].Detail);
    }
}

// ── System prompt ─────────────────────────────────────────────────────────────

public class DataAnalysisSystemPromptTests
{
    [Fact]
    public void SystemPrompt_InstructsJsonOnly()
    {
        Assert.Contains("ONLY with valid JSON", DataAnalysisPromptBuilder.SystemPrompt);
    }

    [Fact]
    public void SystemPrompt_DefinesFindingsCategories()
    {
        Assert.Contains("CandidateKey", DataAnalysisPromptBuilder.SystemPrompt);
        Assert.Contains("DataQuality",  DataAnalysisPromptBuilder.SystemPrompt);
        Assert.Contains("NormalizationOpportunity", DataAnalysisPromptBuilder.SystemPrompt);
    }

    [Fact]
    public void SystemPrompt_DefinesRisksCategories()
    {
        Assert.Contains("DuplicateRisk", DataAnalysisPromptBuilder.SystemPrompt);
        Assert.Contains("NullableRisk",  DataAnalysisPromptBuilder.SystemPrompt);
    }

    [Fact]
    public void SystemPrompt_DefinesRecommendationTypes()
    {
        Assert.Contains("PrimaryKey",       DataAnalysisPromptBuilder.SystemPrompt);
        Assert.Contains("UniqueConstraint", DataAnalysisPromptBuilder.SystemPrompt);
        Assert.Contains("Index",            DataAnalysisPromptBuilder.SystemPrompt);
        Assert.Contains("Normalization",    DataAnalysisPromptBuilder.SystemPrompt);
    }

    [Fact]
    public void SystemPrompt_WarnsTreatDataAsDataOnly()
    {
        Assert.Contains("data only", DataAnalysisPromptBuilder.SystemPrompt);
    }
}

// ── CandidateKeyQuality — duplicate risk suppression ──────────────────────────

public class DeterministicDataAnalysisBooleanRiskTests
{
    [Fact]
    public void BuildRisks_BooleanColumnWithDuplicates_NoDuplicateRisk()
    {
        var request = new DataAnalysisRequest
        {
            TableSchema = new TableSchema
            {
                Columns =
                [
                    new ColumnSchema
                    {
                        SnakeCaseName = "active",
                        InferredType  = PostgresType.Boolean,
                        HasDuplicates = true,
                        IsCandidateKey = false,
                    },
                ],
            },
        };
        var risks = DeterministicDataAnalysisService.BuildRisks(request);
        Assert.DoesNotContain(risks, r => r.Category == "DuplicateRisk" && r.Description.Contains("active"));
    }

    [Fact]
    public void BuildRisks_LookupStatusColumnWithDuplicates_NoDuplicateRisk()
    {
        var request = new DataAnalysisRequest
        {
            TableSchema = new TableSchema
            {
                Columns =
                [
                    new ColumnSchema
                    {
                        SnakeCaseName = "order_status",
                        InferredType  = PostgresType.Text,
                        HasDuplicates = true,
                        IsCandidateKey = false,
                    },
                ],
            },
        };
        var risks = DeterministicDataAnalysisService.BuildRisks(request);
        Assert.DoesNotContain(risks, r => r.Category == "DuplicateRisk" && r.Description.Contains("order_status"));
    }

    [Fact]
    public void BuildRisks_FlagColumnWithDuplicates_NoDuplicateRisk()
    {
        var request = new DataAnalysisRequest
        {
            TableSchema = new TableSchema
            {
                Columns =
                [
                    new ColumnSchema
                    {
                        SnakeCaseName = "is_deleted_flag",
                        InferredType  = PostgresType.Text,
                        HasDuplicates = true,
                        IsCandidateKey = false,
                    },
                ],
            },
        };
        var risks = DeterministicDataAnalysisService.BuildRisks(request);
        Assert.DoesNotContain(risks, r => r.Category == "DuplicateRisk" && r.Description.Contains("is_deleted_flag"));
    }

    [Fact]
    public void BuildRisks_TextColumnWithDuplicates_DuplicateRiskReported()
    {
        var request = new DataAnalysisRequest
        {
            TableSchema = new TableSchema
            {
                Columns =
                [
                    new ColumnSchema
                    {
                        SnakeCaseName = "description",
                        InferredType  = PostgresType.Text,
                        HasDuplicates = true,
                        IsCandidateKey = false,
                    },
                ],
            },
        };
        var risks = DeterministicDataAnalysisService.BuildRisks(request);
        Assert.Contains(risks, r => r.Category == "DuplicateRisk" && r.Description.Contains("description"));
    }
}

// ── CandidateKeyQuality — quality-ranked recommendations ─────────────────────

public class DeterministicDataAnalysisQualityRecommendationTests
{
    [Fact]
    public void BuildRecommendations_NumericUniqueColumn_SuggestsSurrogateNotScore()
    {
        // score is structurally unique but Numeric — should not become PK or UNIQUE
        var request = new DataAnalysisRequest
        {
            TableSchema = new TableSchema
            {
                Columns =
                [
                    new ColumnSchema
                    {
                        SnakeCaseName       = "score",
                        InferredType        = PostgresType.Numeric,
                        IsCandidateKey      = true,
                        CandidateKeyQuality = CandidateKeyQuality.None,
                    },
                ],
            },
        };
        var recs = DeterministicDataAnalysisService.BuildRecommendations(request);
        Assert.Contains(recs, r => r.Type == "PrimaryKey"
                                && r.Description.Contains("surrogate", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(recs, r => r.Type == "UniqueConstraint" && r.Description.Contains("score"));
    }

    [Fact]
    public void BuildRecommendations_BooleanColumn_NeverRecommendedAsKey()
    {
        var request = new DataAnalysisRequest
        {
            TableSchema = new TableSchema
            {
                Columns =
                [
                    new ColumnSchema
                    {
                        SnakeCaseName       = "active",
                        InferredType        = PostgresType.Boolean,
                        IsCandidateKey      = false,
                        CandidateKeyQuality = CandidateKeyQuality.None,
                    },
                ],
            },
        };
        var recs = DeterministicDataAnalysisService.BuildRecommendations(request);
        Assert.DoesNotContain(recs, r => r.Type == "PrimaryKey" && r.Description.Contains("active"));
        Assert.DoesNotContain(recs, r => r.Type == "UniqueConstraint" && r.Description.Contains("active"));
    }

    [Fact]
    public void BuildRecommendations_StrongIdColumn_GetsHighPrimaryKeyRecommendation()
    {
        var request = new DataAnalysisRequest
        {
            TableSchema = new TableSchema
            {
                Columns =
                [
                    new ColumnSchema
                    {
                        SnakeCaseName       = "id",
                        InferredType        = PostgresType.Integer,
                        IsCandidateKey      = true,
                        CandidateKeyQuality = CandidateKeyQuality.Strong,
                    },
                ],
            },
        };
        var recs = DeterministicDataAnalysisService.BuildRecommendations(request);
        Assert.Contains(recs, r =>
            r.Type == "PrimaryKey" &&
            r.Priority == "HIGH" &&
            r.Description.Contains("id"));
    }

    [Fact]
    public void BuildRecommendations_IdStrongPlusUsernamePrefers_IdAsPkAndUsernameAsUnique()
    {
        var request = new DataAnalysisRequest
        {
            TableSchema = new TableSchema
            {
                Columns =
                [
                    new ColumnSchema
                    {
                        SnakeCaseName       = "id",
                        InferredType        = PostgresType.Integer,
                        IsCandidateKey      = true,
                        CandidateKeyQuality = CandidateKeyQuality.Strong,
                    },
                    new ColumnSchema
                    {
                        SnakeCaseName       = "username",
                        InferredType        = PostgresType.Text,
                        IsCandidateKey      = true,
                        CandidateKeyQuality = CandidateKeyQuality.Plausible,
                    },
                ],
            },
        };
        var recs = DeterministicDataAnalysisService.BuildRecommendations(request);
        Assert.Contains(recs, r =>
            r.Type == "PrimaryKey" &&
            r.Priority == "HIGH" &&
            r.Description.Contains("id"));
        Assert.Contains(recs, r =>
            r.Type == "UniqueConstraint" &&
            r.Priority == "MEDIUM" &&
            r.Description.Contains("username"));
        Assert.DoesNotContain(recs, r => r.Type == "UniqueConstraint" && r.Description.Contains("id"));
    }

    [Fact]
    public void BuildRecommendations_IdStrongWithScoreNone_ScoreGetsNoRecommendation()
    {
        var request = new DataAnalysisRequest
        {
            TableSchema = new TableSchema
            {
                Columns =
                [
                    new ColumnSchema
                    {
                        SnakeCaseName       = "id",
                        InferredType        = PostgresType.Integer,
                        IsCandidateKey      = true,
                        CandidateKeyQuality = CandidateKeyQuality.Strong,
                    },
                    new ColumnSchema
                    {
                        SnakeCaseName       = "score",
                        InferredType        = PostgresType.Numeric,
                        IsCandidateKey      = true,
                        CandidateKeyQuality = CandidateKeyQuality.None,
                    },
                ],
            },
        };
        var recs = DeterministicDataAnalysisService.BuildRecommendations(request);
        Assert.Contains(recs, r => r.Type == "PrimaryKey" && r.Description.Contains("id"));
        Assert.DoesNotContain(recs, r => r.Type == "UniqueConstraint" && r.Description.Contains("score"));
    }

    [Fact]
    public void BuildRecommendations_WeakCandidateOnly_SuggestsSurrogate()
    {
        // A column that is structurally unique but semantically Weak should not become PK
        var request = new DataAnalysisRequest
        {
            TableSchema = new TableSchema
            {
                Columns =
                [
                    new ColumnSchema
                    {
                        SnakeCaseName       = "description",
                        InferredType        = PostgresType.Text,
                        IsCandidateKey      = true,
                        CandidateKeyQuality = CandidateKeyQuality.Weak,
                    },
                ],
            },
        };
        var recs = DeterministicDataAnalysisService.BuildRecommendations(request);
        Assert.Contains(recs, r =>
            r.Type == "PrimaryKey" &&
            r.Description.Contains("surrogate", StringComparison.OrdinalIgnoreCase));
    }
}

// ── SchemaInferenceService — CandidateKeyQuality computation ─────────────────

public class SchemaInferenceQualityTests
{
    [Theory]
    [InlineData("id",          PostgresType.Integer,  false, false, CandidateKeyQuality.Strong)]
    [InlineData("user_id",     PostgresType.Integer,  false, false, CandidateKeyQuality.Strong)]
    [InlineData("report_code", PostgresType.Text,     false, false, CandidateKeyQuality.Strong)]
    [InlineData("api_key",     PostgresType.Text,     false, false, CandidateKeyQuality.Strong)]
    [InlineData("order_number",PostgresType.Integer,  false, false, CandidateKeyQuality.Strong)]
    [InlineData("username",    PostgresType.Text,     false, false, CandidateKeyQuality.Plausible)]
    [InlineData("email",       PostgresType.Text,     false, false, CandidateKeyQuality.Plausible)]
    [InlineData("name",        PostgresType.Text,     false, false, CandidateKeyQuality.Plausible)]
    [InlineData("full_name",   PostgresType.Text,     false, false, CandidateKeyQuality.Plausible)]
    [InlineData("score",       PostgresType.Numeric,  false, false, CandidateKeyQuality.None)]
    [InlineData("amount",      PostgresType.Numeric,  false, false, CandidateKeyQuality.None)]
    [InlineData("active",      PostgresType.Boolean,  false, false, CandidateKeyQuality.None)]
    [InlineData("created_at",  PostgresType.Timestamp,false, false, CandidateKeyQuality.None)]
    [InlineData("birth_date",  PostgresType.Date,     false, false, CandidateKeyQuality.None)]
    [InlineData("description", PostgresType.Text,     false, false, CandidateKeyQuality.Weak)]
    [InlineData("id",          PostgresType.Integer,  true,  false, CandidateKeyQuality.None)]   // nullable
    [InlineData("id",          PostgresType.Integer,  false, true,  CandidateKeyQuality.None)]   // has duplicates
    public void ComputeCandidateKeyQuality_ReturnsExpected(
        string name,
        PostgresType type,
        bool isNullable,
        bool hasDuplicates,
        CandidateKeyQuality expected)
    {
        var result = SchemaInferenceService.ComputeCandidateKeyQuality(name, type, isNullable, hasDuplicates);
        Assert.Equal(expected, result);
    }
}

// ── Summary — quality-aware wording ──────────────────────────────────────────

public class DeterministicDataAnalysisSummaryQualityTests
{
    private static ColumnSchema StrongCol(string name) => new()
    {
        SnakeCaseName       = name,
        IsCandidateKey      = true,
        CandidateKeyQuality = CandidateKeyQuality.Strong,
        InferredType        = PostgresType.Integer,
    };

    private static ColumnSchema PlausibleCol(string name) => new()
    {
        SnakeCaseName       = name,
        IsCandidateKey      = true,
        CandidateKeyQuality = CandidateKeyQuality.Plausible,
        InferredType        = PostgresType.Text,
    };

    private static ColumnSchema DisqualifiedCol(string name, PostgresType type) => new()
    {
        SnakeCaseName       = name,
        IsCandidateKey      = true,
        CandidateKeyQuality = CandidateKeyQuality.None,
        InferredType        = type,
    };

    private static DataAnalysisRequest Req(params ColumnSchema[] cols) => new()
    {
        TableSchema  = new TableSchema { Columns = cols },
        SheetPreview = new SheetPreview { TotalRowCount = 5 },
    };

    [Fact]
    public void BuildSummary_SingleStrongColumn_MentionsStrongCandidateAndName()
    {
        var summary = DeterministicDataAnalysisService.BuildSummary(Req(StrongCol("id")));
        Assert.Contains("id", summary);
        Assert.Contains("strong primary key candidate", summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildSummary_SinglePlausibleColumn_MentionsPlausibleAlternateKey()
    {
        var summary = DeterministicDataAnalysisService.BuildSummary(Req(PlausibleCol("username")));
        Assert.Contains("username", summary);
        Assert.Contains("plausible alternate key", summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildSummary_SingleDisqualifiedColumn_MentionsNotRecommended()
    {
        var summary = DeterministicDataAnalysisService.BuildSummary(
            Req(DisqualifiedCol("score", PostgresType.Numeric)));
        Assert.Contains("score", summary);
        Assert.Contains("not recommended", summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildSummary_NoStructuralCandidates_MentionsNoUniqueColumn()
    {
        var req = Req(new ColumnSchema { SnakeCaseName = "notes", IsNullable = true });
        var summary = DeterministicDataAnalysisService.BuildSummary(req);
        Assert.Contains("surrogate", summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildSummary_ThreeStructuralWithIdStrongAndUsername_MentionsStructuralCount()
    {
        var req = Req(
            StrongCol("id"),
            PlausibleCol("username"),
            DisqualifiedCol("score", PostgresType.Numeric));
        var summary = DeterministicDataAnalysisService.BuildSummary(req);
        Assert.Contains("3", summary);
        Assert.Contains("structurally unique", summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildSummary_ThreeStructuralWithIdStrongAndUsername_MentionsStrongCandidate()
    {
        var req = Req(
            StrongCol("id"),
            PlausibleCol("username"),
            DisqualifiedCol("score", PostgresType.Numeric));
        var summary = DeterministicDataAnalysisService.BuildSummary(req);
        Assert.Contains("1 strong primary key candidate", summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id", summary);
    }

    [Fact]
    public void BuildSummary_ThreeStructuralWithIdStrongAndUsername_MentionsPlausibleAlternateKey()
    {
        var req = Req(
            StrongCol("id"),
            PlausibleCol("username"),
            DisqualifiedCol("score", PostgresType.Numeric));
        var summary = DeterministicDataAnalysisService.BuildSummary(req);
        Assert.Contains("1 plausible alternate key", summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("username", summary);
    }

    [Fact]
    public void BuildSummary_AllWeakOrDisqualified_SaysNoRecommendedPrimaryKey()
    {
        var req = Req(
            DisqualifiedCol("score",  PostgresType.Numeric),
            DisqualifiedCol("active", PostgresType.Boolean));
        var summary = DeterministicDataAnalysisService.BuildSummary(req);
        Assert.Contains("No primary key candidate is recommended", summary,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildSummary_DoesNotSayCandidatePrimaryKeys_WhenAllDisqualified()
    {
        var req = Req(
            DisqualifiedCol("score",  PostgresType.Numeric),
            DisqualifiedCol("active", PostgresType.Boolean));
        var summary = DeterministicDataAnalysisService.BuildSummary(req);
        Assert.DoesNotContain("candidate primary key", summary, StringComparison.OrdinalIgnoreCase);
    }
}

// ── Findings — key quality badge is in Schema, not in findings ───────────────

public class DeterministicDataAnalysisFindingsQualityTests
{
    private static DataAnalysisRequest Req(params ColumnSchema[] cols) => new()
    {
        TableSchema = new TableSchema { Columns = cols },
    };

    [Theory]
    [InlineData(CandidateKeyQuality.Strong)]
    [InlineData(CandidateKeyQuality.Plausible)]
    [InlineData(CandidateKeyQuality.Weak)]
    [InlineData(CandidateKeyQuality.None)]
    public void BuildFindings_AnyKeyQuality_NoPerColumnInfoFinding(CandidateKeyQuality quality)
    {
        var req = Req(new ColumnSchema
        {
            SnakeCaseName       = "col",
            InferredType        = PostgresType.Text,
            IsCandidateKey      = quality != CandidateKeyQuality.None,
            CandidateKeyQuality = quality,
        });
        var findings = DeterministicDataAnalysisService.BuildFindings(req);
        Assert.DoesNotContain(findings, f => f.Category == "CandidateKey" && f.Severity == "INFO");
    }

    [Fact]
    public void BuildFindings_NoQualifiedCandidates_AllDisqualified_ProducesWarningWithSurrogateSuggestion()
    {
        var req = Req(
            new ColumnSchema
            {
                SnakeCaseName       = "score",
                InferredType        = PostgresType.Numeric,
                IsCandidateKey      = true,
                CandidateKeyQuality = CandidateKeyQuality.None,
            });
        var findings = DeterministicDataAnalysisService.BuildFindings(req);
        var warning = Assert.Single(findings, f =>
            f.Category == "CandidateKey" && f.Severity == "WARNING");
        Assert.Contains("surrogate", warning.Detail, StringComparison.OrdinalIgnoreCase);
        // Warning detail should note that structural candidates exist but are semantically unsuitable
        Assert.DoesNotContain("nullable or duplicate", warning.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildFindings_NoStructuralCandidatesAtAll_WarningDetailMentionsNullableOrDuplicate()
    {
        var req = Req(new ColumnSchema { SnakeCaseName = "notes", IsNullable = true });
        var findings = DeterministicDataAnalysisService.BuildFindings(req);
        var warning = Assert.Single(findings, f =>
            f.Category == "CandidateKey" && f.Severity == "WARNING");
        Assert.Contains("nullable or duplicate", warning.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildFindings_StrongColumn_DoesNotProduceWarning()
    {
        var req = Req(new ColumnSchema
        {
            SnakeCaseName       = "id",
            InferredType        = PostgresType.Integer,
            IsCandidateKey      = true,
            CandidateKeyQuality = CandidateKeyQuality.Strong,
        });
        var findings = DeterministicDataAnalysisService.BuildFindings(req);
        Assert.DoesNotContain(findings, f =>
            f.Category == "CandidateKey" && f.Severity == "WARNING");
    }

    [Fact]
    public void BuildFindings_IdStrongAndUsernameAndScore_NoPerColumnFindingsAndNoWarning()
    {
        var req = Req(
            new ColumnSchema
            {
                SnakeCaseName       = "id",
                InferredType        = PostgresType.Integer,
                IsCandidateKey      = true,
                CandidateKeyQuality = CandidateKeyQuality.Strong,
            },
            new ColumnSchema
            {
                SnakeCaseName       = "username",
                InferredType        = PostgresType.Text,
                IsCandidateKey      = true,
                CandidateKeyQuality = CandidateKeyQuality.Plausible,
            },
            new ColumnSchema
            {
                SnakeCaseName       = "score",
                InferredType        = PostgresType.Numeric,
                IsCandidateKey      = true,
                CandidateKeyQuality = CandidateKeyQuality.None,
            });
        var findings = DeterministicDataAnalysisService.BuildFindings(req);
        Assert.DoesNotContain(findings, f => f.Category == "CandidateKey" && f.Severity == "INFO");
        Assert.DoesNotContain(findings, f => f.Category == "CandidateKey" && f.Severity == "WARNING");
    }
}

// ── GetTypeDisqualificationReason helper ─────────────────────────────────────

public class GetTypeDisqualificationReasonTests
{
    [Theory]
    [InlineData(PostgresType.Boolean,   "two distinct values")]
    [InlineData(PostgresType.Numeric,   "numeric value column")]
    [InlineData(PostgresType.Date,      "date column")]
    [InlineData(PostgresType.Timestamp, "timestamp column")]
    public void GetTypeDisqualificationReason_ReturnsExpectedPhrase(
        PostgresType type,
        string expectedPhrase)
    {
        var reason = DeterministicDataAnalysisService.GetTypeDisqualificationReason(type);
        Assert.Contains(expectedPhrase, reason, StringComparison.OrdinalIgnoreCase);
    }
}
