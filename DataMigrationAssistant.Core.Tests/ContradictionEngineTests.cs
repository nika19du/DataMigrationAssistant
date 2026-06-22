using DataMigrationAssistant.Core.Models;
using DataMigrationAssistant.Core.Services;

namespace DataMigrationAssistant.Core.Tests;

/// <summary>
/// Tests for ContradictionEngine and all IContradictionRule implementations.
/// Covers every category listed in the requirements.
/// </summary>
public class ContradictionEngineTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ColumnSchema Col(string name, PostgresType type,
        bool nullable = false, bool candidateKey = false)
        => new()
        {
            Name = name, SnakeCaseName = name,
            InferredType = type, IsNullable = nullable, IsCandidateKey = candidateKey,
        };

    private static AiReviewRequest MakeRequest(
        IReadOnlyList<ColumnSchema>? columns = null,
        IReadOnlyList<ValidationWarning>? warnings = null,
        IReadOnlyList<IReadOnlyDictionary<string, string?>>? rows = null,
        DataAnalysisResult? analysis = null)
        => new()
        {
            Mode               = AiReviewMode.Dataset,
            TableSchema        = new TableSchema { Columns = columns ?? [] },
            ValidationResult   = new ValidationResult { Warnings = warnings ?? [] },
            SheetPreview       = new SheetPreview { Rows = rows ?? [] },
            DataAnalysisResult = analysis,
        };

    private static AiReviewResult MakeResult(
        IReadOnlyList<AiReviewRisk>? risks = null,
        IReadOnlyList<AiReviewRecommendation>? recs = null)
        => new() { Summary = "Test", Risks = risks ?? [], Recommendations = recs ?? [] };

    private static DataAnalysisResult MakeAnalysis(
        string pkColumn = "id",
        bool hasDuplicateRiskForScore = false,
        bool hasNullableRiskForNotes = false)
    {
        var findings = new List<DataAnalysisFinding>
        {
            new()
            {
                Category    = "CandidateKey",
                Severity    = "INFO",
                Description = $"'{pkColumn}' is a strong primary key candidate",
                Detail      = "Type: INTEGER.",
            },
            new()
            {
                Category    = "CandidateKey",
                Severity    = "INFO",
                Description = "'score' is sample-unique but not recommended as a key",
                Detail      = "It is not recommended because it is a numeric value column.",
            },
        };

        var risks = new List<DataAnalysisFinding>();
        if (hasDuplicateRiskForScore)
            risks.Add(new DataAnalysisFinding
            {
                Category    = "DuplicateRisk",
                Severity    = "WARNING",
                Description = "'score' contains duplicate values",
            });
        if (hasNullableRiskForNotes)
            risks.Add(new DataAnalysisFinding
            {
                Category    = "NullableRisk",
                Severity    = "WARNING",
                Description = "'notes' is nullable but appears to be an important field",
            });

        return new DataAnalysisResult
        {
            Summary = "Dataset has 10 rows.",
            Findings = findings,
            Risks    = risks,
            Recommendations =
            [
                new DataAnalysisRecommendation
                {
                    Priority    = "HIGH",
                    Type        = "PrimaryKey",
                    Description = $"Designate '{pkColumn}' as the PRIMARY KEY — it is the strongest candidate key.",
                },
            ],
        };
    }

    // ── Mode guard ────────────────────────────────────────────────────────────

    [Fact]
    public void MigrationMode_NothingFiltered()
    {
        var request = new AiReviewRequest
        {
            Mode        = AiReviewMode.Migration,
            TableSchema = new TableSchema { Columns = [Col("score", PostgresType.Numeric)] },
        };
        var risk = new AiReviewRisk
        {
            Level = "HIGH", Description = "score stored as text", Evidence = "schema: score (NUMERIC)"
        };

        var result   = MakeResult(risks: [risk]);
        var filtered = ContradictionEngine.Apply(result, request);

        Assert.Same(result, filtered);
    }

    // ── NUMERIC column — TypeStoredAsTextRule ─────────────────────────────────

    [Fact]
    public void NumericColumn_StoredAsText_Claim_IsFiltered()
    {
        var request = MakeRequest(columns: [Col("score", PostgresType.Numeric)]);
        var risk = new AiReviewRisk
        {
            Level = "HIGH", Description = "score is stored as text", Evidence = "schema: score (NUMERIC)"
        };

        var filtered = ContradictionEngine.Apply(MakeResult(risks: [risk]), request);

        Assert.Empty(filtered.Risks);
    }

    [Fact]
    public void NumericColumn_StoredAsText_Claim_WithExplicitColumnField_IsFiltered()
    {
        var request = MakeRequest(columns: [Col("score", PostgresType.Numeric)]);
        var risk = new AiReviewRisk
        {
            Level = "HIGH", Description = "Values are stored as text format",
            Evidence = "sample rows show text values", Column = "score",
        };

        var filtered = ContradictionEngine.Apply(MakeResult(risks: [risk]), request);

        Assert.Empty(filtered.Risks);
    }

    [Fact]
    public void NumericColumn_NonNumericValues_Claim_IsFiltered()
    {
        var request = MakeRequest(columns: [Col("score", PostgresType.Numeric)]);
        var risk = new AiReviewRisk
        {
            Level = "HIGH", Description = "non-numeric values detected in score column",
            Evidence = "sample rows: score=9,5",
        };

        var filtered = ContradictionEngine.Apply(MakeResult(risks: [risk]), request);

        Assert.Empty(filtered.Risks);
    }

    // ── NUMERIC column — CastToNumericRule ────────────────────────────────────

    [Fact]
    public void NumericColumn_CastToNumericRecommendation_IsFiltered()
    {
        var request = MakeRequest(columns: [Col("score", PostgresType.Numeric)]);
        var rec = new AiReviewRecommendation
        {
            Priority = "MEDIUM", Description = "Cast score to numeric before inserting",
            Evidence = "schema: score (NUMERIC)", Column = "score",
        };

        var filtered = ContradictionEngine.Apply(MakeResult(recs: [rec]), request);

        Assert.Empty(filtered.Recommendations);
    }

    [Fact]
    public void NumericColumn_ConvertToNumericRecommendation_IsFiltered()
    {
        var request = MakeRequest(columns: [Col("price", PostgresType.Numeric)]);
        var rec = new AiReviewRecommendation
        {
            Priority = "LOW", Description = "Convert price to numeric to avoid type errors",
            Evidence = "schema: price (NUMERIC)",
        };

        var filtered = ContradictionEngine.Apply(MakeResult(recs: [rec]), request);

        Assert.Empty(filtered.Recommendations);
    }

    // ── NUMERIC column — TypeInferenceRule ────────────────────────────────────

    [Fact]
    public void NumericColumn_TypeInferenceRisk_IsFiltered()
    {
        var request = MakeRequest(columns: [Col("score", PostgresType.Numeric)]);
        var risk = new AiReviewRisk
        {
            Level = "HIGH", Description = "score has type inference risk due to ambiguous format",
            Evidence = "schema: score (NUMERIC)",
        };

        var filtered = ContradictionEngine.Apply(MakeResult(risks: [risk]), request);

        Assert.Empty(filtered.Risks);
    }

    [Fact]
    public void NumericColumn_IncorrectTypeInference_IsFiltered()
    {
        var request = MakeRequest(columns: [Col("score", PostgresType.Numeric)]);
        var risk = new AiReviewRisk
        {
            Level = "MEDIUM", Description = "incorrect type inference detected in score column",
            Evidence = "sample rows: score=9,5",
        };

        var filtered = ContradictionEngine.Apply(MakeResult(risks: [risk]), request);

        Assert.Empty(filtered.Risks);
    }

    [Fact]
    public void NumericColumn_TypeMismatch_IsFiltered()
    {
        var request = MakeRequest(columns: [Col("score", PostgresType.Numeric)]);
        var risk = new AiReviewRisk
        {
            Level = "MEDIUM", Description = "type mismatch in score column",
            Evidence = "schema: score (NUMERIC)",
        };

        var filtered = ContradictionEngine.Apply(MakeResult(risks: [risk]), request);

        Assert.Empty(filtered.Risks);
    }

    [Fact]
    public void NumericColumn_LowFormattingNote_IsAllowed()
    {
        var request = MakeRequest(columns: [Col("score", PostgresType.Numeric)]);
        var risk = new AiReviewRisk
        {
            Level = "LOW", Description = "score uses comma decimal notation — normalize before insert",
            Evidence = "sample rows: score=9,5",
        };

        var filtered = ContradictionEngine.Apply(MakeResult(risks: [risk]), request);

        Assert.Single(filtered.Risks);
    }

    // ── BOOLEAN column ────────────────────────────────────────────────────────

    [Fact]
    public void BooleanColumn_BooleanConversionRecommendation_IsFiltered()
    {
        var request = MakeRequest(columns: [Col("active", PostgresType.Boolean)]);
        var rec = new AiReviewRecommendation
        {
            Priority = "MEDIUM", Description = "Consider boolean conversion for active column",
            Evidence = "sample rows: active=TRUE, active=FALSE",
        };

        var filtered = ContradictionEngine.Apply(MakeResult(recs: [rec]), request);

        Assert.Empty(filtered.Recommendations);
    }

    [Fact]
    public void BooleanColumn_StoredAsTextRisk_IsFiltered()
    {
        var request = MakeRequest(columns: [Col("active", PostgresType.Boolean)]);
        var risk = new AiReviewRisk
        {
            Level = "HIGH", Description = "active is stored as text instead of boolean",
            Evidence = "sample rows: active=TRUE, active=FALSE",
        };

        var filtered = ContradictionEngine.Apply(MakeResult(risks: [risk]), request);

        Assert.Empty(filtered.Risks);
    }

    [Fact]
    public void BooleanColumn_TextColumnBooleanConversion_IsAllowed()
    {
        // TEXT column: boolean conversion claim IS legitimate
        var request = MakeRequest(columns: [Col("active", PostgresType.Text)]);
        var risk = new AiReviewRisk
        {
            Level = "MEDIUM", Description = "active column stores boolean values as text — consider boolean conversion",
            Evidence = "schema: active (TEXT, NOT NULL); sample rows: active=TRUE, active=FALSE",
        };

        var filtered = ContradictionEngine.Apply(MakeResult(risks: [risk]), request);

        Assert.Single(filtered.Risks);
    }

    // ── Validation-selected primary key ───────────────────────────────────────

    [Fact]
    public void DataAnalysis_RecommendsIdAsPk_AiRecommendsDifferentColumn_IsFiltered()
    {
        var analysis = MakeAnalysis(pkColumn: "id");
        var request  = MakeRequest(
            columns: [Col("id", PostgresType.Integer, candidateKey: true), Col("score", PostgresType.Numeric)],
            analysis: analysis);
        var rec = new AiReviewRecommendation
        {
            Priority = "HIGH", Description = "Use score as the primary key — it appears unique in the sample",
            Evidence = "schema: score (NUMERIC, NOT NULL, candidate key)",
        };

        var filtered = ContradictionEngine.Apply(MakeResult(recs: [rec]), request);

        Assert.Empty(filtered.Recommendations);
    }

    [Fact]
    public void DataAnalysis_RecommendsIdAsPk_AiRecommendsId_IsAllowed()
    {
        var analysis = MakeAnalysis(pkColumn: "id");
        var request  = MakeRequest(
            columns: [Col("id", PostgresType.Integer, candidateKey: true)],
            analysis: analysis);
        var rec = new AiReviewRecommendation
        {
            Priority = "HIGH", Description = "Designate id as the primary key for this table",
            Evidence = "schema: id (INTEGER, NOT NULL, candidate key)",
        };

        var filtered = ContradictionEngine.Apply(MakeResult(recs: [rec]), request);

        Assert.Single(filtered.Recommendations);
    }

    // ── No duplicate risk in Data Analysis ───────────────────────────────────

    [Fact]
    public void DataAnalysis_NoDuplicateRisk_AiRaisedDuplicateRisk_IsFiltered()
    {
        var analysis = MakeAnalysis(hasDuplicateRiskForScore: false);
        var request  = MakeRequest(
            columns: [Col("id", PostgresType.Integer), Col("score", PostgresType.Numeric)],
            analysis: analysis);
        var risk = new AiReviewRisk
        {
            Level = "MEDIUM", Description = "Duplicate values detected in score column",
            Evidence = "schema: score (NUMERIC, NOT NULL)",
        };

        var filtered = ContradictionEngine.Apply(MakeResult(risks: [risk]), request);

        Assert.Empty(filtered.Risks);
    }

    [Fact]
    public void DataAnalysis_HasDuplicateRisk_AiDuplicateRisk_IsKept()
    {
        var analysis = MakeAnalysis(hasDuplicateRiskForScore: true);
        var request  = MakeRequest(
            columns: [Col("id", PostgresType.Integer), Col("score", PostgresType.Numeric)],
            analysis: analysis);
        var risk = new AiReviewRisk
        {
            Level = "MEDIUM", Description = "Duplicate values detected in score column",
            Evidence = "Data Analysis: score contains duplicate values",
        };

        var filtered = ContradictionEngine.Apply(MakeResult(risks: [risk]), request);

        Assert.Single(filtered.Risks);
    }

    // ── No nullable risk ──────────────────────────────────────────────────────

    [Fact]
    public void NotNullColumn_NoEvidence_NullabilityRisk_IsFiltered()
    {
        var request = MakeRequest(columns: [Col("active", PostgresType.Boolean)]);
        var risk = new AiReviewRisk
        {
            Level = "MEDIUM", Description = "Nullable risk in active column",
            Evidence = "active=FALSE",
        };

        var filtered = ContradictionEngine.Apply(MakeResult(risks: [risk]), request);

        Assert.Empty(filtered.Risks);
    }

    [Fact]
    public void NullableColumn_NullabilityRisk_IsAllowed()
    {
        var request = MakeRequest(columns: [Col("notes", PostgresType.Text, nullable: true)]);
        var risk = new AiReviewRisk
        {
            Level = "LOW", Description = "notes column is nullable — confirm NULL intent",
            Evidence = "schema: notes (TEXT, NULL)",
        };

        var filtered = ContradictionEngine.Apply(MakeResult(risks: [risk]), request);

        Assert.Single(filtered.Risks);
    }

    [Fact]
    public void DataAnalysis_NullableRiskPresent_AiNullableRisk_IsAllowed()
    {
        var analysis = MakeAnalysis(hasNullableRiskForNotes: true);
        var request  = MakeRequest(
            columns: [Col("notes", PostgresType.Text, nullable: true)],
            analysis: analysis);
        var risk = new AiReviewRisk
        {
            Level = "LOW", Description = "notes column is nullable but appears important",
            Evidence = "Data Analysis: notes is nullable but appears to be an important field",
        };

        var filtered = ContradictionEngine.Apply(MakeResult(risks: [risk]), request);

        Assert.Single(filtered.Risks);
    }

    // ── New evidence-backed risk that IS allowed ───────────────────────────────

    [Fact]
    public void NewEvidenceBackedRisk_NotCoveredByDa_IsAllowed()
    {
        // DA ran, covering id and score. AI adds a notes nullable risk — not covered by DA → allowed.
        var analysis = MakeAnalysis();
        var request  = MakeRequest(
            columns:
            [
                Col("id",    PostgresType.Integer, candidateKey: true),
                Col("score", PostgresType.Numeric),
                Col("notes", PostgresType.Text, nullable: true),
            ],
            analysis: analysis);
        var risk = new AiReviewRisk
        {
            Level = "LOW", Description = "notes column is nullable — confirm whether NULL values are expected",
            Evidence = "schema: notes (TEXT, NULL)",
        };

        var filtered = ContradictionEngine.Apply(MakeResult(risks: [risk]), request);

        Assert.Single(filtered.Risks);
    }

    [Fact]
    public void EvidenceBackedDuplicateRisk_NoDaButActualDuplicates_IsAllowed()
    {
        // DA not run. id column has actual duplicates in rows → claim is valid.
        var rows = new List<IReadOnlyDictionary<string, string?>>
        {
            new Dictionary<string, string?> { ["id"] = "1" },
            new Dictionary<string, string?> { ["id"] = "1" },
        };
        var request = MakeRequest(
            columns: [Col("id", PostgresType.Integer, candidateKey: true)],
            rows: rows);
        var risk = new AiReviewRisk
        {
            Level = "HIGH", Description = "Duplicate values detected in id column",
            Evidence = "Row 1 and Row 2 both have id=1",
        };

        var filtered = ContradictionEngine.Apply(MakeResult(risks: [risk]), request);

        Assert.Single(filtered.Risks);
    }

    // ── DA authority — not-recommended key ───────────────────────────────────

    [Fact]
    public void DataAnalysis_ScoreNotRecommendedAsKey_AiSuggestsUniqueConstraint_IsFiltered()
    {
        var analysis = MakeAnalysis();
        var request  = MakeRequest(
            columns: [Col("id", PostgresType.Integer, candidateKey: true), Col("score", PostgresType.Numeric)],
            analysis: analysis);
        var rec = new AiReviewRecommendation
        {
            Priority = "MEDIUM", Description = "Add UNIQUE constraint on score — it appears unique in the sample",
            Evidence = "schema: score (NUMERIC, NOT NULL, candidate key)",
        };

        var filtered = ContradictionEngine.Apply(MakeResult(recs: [rec]), request);

        Assert.Empty(filtered.Recommendations);
    }

    // ── Prompt assertions — explanation-layer role ────────────────────────────

    [Fact]
    public void DatasetSystemPrompt_FramesAiAsExplanationLayer()
    {
        Assert.Contains("explanation layer", AiReviewPromptBuilder.DatasetSystemPrompt);
    }

    [Fact]
    public void DatasetSystemPrompt_StatesSchemaIsAuthoritative()
    {
        Assert.Contains("Schema inference is authoritative", AiReviewPromptBuilder.DatasetSystemPrompt);
    }

    [Fact]
    public void DatasetSystemPrompt_StatesValidationIsAuthoritative()
    {
        Assert.Contains("Validation is authoritative", AiReviewPromptBuilder.DatasetSystemPrompt);
    }

    [Fact]
    public void DatasetSystemPrompt_StatesDataAnalysisIsAuthoritative2()
    {
        Assert.Contains("Data Analysis is authoritative", AiReviewPromptBuilder.DatasetSystemPrompt);
    }

    [Fact]
    public void DatasetSystemPrompt_StatesAiMayExplain()
    {
        Assert.Contains("AI Review may explain", AiReviewPromptBuilder.DatasetSystemPrompt);
    }

    [Fact]
    public void DatasetSystemPrompt_StatesAiMayNotContradict()
    {
        Assert.Contains("AI Review may not contradict", AiReviewPromptBuilder.DatasetSystemPrompt);
    }

    [Fact]
    public void DatasetSystemPrompt_RequiresColumnFieldInRisksJson()
    {
        Assert.Contains("\"column\":string|null", AiReviewPromptBuilder.DatasetSystemPrompt);
    }

    // ── Parser maps column field ──────────────────────────────────────────────

    [Fact]
    public void Parse_RiskWithColumn_MapsColumnField()
    {
        var json = """
            {
              "summary": "ok",
              "risks": [{"level":"HIGH","description":"risk","evidence":"schema","column":"score"}],
              "recommendations": []
            }
            """;

        var result = AiReviewResponseParser.Parse(json);

        Assert.Equal("score", result.Risks[0].Column);
    }

    [Fact]
    public void Parse_RecommendationWithColumn_MapsColumnField()
    {
        var json = """
            {
              "summary": "ok",
              "risks": [],
              "recommendations": [{"priority":"HIGH","description":"rec","evidence":"schema","column":"id"}]
            }
            """;

        var result = AiReviewResponseParser.Parse(json);

        Assert.Equal("id", result.Recommendations[0].Column);
    }

    [Fact]
    public void Parse_RiskWithNullColumn_MapsColumnAsNull()
    {
        var json = """
            {
              "summary": "ok",
              "risks": [{"level":"LOW","description":"risk","evidence":"schema","column":null}],
              "recommendations": []
            }
            """;

        var result = AiReviewResponseParser.Parse(json);

        Assert.Null(result.Risks[0].Column);
    }

    [Fact]
    public void Parse_RiskWithoutColumnField_ColumnIsNull()
    {
        var json = """
            {
              "summary": "ok",
              "risks": [{"level":"LOW","description":"risk","evidence":"schema"}],
              "recommendations": []
            }
            """;

        var result = AiReviewResponseParser.Parse(json);

        Assert.Null(result.Risks[0].Column);
    }
}
