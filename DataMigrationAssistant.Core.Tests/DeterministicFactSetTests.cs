using DataMigrationAssistant.Core.Models;
using DataMigrationAssistant.Core.Services;

namespace DataMigrationAssistant.Core.Tests;

public class DeterministicFactSetTests
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

    // ── TypeOf ────────────────────────────────────────────────────────────────

    [Fact]
    public void TypeOf_KnownColumn_ReturnsInferredType()
    {
        var facts = DeterministicFactSet.Build(MakeRequest(columns: [Col("score", PostgresType.Numeric)]));
        Assert.Equal(PostgresType.Numeric, facts.TypeOf("score"));
    }

    [Fact]
    public void TypeOf_UnknownColumn_ReturnsNull()
    {
        var facts = DeterministicFactSet.Build(MakeRequest());
        Assert.Null(facts.TypeOf("nonexistent"));
    }

    [Fact]
    public void TypeOf_IsCaseInsensitive()
    {
        var facts = DeterministicFactSet.Build(MakeRequest(columns: [Col("Score", PostgresType.Numeric)]));
        Assert.Equal(PostgresType.Numeric, facts.TypeOf("score"));
    }

    // ── IsNullable ────────────────────────────────────────────────────────────

    [Fact]
    public void IsNullable_NullableColumn_ReturnsTrue()
    {
        var facts = DeterministicFactSet.Build(MakeRequest(columns: [Col("notes", PostgresType.Text, nullable: true)]));
        Assert.True(facts.IsNullable("notes"));
    }

    [Fact]
    public void IsNullable_NotNullColumn_ReturnsFalse()
    {
        var facts = DeterministicFactSet.Build(MakeRequest(columns: [Col("id", PostgresType.Integer)]));
        Assert.False(facts.IsNullable("id"));
    }

    // ── HasSchemaCandidate ────────────────────────────────────────────────────

    [Fact]
    public void HasSchemaCandidate_CandidateKeyColumn_ReturnsTrue()
    {
        var facts = DeterministicFactSet.Build(MakeRequest(columns: [Col("id", PostgresType.Integer, candidateKey: true)]));
        Assert.True(facts.HasSchemaCandidate("id"));
    }

    [Fact]
    public void HasSchemaCandidate_NonCandidateColumn_ReturnsFalse()
    {
        var facts = DeterministicFactSet.Build(MakeRequest(columns: [Col("score", PostgresType.Numeric)]));
        Assert.False(facts.HasSchemaCandidate("score"));
    }

    // ── HasDuplicateRisk ──────────────────────────────────────────────────────

    [Fact]
    public void HasDuplicateRisk_DaRanWithRisk_ReturnsTrue()
    {
        var analysis = new DataAnalysisResult
        {
            Summary = "x",
            Risks   = [new DataAnalysisFinding { Category = "DuplicateRisk", Severity = "WARNING", Description = "'score' contains duplicate values" }],
        };
        var facts = DeterministicFactSet.Build(MakeRequest(
            columns: [Col("score", PostgresType.Numeric)], analysis: analysis));
        Assert.True(facts.HasDuplicateRisk("score"));
    }

    [Fact]
    public void HasDuplicateRisk_DaRanNoRisk_ReturnsFalse()
    {
        var analysis = new DataAnalysisResult { Summary = "x", Risks = [] };
        var facts    = DeterministicFactSet.Build(MakeRequest(analysis: analysis));
        Assert.False(facts.HasDuplicateRisk("score"));
    }

    [Fact]
    public void HasDuplicateRisk_DaNotRan_ReturnsFalse()
    {
        var facts = DeterministicFactSet.Build(MakeRequest());
        Assert.False(facts.HasDuplicateRisk("score"));
    }

    // ── HasNullableRisk ───────────────────────────────────────────────────────

    [Fact]
    public void HasNullableRisk_DaRanWithRisk_ReturnsTrue()
    {
        var analysis = new DataAnalysisResult
        {
            Summary = "x",
            Risks   = [new DataAnalysisFinding { Category = "NullableRisk", Severity = "WARNING", Description = "'name' is nullable but appears important" }],
        };
        var facts = DeterministicFactSet.Build(MakeRequest(analysis: analysis));
        Assert.True(facts.HasNullableRisk("name"));
    }

    [Fact]
    public void HasNullableRisk_DaRanNoRisk_ReturnsFalse()
    {
        var analysis = new DataAnalysisResult { Summary = "x", Risks = [] };
        var facts    = DeterministicFactSet.Build(MakeRequest(analysis: analysis));
        Assert.False(facts.HasNullableRisk("name"));
    }

    // ── IsNotRecommendedKey ───────────────────────────────────────────────────

    [Fact]
    public void IsNotRecommendedKey_DaFindingPresent_ReturnsTrue()
    {
        var analysis = new DataAnalysisResult
        {
            Summary  = "x",
            Findings = [new DataAnalysisFinding
            {
                Category    = "CandidateKey",
                Severity    = "INFO",
                Description = "'score' is sample-unique but not recommended as a key",
                Detail      = "It is not recommended because it is a numeric value column.",
            }],
        };
        var facts = DeterministicFactSet.Build(MakeRequest(analysis: analysis));
        Assert.True(facts.IsNotRecommendedKey("score"));
    }

    [Fact]
    public void IsNotRecommendedKey_NeitherFindingNorDa_ReturnsFalse()
    {
        var facts = DeterministicFactSet.Build(MakeRequest());
        Assert.False(facts.IsNotRecommendedKey("score"));
    }

    // ── IsNumericValueColumn ──────────────────────────────────────────────────

    [Fact]
    public void IsNumericValueColumn_DetailContainsPhrase_ReturnsTrue()
    {
        var analysis = new DataAnalysisResult
        {
            Summary  = "x",
            Findings = [new DataAnalysisFinding
            {
                Category = "CandidateKey",
                Severity = "INFO",
                Description = "'score' is sample-unique but not recommended as a key",
                Detail      = "It is not recommended because it is a numeric value column.",
            }],
        };
        var facts = DeterministicFactSet.Build(MakeRequest(analysis: analysis));
        Assert.True(facts.IsNumericValueColumn("score"));
    }

    [Fact]
    public void IsNumericValueColumn_DetailAbsent_ReturnsFalse()
    {
        var analysis = new DataAnalysisResult
        {
            Summary  = "x",
            Findings = [new DataAnalysisFinding
            {
                Category    = "CandidateKey",
                Severity    = "INFO",
                Description = "'score' is sample-unique",
            }],
        };
        var facts = DeterministicFactSet.Build(MakeRequest(analysis: analysis));
        Assert.False(facts.IsNumericValueColumn("score"));
    }

    // ── RecommendedPrimaryKey ─────────────────────────────────────────────────

    [Fact]
    public void RecommendedPrimaryKey_ExtractedFromDaRecommendation()
    {
        var analysis = new DataAnalysisResult
        {
            Summary = "x",
            Recommendations = [new DataAnalysisRecommendation
            {
                Priority    = "HIGH",
                Type        = "PrimaryKey",
                Description = "Designate 'id' as the PRIMARY KEY — it is the strongest candidate.",
            }],
        };
        var facts = DeterministicFactSet.Build(MakeRequest(analysis: analysis));
        Assert.Equal("id", facts.RecommendedPrimaryKey);
    }

    [Fact]
    public void RecommendedPrimaryKey_SurrogateKeyDescription_ReturnsNull()
    {
        var analysis = new DataAnalysisResult
        {
            Summary = "x",
            Recommendations = [new DataAnalysisRecommendation
            {
                Priority    = "HIGH",
                Type        = "PrimaryKey",
                Description = "Add a surrogate primary key — no natural unique identifier was found.",
            }],
        };
        var facts = DeterministicFactSet.Build(MakeRequest(analysis: analysis));
        Assert.Null(facts.RecommendedPrimaryKey);
    }

    [Fact]
    public void RecommendedPrimaryKey_NoDa_ReturnsNull()
    {
        var facts = DeterministicFactSet.Build(MakeRequest());
        Assert.Null(facts.RecommendedPrimaryKey);
    }

    // ── HasNullabilityEvidence ────────────────────────────────────────────────

    [Fact]
    public void HasNullabilityEvidence_NullableColumn_ReturnsTrue()
    {
        var facts = DeterministicFactSet.Build(MakeRequest(columns: [Col("notes", PostgresType.Text, nullable: true)]));
        Assert.True(facts.HasNullabilityEvidence("notes"));
    }

    [Fact]
    public void HasNullabilityEvidence_NullInRows_ReturnsTrue()
    {
        var rows = new List<IReadOnlyDictionary<string, string?>>
        {
            new Dictionary<string, string?> { ["email"] = null },
        };
        var facts = DeterministicFactSet.Build(MakeRequest(
            columns: [Col("email", PostgresType.Text)], rows: rows));
        Assert.True(facts.HasNullabilityEvidence("email"));
    }

    [Fact]
    public void HasNullabilityEvidence_ValidationWarning_ReturnsTrue()
    {
        var warnings = new List<ValidationWarning>
        {
            new() { ColumnName = "email", Message = "Column has missing values", Severity = ValidationSeverity.Warning },
        };
        var facts = DeterministicFactSet.Build(MakeRequest(
            columns: [Col("email", PostgresType.Text)], warnings: warnings));
        Assert.True(facts.HasNullabilityEvidence("email"));
    }

    [Fact]
    public void HasNullabilityEvidence_NotNullNoEvidence_ReturnsFalse()
    {
        var facts = DeterministicFactSet.Build(MakeRequest(
            columns: [Col("active", PostgresType.Boolean)],
            rows: [new Dictionary<string, string?> { ["active"] = "FALSE" }]));
        Assert.False(facts.HasNullabilityEvidence("active"));
    }

    [Fact]
    public void HasNullabilityEvidence_UnknownColumn_ReturnsTrue()
    {
        var facts = DeterministicFactSet.Build(MakeRequest());
        Assert.True(facts.HasNullabilityEvidence("nonexistent"));
    }

    // ── HasDuplicateEvidence ──────────────────────────────────────────────────

    [Fact]
    public void HasDuplicateEvidence_ActualDuplicatesInRows_ReturnsTrue()
    {
        var rows = new List<IReadOnlyDictionary<string, string?>>
        {
            new Dictionary<string, string?> { ["id"] = "1" },
            new Dictionary<string, string?> { ["id"] = "1" },
        };
        var facts = DeterministicFactSet.Build(MakeRequest(
            columns: [Col("id", PostgresType.Integer)], rows: rows));
        Assert.True(facts.HasDuplicateEvidence("id"));
    }

    [Fact]
    public void HasDuplicateEvidence_ValidationWarning_ReturnsTrue()
    {
        var warnings = new List<ValidationWarning>
        {
            new() { ColumnName = "id", Message = "Duplicate values detected", Severity = ValidationSeverity.Warning },
        };
        var facts = DeterministicFactSet.Build(MakeRequest(
            columns: [Col("id", PostgresType.Integer)], warnings: warnings));
        Assert.True(facts.HasDuplicateEvidence("id"));
    }

    [Fact]
    public void HasDuplicateEvidence_NoEvidence_ReturnsFalse()
    {
        var rows = new List<IReadOnlyDictionary<string, string?>>
        {
            new Dictionary<string, string?> { ["id"] = "1" },
            new Dictionary<string, string?> { ["id"] = "2" },
        };
        var facts = DeterministicFactSet.Build(MakeRequest(
            columns: [Col("id", PostgresType.Integer)], rows: rows));
        Assert.False(facts.HasDuplicateEvidence("id"));
    }

    // ── ResolveColumn ─────────────────────────────────────────────────────────

    [Fact]
    public void ResolveColumn_ExplicitHintTakesPriority()
    {
        var facts = DeterministicFactSet.Build(MakeRequest(columns:
        [
            Col("id",    PostgresType.Integer),
            Col("score", PostgresType.Numeric),
        ]));
        var col = facts.ResolveColumn("id column has type risk", null, "score");
        Assert.Equal("score", col?.SnakeCaseName);
    }

    [Fact]
    public void ResolveColumn_FallsBackToWordBoundarySearch()
    {
        var facts = DeterministicFactSet.Build(MakeRequest(columns:
        [
            Col("id",    PostgresType.Integer),
            Col("score", PostgresType.Numeric),
        ]));
        var col = facts.ResolveColumn("score has type inference risk", null, null);
        Assert.Equal("score", col?.SnakeCaseName);
    }

    [Fact]
    public void ResolveColumn_ShortNameDoesNotMatchInsideLongerWord()
    {
        // "id" should not match inside "candidate"
        var facts = DeterministicFactSet.Build(MakeRequest(columns: [Col("id", PostgresType.Integer)]));
        var col = facts.ResolveColumn("candidate key recommendation", null, null);
        Assert.Null(col);
    }
}
