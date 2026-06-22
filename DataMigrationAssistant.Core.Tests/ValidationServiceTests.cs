using DataMigrationAssistant.Core.Models;
using DataMigrationAssistant.Core.Services;

namespace DataMigrationAssistant.Core.Tests;

public sealed class ValidationServiceTests
{
    private readonly ValidationService _sut = new();

    // ── EMPTY_SHEET ────────────────────────────────────────────────────────────

    [Fact]
    public void Validate_NoColumns_CannotProceed()
    {
        var preview = Preview("t", [], []);
        var schema  = Schema("t");

        var result = _sut.Validate(preview, schema);

        Assert.False(result.CanProceed);
    }

    [Fact]
    public void Validate_NoColumns_ReturnsEmptySheetCode()
    {
        var preview = Preview("t", [], []);
        var schema  = Schema("t");

        var result = _sut.Validate(preview, schema);

        Assert.Single(result.Warnings, w => w.Code == "EMPTY_SHEET");
    }

    [Fact]
    public void Validate_NoColumns_WarningMentionsSheetName()
    {
        var preview = Preview("MySheet", [], []);
        var schema  = Schema("my_sheet");

        var result = _sut.Validate(preview, schema);

        Assert.Contains("MySheet", result.Warnings[0].Message);
    }

    // ── CanProceed is true for all non-fatal conditions ───────────────────────

    [Fact]
    public void Validate_WithWarnings_CanProceedIsTrue()
    {
        // NO_CANDIDATE_KEY produces a warning but processing can continue
        var (preview, schema) = Build(
            "t",
            [("id", PostgresType.Integer, isKey: false, nullable: false, hasDups: true)],
            [["1"], ["1"]]);

        var result = _sut.Validate(preview, schema);

        Assert.True(result.CanProceed);
    }

    // ── NO_DATA_ROWS ──────────────────────────────────────────────────────────

    [Fact]
    public void Validate_HasColumnsNoRows_ReturnsNoDataRowsWarning()
    {
        var preview = Preview("t", ["id"], []);
        var schema  = Schema("t", ("id", PostgresType.Integer, isKey: false, nullable: false, hasDups: false));

        var result = _sut.Validate(preview, schema);

        Assert.Contains(result.Warnings, w => w.Code == "NO_DATA_ROWS");
    }

    [Fact]
    public void Validate_HasColumnsAndRows_NoNoDataRowsWarning()
    {
        var (preview, schema) = Build(
            "t",
            [("id", PostgresType.Integer, isKey: true, nullable: false, hasDups: false)],
            [["1"]]);

        var result = _sut.Validate(preview, schema);

        Assert.DoesNotContain(result.Warnings, w => w.Code == "NO_DATA_ROWS");
    }

    // ── NO_CANDIDATE_KEY ──────────────────────────────────────────────────────

    [Fact]
    public void Validate_NoCandidateKey_ReturnsWarning()
    {
        var (preview, schema) = Build(
            "t",
            [("name", PostgresType.Text, isKey: false, nullable: false, hasDups: true)],
            [["Alice"], ["Alice"]]);

        var result = _sut.Validate(preview, schema);

        Assert.Contains(result.Warnings, w => w.Code == "NO_CANDIDATE_KEY");
    }

    [Fact]
    public void Validate_HasCandidateKey_NoNoCandidateKeyWarning()
    {
        var (preview, schema) = Build(
            "t",
            [("id", PostgresType.Integer, isKey: true, nullable: false, hasDups: false)],
            [["1"], ["2"]]);

        var result = _sut.Validate(preview, schema);

        Assert.DoesNotContain(result.Warnings, w => w.Code == "NO_CANDIDATE_KEY");
    }

    // ── MULTIPLE_CANDIDATE_KEYS ───────────────────────────────────────────────

    [Fact]
    public void Validate_MultipleCandidateKeys_ReturnsInfoWarning()
    {
        var (preview, schema) = Build(
            "t",
            [
                ("id",    PostgresType.Integer, isKey: true, nullable: false, hasDups: false),
                ("email", PostgresType.Text,    isKey: true, nullable: false, hasDups: false),
            ],
            [["1", "a@b.com"], ["2", "c@d.com"]]);

        var result = _sut.Validate(preview, schema);

        Assert.Contains(result.Warnings, w => w.Code == "MULTIPLE_CANDIDATE_KEYS");
    }

    [Fact]
    public void Validate_MultipleCandidateKeys_WarningMentionsFirstKey()
    {
        var (preview, schema) = Build(
            "t",
            [
                ("id",    PostgresType.Integer, isKey: true, nullable: false, hasDups: false),
                ("email", PostgresType.Text,    isKey: true, nullable: false, hasDups: false),
            ],
            [["1", "a@b.com"], ["2", "c@d.com"]]);

        var result  = _sut.Validate(preview, schema);
        var warning = result.Warnings.Single(w => w.Code == "MULTIPLE_CANDIDATE_KEYS");

        Assert.Contains("id", warning.Message);
    }

    [Fact]
    public void Validate_SingleCandidateKey_NoMultipleCandidateKeysWarning()
    {
        var (preview, schema) = Build(
            "t",
            [("id", PostgresType.Integer, isKey: true, nullable: false, hasDups: false)],
            [["1"], ["2"]]);

        var result = _sut.Validate(preview, schema);

        Assert.DoesNotContain(result.Warnings, w => w.Code == "MULTIPLE_CANDIDATE_KEYS");
    }

    // ── ALL_NULL_COLUMN ───────────────────────────────────────────────────────

    [Fact]
    public void Validate_AllNullColumn_ReturnsWarning()
    {
        var (preview, schema) = Build(
            "t",
            [
                ("id",   PostgresType.Integer, isKey: true,  nullable: false, hasDups: false),
                ("note", PostgresType.Text,    isKey: false, nullable: true,  hasDups: false),
            ],
            [["1", null], ["2", null], ["3", null]]);

        var result = _sut.Validate(preview, schema);

        Assert.Contains(result.Warnings, w => w.Code == "ALL_NULL_COLUMN" && w.ColumnName == "note");
    }

    [Fact]
    public void Validate_AllNullColumn_WarningMentionsColumnName()
    {
        var (preview, schema) = Build(
            "t",
            [
                ("id",   PostgresType.Integer, isKey: true,  nullable: false, hasDups: false),
                ("note", PostgresType.Text,    isKey: false, nullable: true,  hasDups: false),
            ],
            [["1", null], ["2", null]]);

        var result  = _sut.Validate(preview, schema);
        var warning = result.Warnings.Single(w => w.Code == "ALL_NULL_COLUMN");

        Assert.Contains("note", warning.Message);
    }

    [Fact]
    public void Validate_ColumnHasSomeNonNullValues_NoAllNullWarning()
    {
        var (preview, schema) = Build(
            "t",
            [
                ("id",   PostgresType.Integer, isKey: true,  nullable: false, hasDups: false),
                ("note", PostgresType.Text,    isKey: false, nullable: true,  hasDups: false),
            ],
            [["1", "hello"], ["2", null]]);

        var result = _sut.Validate(preview, schema);

        Assert.DoesNotContain(result.Warnings, w => w.Code == "ALL_NULL_COLUMN");
    }

    // ── HIGH_NULL_RATIO ───────────────────────────────────────────────────────

    [Fact]
    public void Validate_ColumnAtNullRatioThreshold_ReturnsWarning()
    {
        // 1 non-null, 1 null → 50% null, exactly at threshold
        var (preview, schema) = Build(
            "t",
            [
                ("id",    PostgresType.Integer, isKey: true,  nullable: false, hasDups: false),
                ("score", PostgresType.Numeric, isKey: false, nullable: true,  hasDups: false),
            ],
            [["1", "9.5"], ["2", null]]);

        var result = _sut.Validate(preview, schema);

        Assert.Contains(result.Warnings, w => w.Code == "HIGH_NULL_RATIO" && w.ColumnName == "score");
    }

    [Fact]
    public void Validate_ColumnAboveNullRatioThreshold_ReturnsWarning()
    {
        // 1 non-null, 2 null → 67% null
        var (preview, schema) = Build(
            "t",
            [
                ("id",    PostgresType.Integer, isKey: true,  nullable: false, hasDups: false),
                ("score", PostgresType.Numeric, isKey: false, nullable: true,  hasDups: false),
            ],
            [["1", "9.5"], ["2", null], ["3", null]]);

        var result = _sut.Validate(preview, schema);

        Assert.Contains(result.Warnings, w => w.Code == "HIGH_NULL_RATIO" && w.ColumnName == "score");
    }

    [Fact]
    public void Validate_ColumnBelowNullRatioThreshold_NoHighNullRatioWarning()
    {
        // 2 non-null, 1 null → 33% null
        var (preview, schema) = Build(
            "t",
            [
                ("id",    PostgresType.Integer, isKey: true,  nullable: false, hasDups: false),
                ("score", PostgresType.Numeric, isKey: false, nullable: true,  hasDups: false),
            ],
            [["1", "9.5"], ["2", "8.0"], ["3", null]]);

        var result = _sut.Validate(preview, schema);

        Assert.DoesNotContain(result.Warnings, w => w.Code == "HIGH_NULL_RATIO");
    }

    [Fact]
    public void Validate_HighNullRatioWarning_MentionsNullCount()
    {
        var (preview, schema) = Build(
            "t",
            [
                ("id",    PostgresType.Integer, isKey: true,  nullable: false, hasDups: false),
                ("score", PostgresType.Numeric, isKey: false, nullable: true,  hasDups: false),
            ],
            [["1", null], ["2", null], ["3", "9.5"]]);

        var result  = _sut.Validate(preview, schema);
        var warning = result.Warnings.Single(w => w.Code == "HIGH_NULL_RATIO");

        Assert.Contains("2", warning.Message); // 2 nulls out of 3
        Assert.Contains("3", warning.Message); // total rows
    }

    // ── DUPLICATE_VALUES ──────────────────────────────────────────────────────

    [Fact]
    public void Validate_NonNullableIntegerColumnWithDuplicates_ReturnsWarning()
    {
        var (preview, schema) = Build(
            "t",
            [("id", PostgresType.Integer, isKey: false, nullable: false, hasDups: true)],
            [["1"], ["1"]]);

        var result = _sut.Validate(preview, schema);

        Assert.Contains(result.Warnings, w => w.Code == "DUPLICATE_VALUES" && w.ColumnName == "id");
    }

    [Fact]
    public void Validate_NullableIntegerColumnWithDuplicates_NoDuplicateValuesWarning()
    {
        // nullable column with duplicates — DUPLICATE_VALUES only fires for non-nullable
        var (preview, schema) = Build(
            "t",
            [("id", PostgresType.Integer, isKey: false, nullable: true, hasDups: true)],
            [["1"], ["1"]]);

        var result = _sut.Validate(preview, schema);

        Assert.DoesNotContain(result.Warnings, w => w.Code == "DUPLICATE_VALUES");
    }

    [Fact]
    public void Validate_NonNullableTextColumnWithDuplicates_NoDuplicateValuesWarning()
    {
        // Text column with duplicates is normal (e.g., a status column); only warn for numeric types
        var (preview, schema) = Build(
            "t",
            [("status", PostgresType.Text, isKey: false, nullable: false, hasDups: true)],
            [["active"], ["active"]]);

        var result = _sut.Validate(preview, schema);

        Assert.DoesNotContain(result.Warnings, w => w.Code == "DUPLICATE_VALUES");
    }

    [Fact]
    public void Validate_NonNullableIntegerColumnWithoutDuplicates_NoDuplicateValuesWarning()
    {
        var (preview, schema) = Build(
            "t",
            [("id", PostgresType.Integer, isKey: true, nullable: false, hasDups: false)],
            [["1"], ["2"]]);

        var result = _sut.Validate(preview, schema);

        Assert.DoesNotContain(result.Warnings, w => w.Code == "DUPLICATE_VALUES");
    }

    // ── NULLABLE_KEY_CANDIDATE ────────────────────────────────────────────────

    [Fact]
    public void Validate_NullableIntegerColumnWithoutDuplicates_ReturnsInfo()
    {
        var (preview, schema) = Build(
            "t",
            [("id", PostgresType.Integer, isKey: false, nullable: true, hasDups: false)],
            [["1"], [null]]);

        var result = _sut.Validate(preview, schema);

        Assert.Contains(result.Warnings, w =>
            w.Code == "NULLABLE_KEY_CANDIDATE"
            && w.ColumnName == "id"
            && w.Severity == ValidationSeverity.Info);
    }

    [Fact]
    public void Validate_NonNullableIntegerColumnWithoutDuplicates_NoNullableKeyCandidateWarning()
    {
        var (preview, schema) = Build(
            "t",
            [("id", PostgresType.Integer, isKey: true, nullable: false, hasDups: false)],
            [["1"], ["2"]]);

        var result = _sut.Validate(preview, schema);

        Assert.DoesNotContain(result.Warnings, w => w.Code == "NULLABLE_KEY_CANDIDATE");
    }

    // ── MIXED_TYPES ───────────────────────────────────────────────────────────

    [Fact]
    public void Validate_TextColumnWithMajorityNumericValues_ReturnsMixedTypesWarning()
    {
        // 3 numeric, 1 text → 75% numeric → warning
        var (preview, schema) = Build(
            "t",
            [
                ("id",    PostgresType.Integer, isKey: true,  nullable: false, hasDups: false),
                ("value", PostgresType.Text,    isKey: false, nullable: false, hasDups: false),
            ],
            [["1", "10"], ["2", "20"], ["3", "30"], ["4", "N/A"]]);

        var result = _sut.Validate(preview, schema);

        Assert.Contains(result.Warnings, w => w.Code == "MIXED_TYPES" && w.ColumnName == "value");
    }

    [Fact]
    public void Validate_TextColumnAtNumericMixedThreshold_ReturnsMixedTypesWarning()
    {
        // 1 numeric, 1 text → 50% → exactly at threshold → warning
        var (preview, schema) = Build(
            "t",
            [
                ("id",    PostgresType.Integer, isKey: true,  nullable: false, hasDups: false),
                ("value", PostgresType.Text,    isKey: false, nullable: false, hasDups: false),
            ],
            [["1", "42"], ["2", "hello"]]);

        var result = _sut.Validate(preview, schema);

        Assert.Contains(result.Warnings, w => w.Code == "MIXED_TYPES");
    }

    [Fact]
    public void Validate_TextColumnWithAllNonNumericValues_NoMixedTypesWarning()
    {
        var (preview, schema) = Build(
            "t",
            [
                ("id",    PostgresType.Integer, isKey: true,  nullable: false, hasDups: false),
                ("label", PostgresType.Text,    isKey: false, nullable: false, hasDups: false),
            ],
            [["1", "hello"], ["2", "world"]]);

        var result = _sut.Validate(preview, schema);

        Assert.DoesNotContain(result.Warnings, w => w.Code == "MIXED_TYPES");
    }

    [Fact]
    public void Validate_TextColumnWithMinorityNumericValues_NoMixedTypesWarning()
    {
        // 1 numeric, 3 text → 25% → below threshold → no warning
        var (preview, schema) = Build(
            "t",
            [
                ("id",    PostgresType.Integer, isKey: true,  nullable: false, hasDups: false),
                ("label", PostgresType.Text,    isKey: false, nullable: false, hasDups: false),
            ],
            [["1", "42"], ["2", "hello"], ["3", "world"], ["4", "foo"]]);

        var result = _sut.Validate(preview, schema);

        Assert.DoesNotContain(result.Warnings, w => w.Code == "MIXED_TYPES");
    }

    [Fact]
    public void Validate_IntegerColumnWithAllNumericValues_NoMixedTypesWarning()
    {
        var (preview, schema) = Build(
            "t",
            [("id", PostgresType.Integer, isKey: true, nullable: false, hasDups: false)],
            [["1"], ["2"], ["3"]]);

        var result = _sut.Validate(preview, schema);

        Assert.DoesNotContain(result.Warnings, w => w.Code == "MIXED_TYPES");
    }

    // ── MIXED_DATE_FORMATS ────────────────────────────────────────────────────

    [Fact]
    public void Validate_DateColumnWithMultipleFormats_ReturnsMixedDateFormatsWarning()
    {
        // "2023-01-15" is yyyy-MM-dd; "15/01/2023" is dd/MM/yyyy — two distinct formats
        var (preview, schema) = Build(
            "t",
            [
                ("id",  PostgresType.Integer, isKey: true,  nullable: false, hasDups: false),
                ("dob", PostgresType.Date,    isKey: false, nullable: false, hasDups: false),
            ],
            [["1", "2023-01-15"], ["2", "15/01/2023"]]);

        var result = _sut.Validate(preview, schema);

        Assert.Contains(result.Warnings, w => w.Code == "MIXED_DATE_FORMATS" && w.ColumnName == "dob");
    }

    [Fact]
    public void Validate_DateColumnWithSingleFormat_NoMixedDateFormatsWarning()
    {
        var (preview, schema) = Build(
            "t",
            [
                ("id",  PostgresType.Integer, isKey: true,  nullable: false, hasDups: false),
                ("dob", PostgresType.Date,    isKey: false, nullable: false, hasDups: false),
            ],
            [["1", "2023-01-15"], ["2", "2024-06-20"], ["3", "2020-12-01"]]);

        var result = _sut.Validate(preview, schema);

        Assert.DoesNotContain(result.Warnings, w => w.Code == "MIXED_DATE_FORMATS");
    }

    [Fact]
    public void Validate_MixedDateFormatsWarning_MentionsFormats()
    {
        var (preview, schema) = Build(
            "t",
            [
                ("id",  PostgresType.Integer, isKey: true,  nullable: false, hasDups: false),
                ("dob", PostgresType.Date,    isKey: false, nullable: false, hasDups: false),
            ],
            [["1", "2023-01-15"], ["2", "15/01/2023"]]);

        var result  = _sut.Validate(preview, schema);
        var warning = result.Warnings.Single(w => w.Code == "MIXED_DATE_FORMATS");

        Assert.Contains("yyyy-MM-dd",  warning.Message);
        Assert.Contains("dd/MM/yyyy",  warning.Message);
    }

    // ── SUSPICIOUS_TYPE ───────────────────────────────────────────────────────

    [Fact]
    public void Validate_ColumnNamedIdInferredAsText_ReturnsSuspiciousTypeWarning()
    {
        var (preview, schema) = Build(
            "t",
            [("id", PostgresType.Text, isKey: false, nullable: false, hasDups: false)],
            [["abc"], ["def"]]);

        var result = _sut.Validate(preview, schema);

        Assert.Contains(result.Warnings, w => w.Code == "SUSPICIOUS_TYPE" && w.ColumnName == "id");
    }

    [Fact]
    public void Validate_ColumnNamedUserIdInferredAsText_ReturnsSuspiciousTypeWarning()
    {
        var (preview, schema) = Build(
            "t",
            [
                ("user_id", PostgresType.Text,    isKey: false, nullable: false, hasDups: false),
                ("name",    PostgresType.Text,    isKey: false, nullable: false, hasDups: false),
            ],
            [["abc", "Alice"], ["def", "Bob"]]);

        var result = _sut.Validate(preview, schema);

        Assert.Contains(result.Warnings, w => w.Code == "SUSPICIOUS_TYPE" && w.ColumnName == "user_id");
    }

    [Fact]
    public void Validate_ColumnNamedCreatedAtInferredAsText_ReturnsSuspiciousTypeWarning()
    {
        var (preview, schema) = Build(
            "t",
            [
                ("id",         PostgresType.Integer, isKey: true,  nullable: false, hasDups: false),
                ("created_at", PostgresType.Text,    isKey: false, nullable: false, hasDups: false),
            ],
            [["1", "not-a-date"], ["2", "also-not"]]);

        var result = _sut.Validate(preview, schema);

        Assert.Contains(result.Warnings, w => w.Code == "SUSPICIOUS_TYPE" && w.ColumnName == "created_at");
    }

    [Fact]
    public void Validate_ColumnNamedIdInferredAsInteger_NoSuspiciousTypeWarning()
    {
        var (preview, schema) = Build(
            "t",
            [("id", PostgresType.Integer, isKey: true, nullable: false, hasDups: false)],
            [["1"], ["2"]]);

        var result = _sut.Validate(preview, schema);

        Assert.DoesNotContain(result.Warnings, w => w.Code == "SUSPICIOUS_TYPE");
    }

    [Fact]
    public void Validate_RegularTextColumn_NoSuspiciousTypeWarning()
    {
        var (preview, schema) = Build(
            "t",
            [
                ("id",   PostgresType.Integer, isKey: true,  nullable: false, hasDups: false),
                ("name", PostgresType.Text,    isKey: false, nullable: false, hasDups: false),
            ],
            [["1", "Alice"], ["2", "Bob"]]);

        var result = _sut.Validate(preview, schema);

        Assert.DoesNotContain(result.Warnings, w => w.Code == "SUSPICIOUS_TYPE");
    }

    // ── No per-column checks without rows ─────────────────────────────────────

    [Fact]
    public void Validate_NoRows_PerColumnChecksSkipped()
    {
        // Column looks like it should be an integer ID but has type Text — would normally warn.
        // With no rows, per-column checks are skipped.
        var preview = Preview("t", ["id"], []);
        var schema  = Schema("t", ("id", PostgresType.Text, isKey: false, nullable: true, hasDups: false));

        var result = _sut.Validate(preview, schema);

        Assert.DoesNotContain(result.Warnings, w => w.Code == "SUSPICIOUS_TYPE");
        Assert.DoesNotContain(result.Warnings, w => w.Code == "ALL_NULL_COLUMN");
    }

    // ── Happy path: valid sheet ────────────────────────────────────────────────

    [Fact]
    public void Validate_CleanSheet_NoWarnings()
    {
        var (preview, schema) = Build(
            "users",
            [
                ("id",   PostgresType.Integer, isKey: true,  nullable: false, hasDups: false),
                ("name", PostgresType.Text,    isKey: false, nullable: false, hasDups: false),
            ],
            [["1", "Alice"], ["2", "Bob"], ["3", "Carol"]]);

        var result = _sut.Validate(preview, schema);

        Assert.True(result.CanProceed);
        Assert.Empty(result.Warnings);
    }

    // ── MULTIPLE_CANDIDATE_KEYS — quality labels, ranking, suggestions ────────

    [Fact]
    public void Validate_MultipleCandidateKeys_MessageIncludesQualityLabels()
    {
        var schema = SchemaWithQuality(
            "users",
            [
                ("id",       PostgresType.Integer, isKey: true, nullable: false, hasDups: false, quality: CandidateKeyQuality.Strong),
                ("username", PostgresType.Text,    isKey: true, nullable: false, hasDups: false, quality: CandidateKeyQuality.Plausible),
                ("score",    PostgresType.Numeric, isKey: true, nullable: false, hasDups: false, quality: CandidateKeyQuality.None),
            ]);
        var preview = PreviewFromSchema("users", schema, [["1", "alice", "95"], ["2", "bob", "87"]]);

        var result  = _sut.Validate(preview, schema);
        var warning = result.Warnings.Single(w => w.Code == "MULTIPLE_CANDIDATE_KEYS");

        Assert.Contains("(Strong)",           warning.Message);
        Assert.Contains("(Plausible)",        warning.Message);
        Assert.Contains("(not recommended)",  warning.Message);
    }

    [Fact]
    public void Validate_MultipleCandidateKeys_SelectedKeyFollowsQualityRanking()
    {
        // score is column index 0 (None quality) — id is index 1 (Strong); id should win
        var schema = SchemaWithQuality(
            "t",
            [
                ("score", PostgresType.Numeric, isKey: true, nullable: false, hasDups: false, quality: CandidateKeyQuality.None),
                ("id",    PostgresType.Integer, isKey: true, nullable: false, hasDups: false, quality: CandidateKeyQuality.Strong),
            ]);
        var preview = PreviewFromSchema("t", schema, [["95", "1"], ["87", "2"]]);

        var result  = _sut.Validate(preview, schema);
        var warning = result.Warnings.Single(w => w.Code == "MULTIPLE_CANDIDATE_KEYS");

        Assert.Contains("'id' will be selected", warning.Message);
    }

    [Fact]
    public void Validate_MultipleCandidateKeys_SuggestionIncludesPrimaryKeyRecommendation()
    {
        var schema = SchemaWithQuality(
            "users",
            [
                ("id",       PostgresType.Integer, isKey: true, nullable: false, hasDups: false, quality: CandidateKeyQuality.Strong),
                ("username", PostgresType.Text,    isKey: true, nullable: false, hasDups: false, quality: CandidateKeyQuality.Plausible),
                ("score",    PostgresType.Numeric, isKey: true, nullable: false, hasDups: false, quality: CandidateKeyQuality.None),
            ]);
        var preview = PreviewFromSchema("users", schema, [["1", "alice", "95"], ["2", "bob", "87"]]);

        var result  = _sut.Validate(preview, schema);
        var warning = result.Warnings.Single(w => w.Code == "MULTIPLE_CANDIDATE_KEYS");

        Assert.NotNull(warning.Suggestion);
        Assert.Contains("PRIMARY KEY", warning.Suggestion);
        Assert.Contains("id",          warning.Suggestion);
    }

    [Fact]
    public void Validate_MultipleCandidateKeys_SuggestionWarnsAgainstNumericKey()
    {
        var schema = SchemaWithQuality(
            "users",
            [
                ("id",    PostgresType.Integer, isKey: true, nullable: false, hasDups: false, quality: CandidateKeyQuality.Strong),
                ("score", PostgresType.Numeric, isKey: true, nullable: false, hasDups: false, quality: CandidateKeyQuality.None),
            ]);
        var preview = PreviewFromSchema("users", schema, [["1", "95"], ["2", "87"]]);

        var result  = _sut.Validate(preview, schema);
        var warning = result.Warnings.Single(w => w.Code == "MULTIPLE_CANDIDATE_KEYS");

        Assert.NotNull(warning.Suggestion);
        Assert.Contains("score",               warning.Suggestion);
        Assert.Contains("numeric value column", warning.Suggestion);
    }

    [Fact]
    public void Validate_NoCandidateKey_SuggestionIncludesSurrogateKeyAdvice()
    {
        var (preview, schema) = Build(
            "t",
            [("name", PostgresType.Text, isKey: false, nullable: false, hasDups: true)],
            [["Alice"], ["Alice"]]);

        var result  = _sut.Validate(preview, schema);
        var warning = result.Warnings.Single(w => w.Code == "NO_CANDIDATE_KEY");

        Assert.NotNull(warning.Suggestion);
        Assert.Contains("surrogate", warning.Suggestion, StringComparison.OrdinalIgnoreCase);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static SheetPreview Preview(string sheetName, string[] columns, string?[][] rows)
    {
        var cols = columns
            .Select((c, i) => new ColumnInfo { Index = i, Name = c, SnakeCaseName = c })
            .ToList();

        var rowDicts = rows
            .Select(row =>
            {
                var dict = new Dictionary<string, string?>();
                for (int i = 0; i < columns.Length && i < row.Length; i++)
                    dict[columns[i]] = row[i];
                return (IReadOnlyDictionary<string, string?>)dict;
            })
            .ToList();

        return new SheetPreview
        {
            SheetName     = sheetName,
            FilePath      = "/test.xlsx",
            Columns       = cols,
            Rows          = rowDicts,
            TotalRowCount = rows.Length,
        };
    }

    private static TableSchema Schema(
        string tableName,
        params (string col, PostgresType type, bool isKey, bool nullable, bool hasDups)[] columns) =>
        new()
        {
            TableName      = tableName,
            SheetName      = tableName,
            SampleRowCount = 0,
            Columns        = columns
                .Select((c, i) => new ColumnSchema
                {
                    Index          = i,
                    Name           = c.col,
                    SnakeCaseName  = c.col,
                    InferredType   = c.type,
                    IsNullable     = c.nullable,
                    HasDuplicates  = c.hasDups,
                    IsCandidateKey = c.isKey,
                })
                .ToList(),
        };

    private static (SheetPreview Preview, TableSchema Schema) Build(
        string tableName,
        (string col, PostgresType type, bool isKey, bool nullable, bool hasDups)[] columns,
        string?[][] rows)
    {
        var colNames = columns.Select(c => c.col).ToArray();
        var preview  = Preview(tableName, colNames, rows);
        var schema   = Schema(tableName, columns);
        return (preview, schema);
    }

    private static TableSchema SchemaWithQuality(
        string tableName,
        (string col, PostgresType type, bool isKey, bool nullable, bool hasDups, CandidateKeyQuality quality)[] columns) =>
        new()
        {
            TableName      = tableName,
            SheetName      = tableName,
            SampleRowCount = 0,
            Columns        = columns
                .Select((c, i) => new ColumnSchema
                {
                    Index               = i,
                    Name                = c.col,
                    SnakeCaseName       = c.col,
                    InferredType        = c.type,
                    IsNullable          = c.nullable,
                    HasDuplicates       = c.hasDups,
                    IsCandidateKey      = c.isKey,
                    CandidateKeyQuality = c.quality,
                })
                .ToList(),
        };

    private static SheetPreview PreviewFromSchema(string sheetName, TableSchema schema, string?[][] rows)
    {
        var colNames = schema.Columns.Select(c => c.SnakeCaseName).ToArray();
        return Preview(sheetName, colNames, rows);
    }
}
