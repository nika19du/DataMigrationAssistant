using DataMigrationAssistant.Core.Models;
using DataMigrationAssistant.Core.Services;

namespace DataMigrationAssistant.Core.Tests;

public sealed class SchemaInferenceServiceTests
{
    private readonly SchemaInferenceService _sut = new();

    // ── TableSchema properties ─────────────────────────────────────────────────

    [Fact]
    public void InferSchema_TableNameIsSnakeCaseOfSheetName()
    {
        var preview = BuildPreview("User Accounts", ["id"], [["1"]]);
        var schema = _sut.InferSchema(preview);
        Assert.Equal("user_accounts", schema.TableName);
    }

    [Fact]
    public void InferSchema_SheetNameIsPreservedRaw()
    {
        var preview = BuildPreview("User Accounts", ["id"], [["1"]]);
        var schema = _sut.InferSchema(preview);
        Assert.Equal("User Accounts", schema.SheetName);
    }

    [Fact]
    public void InferSchema_SampleRowCountMatchesPreviewRows()
    {
        var preview = BuildPreview("s", ["a"], [["1"], ["2"], ["3"]]);
        var schema = _sut.InferSchema(preview);
        Assert.Equal(3, schema.SampleRowCount);
    }

    // ── IsNullable ─────────────────────────────────────────────────────────────

    [Fact]
    public void InferSchema_NoNullValues_IsNullableFalse()
    {
        var preview = BuildPreview("s", ["id"], [["1"], ["2"]]);
        Assert.False(_sut.InferSchema(preview).Columns[0].IsNullable);
    }

    [Fact]
    public void InferSchema_NullValuePresent_IsNullableTrue()
    {
        var preview = BuildPreview("s", ["name"], [["Alice"], [null]]);
        Assert.True(_sut.InferSchema(preview).Columns[0].IsNullable);
    }

    [Fact]
    public void InferSchema_EmptyStringTreatedAsNull_IsNullableTrue()
    {
        var preview = BuildPreview("s", ["name"], [["Alice"], [""]]);
        Assert.True(_sut.InferSchema(preview).Columns[0].IsNullable);
    }

    // ── HasDuplicates ──────────────────────────────────────────────────────────

    [Fact]
    public void InferSchema_UniqueNonNullValues_HasDuplicatesFalse()
    {
        var preview = BuildPreview("s", ["id"], [["1"], ["2"], ["3"]]);
        Assert.False(_sut.InferSchema(preview).Columns[0].HasDuplicates);
    }

    [Fact]
    public void InferSchema_DuplicateValues_HasDuplicatesTrue()
    {
        var preview = BuildPreview("s", ["status"], [["active"], ["active"], ["inactive"]]);
        Assert.True(_sut.InferSchema(preview).Columns[0].HasDuplicates);
    }

    // ── IsCandidateKey ─────────────────────────────────────────────────────────

    [Fact]
    public void InferSchema_UniqueNonNullColumn_IsCandidateKeyTrue()
    {
        var preview = BuildPreview("s", ["id"], [["1"], ["2"], ["3"]]);
        Assert.True(_sut.InferSchema(preview).Columns[0].IsCandidateKey);
    }

    [Fact]
    public void InferSchema_ColumnWithNulls_IsCandidateKeyFalse()
    {
        var preview = BuildPreview("s", ["id"], [["1"], [null]]);
        Assert.False(_sut.InferSchema(preview).Columns[0].IsCandidateKey);
    }

    [Fact]
    public void InferSchema_ColumnWithDuplicates_IsCandidateKeyFalse()
    {
        var preview = BuildPreview("s", ["code"], [["A"], ["A"], ["B"]]);
        Assert.False(_sut.InferSchema(preview).Columns[0].IsCandidateKey);
    }

    // ── InferredType ───────────────────────────────────────────────────────────

    [Fact]
    public void InferSchema_IntegerColumn_ReturnsIntegerType()
    {
        var preview = BuildPreview("s", ["age"], [["25"], ["30"], ["42"]]);
        Assert.Equal(PostgresType.Integer, _sut.InferSchema(preview).Columns[0].InferredType);
    }

    [Fact]
    public void InferSchema_MixedNumericColumn_ReturnsNumericType()
    {
        var preview = BuildPreview("s", ["price"], [["9"], ["14.99"], ["100"]]);
        Assert.Equal(PostgresType.Numeric, _sut.InferSchema(preview).Columns[0].InferredType);
    }

    [Fact]
    public void InferSchema_DateColumn_ReturnsDateType()
    {
        var preview = BuildPreview("s", ["dob"], [["1990-05-12"], ["2001-11-03"]]);
        Assert.Equal(PostgresType.Date, _sut.InferSchema(preview).Columns[0].InferredType);
    }

    [Fact]
    public void InferSchema_MixedStringColumn_ReturnsTextType()
    {
        var preview = BuildPreview("s", ["notes"], [["hello"], ["42"], ["2023-01-01"]]);
        Assert.Equal(PostgresType.Text, _sut.InferSchema(preview).Columns[0].InferredType);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static SheetPreview BuildPreview(string sheetName, string[] columns, string?[][] rowData)
    {
        var cols = columns
            .Select((name, i) => new ColumnInfo
            {
                Index = i,
                Name = name,
                SnakeCaseName = name.ToLowerInvariant().Replace(' ', '_'),
            })
            .ToList();

        var rows = rowData
            .Select(row =>
            {
                var dict = new Dictionary<string, string?>();
                for (int i = 0; i < cols.Count && i < row.Length; i++)
                    dict[cols[i].SnakeCaseName] = row[i];
                return (IReadOnlyDictionary<string, string?>)dict;
            })
            .ToList();

        return new SheetPreview
        {
            SheetName = sheetName,
            FilePath = "/test.xlsx",
            Columns = cols,
            Rows = rows,
            TotalRowCount = rows.Count,
        };
    }
}
