using System.Globalization;
using DataMigrationAssistant.Core.Generators;
using DataMigrationAssistant.Core.Inference;
using DataMigrationAssistant.Core.Models;
using DataMigrationAssistant.Core.Services;
using DataMigrationAssistant.Core.Utilities;

namespace DataMigrationAssistant.Core.Tests;

public sealed class NumericParsingTests
{
    // ── NumericParser.TryParseDecimal ──────────────────────────────────────────

    [Theory]
    [InlineData("9.5",    "9.5")]
    [InlineData("4.5",    "4.5")]
    [InlineData("3.14",   "3.14")]
    [InlineData("-1.5",  "-1.5")]
    [InlineData("0.001",  "0.001")]
    public void TryParseDecimal_DotDecimal_ParsesCorrectly(string input, string expectedString)
    {
        Assert.True(NumericParser.TryParseDecimal(input, out var result));
        Assert.Equal(expectedString, result.ToString(CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("9,5",   "9.5")]
    [InlineData("4,5",   "4.5")]
    [InlineData("3,14",  "3.14")]
    [InlineData("-1,5",  "-1.5")]
    public void TryParseDecimal_CommaDecimal_ParsesCorrectly(string input, string expectedString)
    {
        Assert.True(NumericParser.TryParseDecimal(input, out var result));
        Assert.Equal(expectedString, result.ToString(CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("42")]
    [InlineData("-7")]
    [InlineData("0")]
    public void TryParseDecimal_WholeNumbers_ParseCorrectly(string input)
    {
        Assert.True(NumericParser.TryParseDecimal(input, out var result));
        Assert.Equal(decimal.Parse(input, CultureInfo.InvariantCulture), result);
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("abc")]
    [InlineData("N/A")]
    [InlineData("not-a-number")]
    public void TryParseDecimal_NonNumericStrings_ReturnsFalse(string input)
        => Assert.False(NumericParser.TryParseDecimal(input, out _));

    // ── TypeInferrer: comma decimals classified as Numeric ─────────────────────

    [Theory]
    [InlineData("9,5")]
    [InlineData("4,5")]
    [InlineData("3,14")]
    [InlineData("-1,5")]
    public void ClassifyValue_CommaSeparatedDecimal_ReturnsNumeric(string value)
        => Assert.Equal(PostgresType.Numeric, TypeInferrer.ClassifyValue(value));

    [Theory]
    [InlineData("9.5")]
    [InlineData("4.5")]
    [InlineData("3.14")]
    [InlineData("-1.5")]
    public void ClassifyValue_DotSeparatedDecimal_ReturnsNumeric(string value)
        => Assert.Equal(PostgresType.Numeric, TypeInferrer.ClassifyValue(value));

    [Theory]
    [InlineData("9,5",  PostgresType.Numeric)]
    [InlineData("9.5",  PostgresType.Numeric)]
    [InlineData("42",   PostgresType.Integer)]
    [InlineData("true", PostgresType.Boolean)]
    public void ClassifyValue_MixedCultureInput_ClassifiesCorrectly(string value, PostgresType expected)
        => Assert.Equal(expected, TypeInferrer.ClassifyValue(value));

    [Fact]
    public void InferColumnType_MixedDotAndCommaDecimals_ReturnsNumeric()
        => Assert.Equal(PostgresType.Numeric, TypeInferrer.InferColumnType(["9.5", "4,5", "3.14"]));

    [Fact]
    public void InferColumnType_AllCommaDecimals_ReturnsNumeric()
        => Assert.Equal(PostgresType.Numeric, TypeInferrer.InferColumnType(["9,5", "4,5"]));

    // ── SqlValueFormatter: comma → dot in SQL output ──────────────────────────

    [Theory]
    [InlineData("9,5",  "9.5")]
    [InlineData("4,5",  "4.5")]
    [InlineData("3,14", "3.14")]
    [InlineData("-1,5", "-1.5")]
    public void Format_CommaDecimal_EmitsDotInSql(string rawValue, string expected)
    {
        var result = SqlValueFormatter.Format(rawValue, PostgresType.Numeric);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("9.5",  "9.5")]
    [InlineData("4.5",  "4.5")]
    [InlineData("3.14", "3.14")]
    public void Format_DotDecimal_EmitsDotInSql(string rawValue, string expected)
    {
        var result = SqlValueFormatter.Format(rawValue, PostgresType.Numeric);
        Assert.Equal(expected, result);
    }

    // ── SeedSqlGeneratorService: end-to-end comma decimal → dot in SQL ────────

    [Fact]
    public void GenerateSeed_CommaDecimalValue_SqlUsesDot()
    {
        var sut = new SeedSqlGeneratorService();
        var sql = RunSeed(sut, "t", [("price", PostgresType.Numeric)], [["9,5"]]);
        Assert.Contains("9.5", sql);
        Assert.DoesNotContain("9,5", sql);
    }

    [Fact]
    public void GenerateSeed_MixedDecimalFormats_AllNormalisedToDot()
    {
        var sut = new SeedSqlGeneratorService();
        var sql = RunSeed(sut, "t", [("price", PostgresType.Numeric)], [["9,5"], ["4.5"]]);
        Assert.Contains("9.5", sql);
        Assert.Contains("4.5", sql);
        Assert.DoesNotContain("9,5", sql);
    }

    // ── UpsertSqlGeneratorService: comma decimal → dot in SQL ─────────────────

    [Fact]
    public void GenerateUpsert_CommaDecimalValue_SqlUsesDot()
    {
        var sut = new UpsertSqlGeneratorService();
        var result = RunUpsert(sut, "t",
            [("id", PostgresType.Integer, true), ("price", PostgresType.Numeric, false)],
            [["1", "9,5"]]);

        Assert.True(result.Success);
        Assert.Contains("9.5", result.Value!);
        Assert.DoesNotContain("9,5", result.Value!);
    }

    // ── SeedDiffService: "9,5" and "9.5" are equal across cultures ───────────

    [Fact]
    public void Diff_CommaDecimalInNewEquivalentToDotDecimalInOld_IsUnchanged()
    {
        var sut = new SeedDiffService();
        var result = sut.Diff(
            OldSeed("t", ["id", "score"], [["1", "9.5"]]),
            NewData(["id", "score"], [["1", "9,5"]]),
            Schema("t", [("id", true), ("score", false)]));

        Assert.True(result.Success);
        var row = result.Value!.Rows.Single();
        Assert.Equal(SeedDiffStatus.Unchanged, row.Status);
    }

    [Fact]
    public void Diff_DotDecimalInNewEquivalentToCommaDecimalInOld_IsUnchanged()
    {
        var sut = new SeedDiffService();
        var result = sut.Diff(
            OldSeed("t", ["id", "score"], [["1", "9,5"]]),
            NewData(["id", "score"], [["1", "9.5"]]),
            Schema("t", [("id", true), ("score", false)]));

        Assert.True(result.Success);
        var row = result.Value!.Rows.Single();
        Assert.Equal(SeedDiffStatus.Unchanged, row.Status);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static string RunSeed(
        SeedSqlGeneratorService sut,
        string tableName,
        (string name, PostgresType type)[] columns,
        string?[][] rows)
    {
        var cols = columns.Select(c => (c.name, c.type, false)).ToArray();
        var (preview, schema) = BuildPreviewAndSchema(tableName, cols, rows);
        return sut.GenerateSeed(preview, schema);
    }

    private static DataMigrationAssistant.Core.Results.ServiceResult<string> RunUpsert(
        UpsertSqlGeneratorService sut,
        string tableName,
        (string name, PostgresType type, bool candidateKey)[] columns,
        string?[][] rows)
    {
        var (preview, schema) = BuildPreviewAndSchema(tableName, columns, rows);
        return sut.GenerateUpsert(preview, schema);
    }

    private static (SheetPreview Preview, TableSchema Schema) BuildPreviewAndSchema(
        string tableName,
        (string name, PostgresType type, bool candidateKey)[] columns,
        string?[][] rows)
    {
        var colInfos = columns
            .Select((c, i) => new ColumnInfo { Index = i, Name = c.name, SnakeCaseName = c.name })
            .ToList();

        var rowDicts = rows
            .Select(row =>
            {
                var dict = new Dictionary<string, string?>();
                for (int i = 0; i < columns.Length && i < row.Length; i++)
                    dict[columns[i].name] = row[i];
                return (IReadOnlyDictionary<string, string?>)dict;
            })
            .ToList();

        var preview = new SheetPreview
        {
            SheetName     = tableName,
            FilePath      = "/test.xlsx",
            Columns       = colInfos,
            Rows          = rowDicts,
            TotalRowCount = rows.Length,
        };

        var colSchemas = columns
            .Select((c, i) => new ColumnSchema
            {
                Index          = i,
                Name           = c.name,
                SnakeCaseName  = c.name,
                InferredType   = c.type,
                IsNullable     = false,
                HasDuplicates  = false,
                IsCandidateKey = c.candidateKey,
            })
            .ToList();

        var schema = new TableSchema
        {
            TableName      = tableName,
            SheetName      = tableName,
            Columns        = colSchemas,
            SampleRowCount = rows.Length,
        };

        return (preview, schema);
    }

    private static SeedRecord OldSeed(string table, string[] columns, string?[][] rows) =>
        new()
        {
            TableName = table,
            Columns   = columns,
            Rows      = rows.Select(r => (IReadOnlyList<string?>)r.ToList()).ToList(),
        };

    private static SheetPreview NewData(string[] columns, string?[][] rows)
    {
        var colInfos = columns
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
            SheetName     = "sheet",
            FilePath      = "/test.xlsx",
            Columns       = colInfos,
            Rows          = rowDicts,
            TotalRowCount = rows.Length,
        };
    }

    private static TableSchema Schema(string name, (string col, bool isKey)[] columns) =>
        new()
        {
            TableName      = name,
            SheetName      = name,
            SampleRowCount = 1,
            Columns        = columns.Select((c, i) => new ColumnSchema
            {
                Index          = i,
                Name           = c.col,
                SnakeCaseName  = c.col,
                InferredType   = PostgresType.Text,
                IsNullable     = false,
                HasDuplicates  = false,
                IsCandidateKey = c.isKey,
            }).ToList(),
        };
}
