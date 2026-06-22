using DataMigrationAssistant.Core.Models;
using DataMigrationAssistant.Core.Services;

namespace DataMigrationAssistant.Core.Tests;

public sealed class SeedSqlGeneratorServiceTests
{
    private readonly SeedSqlGeneratorService _sut = new();

    // ── Structure ──────────────────────────────────────────────────────────────

    [Fact]
    public void GenerateSeed_ContainsInsertIntoTableName()
    {
        var sql = Run("users", [("id", PostgresType.Integer)], [["1"]]);
        Assert.Contains("INSERT INTO users", sql);
    }

    [Fact]
    public void GenerateSeed_ContainsColumnList()
    {
        var sql = Run("t", [("user_id", PostgresType.Integer), ("email", PostgresType.Text)], [["1", "a@b.com"]]);
        Assert.Contains("(user_id, email)", sql);
    }

    [Fact]
    public void GenerateSeed_ContainsValues()
    {
        var sql = Run("t", [("id", PostgresType.Integer)], [["1"]]);
        Assert.Contains("VALUES", sql);
    }

    [Fact]
    public void GenerateSeed_EndsWithOnConflictDoNothing()
    {
        var sql = Run("t", [("id", PostgresType.Integer)], [["1"]]);
        Assert.EndsWith("ON CONFLICT DO NOTHING;", sql.TrimEnd());
    }

    [Fact]
    public void GenerateSeed_EmptyRows_ReturnsComment()
    {
        var sql = Run("t", [("id", PostgresType.Integer)], []);
        Assert.DoesNotContain("INSERT INTO", sql);
        Assert.StartsWith("--", sql.TrimStart());
    }

    // ── Multi-row ──────────────────────────────────────────────────────────────

    [Fact]
    public void GenerateSeed_MultipleRows_AllInOneInsert()
    {
        var sql = Run("t",
            [("id", PostgresType.Integer)],
            [["1"], ["2"], ["3"]]);

        // Only one INSERT statement
        Assert.Equal(1, CountOccurrences(sql, "INSERT INTO"));
        Assert.Contains("(1)", sql);
        Assert.Contains("(2)", sql);
        Assert.Contains("(3)", sql);
    }

    [Fact]
    public void GenerateSeed_MultipleRows_RowsSeparatedByCommas()
    {
        var sql = Run("t",
            [("id", PostgresType.Integer)],
            [["1"], ["2"], ["3"]]);

        // All but last row end with comma
        var lines = sql.Split('\n');
        var valueLines = lines.Where(l => l.TrimStart().StartsWith("(")).ToList();

        Assert.Equal(3, valueLines.Count);
        Assert.EndsWith(",", valueLines[0].TrimEnd());
        Assert.EndsWith(",", valueLines[1].TrimEnd());
        Assert.DoesNotMatch(@",\s*$", valueLines[2].TrimEnd() + " ");
        Assert.False(valueLines[2].TrimEnd().EndsWith(','));
    }

    // ── NULL values ────────────────────────────────────────────────────────────

    [Fact]
    public void GenerateSeed_NullValue_EmitsNull()
    {
        var sql = Run("t", [("name", PostgresType.Text)], [[null]]);
        Assert.Contains("(NULL)", sql);
    }

    [Fact]
    public void GenerateSeed_EmptyString_EmitsNull()
    {
        var sql = Run("t", [("name", PostgresType.Text)], [[string.Empty]]);
        Assert.Contains("(NULL)", sql);
    }

    [Fact]
    public void GenerateSeed_WhitespaceString_EmitsNull()
    {
        var sql = Run("t", [("name", PostgresType.Text)], [["   "]]);
        Assert.Contains("(NULL)", sql);
    }

    // ── String escaping ────────────────────────────────────────────────────────

    [Fact]
    public void GenerateSeed_StringWithSingleQuote_IsEscaped()
    {
        var sql = Run("t", [("name", PostgresType.Text)], [["Alice's cat"]]);
        Assert.Contains("'Alice''s cat'", sql);
    }

    [Fact]
    public void GenerateSeed_StringWithMultipleQuotes_AllEscaped()
    {
        var sql = Run("t", [("note", PostgresType.Text)], [["it's the user's data"]]);
        Assert.Contains("'it''s the user''s data'", sql);
    }

    [Fact]
    public void GenerateSeed_PlainString_IsQuoted()
    {
        var sql = Run("t", [("name", PostgresType.Text)], [["Alice"]]);
        Assert.Contains("'Alice'", sql);
    }

    // ── Numeric values ─────────────────────────────────────────────────────────

    [Fact]
    public void GenerateSeed_IntegerValue_NotQuoted()
    {
        var sql = Run("t", [("age", PostgresType.Integer)], [["42"]]);
        Assert.Contains("(42)", sql);
        Assert.DoesNotContain("'42'", sql);
    }

    [Fact]
    public void GenerateSeed_NegativeInteger_NotQuoted()
    {
        var sql = Run("t", [("balance", PostgresType.Integer)], [["-5"]]);
        Assert.Contains("(-5)", sql);
    }

    [Fact]
    public void GenerateSeed_BigIntValue_NotQuoted()
    {
        var sql = Run("t", [("big_id", PostgresType.BigInt)], [["9999999999"]]);
        Assert.Contains("(9999999999)", sql);
        Assert.DoesNotContain("'9999999999'", sql);
    }

    [Fact]
    public void GenerateSeed_NumericValue_NotQuoted()
    {
        var sql = Run("t", [("price", PostgresType.Numeric)], [["3.14"]]);
        Assert.Contains("(3.14)", sql);
        Assert.DoesNotContain("'3.14'", sql);
    }

    // ── Boolean values ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("true")]
    [InlineData("TRUE")]
    [InlineData("yes")]
    [InlineData("YES")]
    [InlineData("y")]
    [InlineData("t")]
    public void GenerateSeed_TrueBooleanVariants_EmitsTRUE(string value)
    {
        var sql = Run("t", [("active", PostgresType.Boolean)], [[value]]);
        Assert.Contains("(TRUE)", sql);
    }

    [Theory]
    [InlineData("false")]
    [InlineData("FALSE")]
    [InlineData("no")]
    [InlineData("NO")]
    [InlineData("n")]
    [InlineData("f")]
    public void GenerateSeed_FalseBooleanVariants_EmitsFALSE(string value)
    {
        var sql = Run("t", [("active", PostgresType.Boolean)], [[value]]);
        Assert.Contains("(FALSE)", sql);
    }

    // ── Date / Timestamp ───────────────────────────────────────────────────────

    [Fact]
    public void GenerateSeed_DateValue_FormattedIso()
    {
        var sql = Run("t", [("dob", PostgresType.Date)], [["2023-01-15"]]);
        Assert.Contains("'2023-01-15'", sql);
    }

    [Fact]
    public void GenerateSeed_TimestampWithTime_FormattedIso()
    {
        var sql = Run("t", [("created_at", PostgresType.Timestamp)], [["2023-01-15 10:30:00"]]);
        Assert.Contains("'2023-01-15 10:30:00'", sql);
    }

    [Fact]
    public void GenerateSeed_TimestampIso8601_NormalisedToSpaceSeparator()
    {
        var sql = Run("t", [("created_at", PostgresType.Timestamp)], [["2023-01-15T10:30:00"]]);
        Assert.Contains("'2023-01-15 10:30:00'", sql);
    }

    // ── Safety: no destructive SQL ─────────────────────────────────────────────

    [Fact]
    public void GenerateSeed_NeverContainsDestructiveKeywords()
    {
        var sql = Run("t", [("id", PostgresType.Integer)], [["1"]]);
        Assert.DoesNotContain("DROP",     sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TRUNCATE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE",   sql, StringComparison.OrdinalIgnoreCase);
    }

    // ── Snake_case column names ────────────────────────────────────────────────

    [Fact]
    public void GenerateSeed_SnakeCaseColumnNames_UsedInColumnList()
    {
        var sql = Run("t",
            [("first_name", PostgresType.Text), ("last_name", PostgresType.Text)],
            [["Alice", "Smith"]]);

        Assert.Contains("(first_name, last_name)", sql);
    }

    // ── Mixed column types in one row ──────────────────────────────────────────

    [Fact]
    public void GenerateSeed_MixedTypes_CorrectFormattingPerColumn()
    {
        var sql = Run("users",
            [
                ("id",         PostgresType.Integer),
                ("name",       PostgresType.Text),
                ("score",      PostgresType.Numeric),
                ("active",     PostgresType.Boolean),
                ("joined",     PostgresType.Date),
            ],
            [["1", "Bob", "9.5", "true", "2023-06-01"]]);

        Assert.Contains("1",            sql);
        Assert.Contains("'Bob'",        sql);
        Assert.Contains("9.5",          sql);
        Assert.Contains("TRUE",         sql);
        Assert.Contains("'2023-06-01'", sql);
        // Numeric and integer values should not be quoted
        Assert.DoesNotContain("'1'",   sql);
        Assert.DoesNotContain("'9.5'", sql);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private string Run(
        string tableName,
        (string name, PostgresType type)[] columns,
        string?[][] rows)
    {
        var (preview, schema) = Build(tableName, columns, rows);
        return _sut.GenerateSeed(preview, schema);
    }

    private static (SheetPreview Preview, TableSchema Schema) Build(
        string tableName,
        (string name, PostgresType type)[] columns,
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
            SheetName    = tableName,
            FilePath     = "/test.xlsx",
            Columns      = colInfos,
            Rows         = rowDicts,
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
                IsCandidateKey = false,
            })
            .ToList();

        var schema = new TableSchema
        {
            TableName     = tableName,
            SheetName     = tableName,
            Columns       = colSchemas,
            SampleRowCount = rows.Length,
        };

        return (preview, schema);
    }

    // ── More than 10 rows (full data load scenario) ───────────────────────────

    [Fact]
    public void GenerateSeed_15Rows_AllRowsIncludedInSql()
    {
        var rows   = Enumerable.Range(1, 15).Select(i => new string?[] { i.ToString() }).ToArray();
        var sql    = Run("t", [("id", PostgresType.Integer)], rows);

        for (int i = 1; i <= 15; i++)
            Assert.Contains($"({i})", sql);
    }

    [Fact]
    public void GenerateSeed_15Rows_SingleInsertStatement()
    {
        var rows = Enumerable.Range(1, 15).Select(i => new string?[] { i.ToString() }).ToArray();
        var sql  = Run("t", [("id", PostgresType.Integer)], rows);

        Assert.Equal(1, CountOccurrences(sql, "INSERT INTO"));
    }

    private static int CountOccurrences(string text, string pattern) =>
        (text.Length - text.Replace(pattern, string.Empty).Length) / pattern.Length;
}
