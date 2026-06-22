using DataMigrationAssistant.Core.Models;
using DataMigrationAssistant.Core.Services;

namespace DataMigrationAssistant.Core.Tests;

public sealed class UpsertSqlGeneratorServiceTests
{
    private readonly UpsertSqlGeneratorService _sut = new();

    // ── No candidate key ───────────────────────────────────────────────────────

    [Fact]
    public void GenerateUpsert_NoCandidateKey_ReturnsFail()
    {
        var result = Run("t", [("name", PostgresType.Text, false)], [["Alice"]]);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("candidate key", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GenerateUpsert_NoCandidateKey_ErrorMentionsTableName()
    {
        var result = Run("my_table", [("name", PostgresType.Text, false)], [["Alice"]]);

        Assert.False(result.Success);
        Assert.Contains("my_table", result.Error);
    }

    // ── All columns are the conflict key ──────────────────────────────────────

    [Fact]
    public void GenerateUpsert_AllColumnsAreCandidateKeys_ReturnsFail()
    {
        var result = Run("t", [("id", PostgresType.Integer, true)], [["1"]]);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    // ── Empty rows ─────────────────────────────────────────────────────────────

    [Fact]
    public void GenerateUpsert_EmptyRows_ReturnsSuccessWithComment()
    {
        var result = Run("t",
            [("id", PostgresType.Integer, true), ("name", PostgresType.Text, false)],
            []);

        Assert.True(result.Success);
        Assert.StartsWith("--", result.Value!.TrimStart());
    }

    // ── SQL structure ──────────────────────────────────────────────────────────

    [Fact]
    public void GenerateUpsert_NormalCase_ContainsInsertIntoTableName()
    {
        var result = Run("users",
            [("id", PostgresType.Integer, true), ("name", PostgresType.Text, false)],
            [["1", "Alice"]]);

        Assert.True(result.Success);
        Assert.Contains("INSERT INTO users", result.Value!);
    }

    [Fact]
    public void GenerateUpsert_NormalCase_ContainsColumnList()
    {
        var result = Run("t",
            [("id", PostgresType.Integer, true), ("email", PostgresType.Text, false)],
            [["1", "a@b.com"]]);

        Assert.True(result.Success);
        Assert.Contains("(id, email)", result.Value!);
    }

    [Fact]
    public void GenerateUpsert_NormalCase_ContainsOnConflictWithKey()
    {
        var result = Run("t",
            [("user_id", PostgresType.Integer, true), ("name", PostgresType.Text, false)],
            [["1", "Alice"]]);

        Assert.True(result.Success);
        Assert.Contains("ON CONFLICT (user_id)", result.Value!);
    }

    [Fact]
    public void GenerateUpsert_NormalCase_ContainsDoUpdateSet()
    {
        var result = Run("t",
            [("id", PostgresType.Integer, true), ("name", PostgresType.Text, false)],
            [["1", "Alice"]]);

        Assert.True(result.Success);
        Assert.Contains("DO UPDATE SET", result.Value!);
    }

    [Fact]
    public void GenerateUpsert_NormalCase_EndsWithSemicolon()
    {
        var result = Run("t",
            [("id", PostgresType.Integer, true), ("name", PostgresType.Text, false)],
            [["1", "Alice"]]);

        Assert.True(result.Success);
        Assert.EndsWith(";", result.Value!.TrimEnd());
    }

    // ── Conflict key excluded from DO UPDATE SET ───────────────────────────────

    [Fact]
    public void GenerateUpsert_ConflictKeyNotInDoUpdateSet()
    {
        var result = Run("t",
            [("id", PostgresType.Integer, true), ("score", PostgresType.Numeric, false)],
            [["1", "9.5"]]);

        Assert.True(result.Success);
        Assert.DoesNotContain("id = EXCLUDED.id", result.Value!);
    }

    [Fact]
    public void GenerateUpsert_SingleNonKeyColumn_DoUpdateSetHasOnlyThatColumn()
    {
        var result = Run("t",
            [("id", PostgresType.Integer, true), ("name", PostgresType.Text, false)],
            [["1", "Alice"]]);

        Assert.True(result.Success);
        Assert.Contains("name = EXCLUDED.name", result.Value!);
        Assert.DoesNotContain("id = EXCLUDED.id", result.Value!);
    }

    [Fact]
    public void GenerateUpsert_MultipleNonKeyColumns_AllInDoUpdateSet()
    {
        var result = Run("t",
            [
                ("id",       PostgresType.Integer, true),
                ("username", PostgresType.Text,    false),
                ("score",    PostgresType.Numeric, false),
            ],
            [["1", "Alice", "9.5"]]);

        Assert.True(result.Success);
        var sql = result.Value!;
        Assert.Contains("username = EXCLUDED.username", sql);
        Assert.Contains("score = EXCLUDED.score",       sql);
        Assert.DoesNotContain("id = EXCLUDED.id",       sql);
    }

    // ── EXCLUDED references ────────────────────────────────────────────────────

    [Fact]
    public void GenerateUpsert_NonKeyColumns_UseExcludedPseudoTable()
    {
        var result = Run("t",
            [("id", PostgresType.Integer, true), ("email", PostgresType.Text, false)],
            [["1", "user@example.com"]]);

        Assert.True(result.Success);
        Assert.Contains("email = EXCLUDED.email", result.Value!);
    }

    // ── Nullable / NULL values ─────────────────────────────────────────────────

    [Fact]
    public void GenerateUpsert_NullValue_EmitsNull()
    {
        var result = Run("t",
            [("id", PostgresType.Integer, true), ("name", PostgresType.Text, false)],
            [["1", null]]);

        Assert.True(result.Success);
        Assert.Contains("NULL", result.Value!);
    }

    [Fact]
    public void GenerateUpsert_EmptyString_EmitsNull()
    {
        var result = Run("t",
            [("id", PostgresType.Integer, true), ("name", PostgresType.Text, false)],
            [["1", string.Empty]]);

        Assert.True(result.Success);
        Assert.Contains("NULL", result.Value!);
    }

    // ── String escaping ────────────────────────────────────────────────────────

    [Fact]
    public void GenerateUpsert_StringWithSingleQuote_IsEscaped()
    {
        var result = Run("t",
            [("id", PostgresType.Integer, true), ("note", PostgresType.Text, false)],
            [["1", "it's fine"]]);

        Assert.True(result.Success);
        Assert.Contains("'it''s fine'", result.Value!);
    }

    // ── Snake_case column names ────────────────────────────────────────────────

    [Fact]
    public void GenerateUpsert_SnakeCaseColumnsUsedEverywhere()
    {
        var result = Run("order_items",
            [("order_id", PostgresType.Integer, true), ("product_name", PostgresType.Text, false)],
            [["42", "Widget"]]);

        Assert.True(result.Success);
        var sql = result.Value!;
        Assert.Contains("INSERT INTO order_items (order_id, product_name)", sql);
        Assert.Contains("ON CONFLICT (order_id)",                           sql);
        Assert.Contains("product_name = EXCLUDED.product_name",             sql);
    }

    // ── Multi-row VALUES ───────────────────────────────────────────────────────

    [Fact]
    public void GenerateUpsert_MultipleRows_AllInOneInsert()
    {
        var result = Run("t",
            [("id", PostgresType.Integer, true), ("name", PostgresType.Text, false)],
            [["1", "Alice"], ["2", "Bob"], ["3", "Carol"]]);

        Assert.True(result.Success);
        var sql = result.Value!;
        Assert.Equal(1, sql.Split("INSERT INTO").Length - 1);
        Assert.Contains("(1, 'Alice')", sql);
        Assert.Contains("(2, 'Bob')",   sql);
        Assert.Contains("(3, 'Carol')", sql);
    }

    [Fact]
    public void GenerateUpsert_MultipleRows_RowsSeparatedByCommas()
    {
        var result = Run("t",
            [("id", PostgresType.Integer, true), ("name", PostgresType.Text, false)],
            [["1", "Alice"], ["2", "Bob"]]);

        Assert.True(result.Success);
        var lines      = result.Value!.Split('\n');
        var valueLines = lines.Where(l => l.TrimStart().StartsWith('(')).ToList();

        Assert.Equal(2, valueLines.Count);
        Assert.EndsWith(",", valueLines[0].TrimEnd());
        Assert.False(valueLines[1].TrimEnd().EndsWith(','));
    }

    // ── Safety: no destructive SQL ─────────────────────────────────────────────

    [Fact]
    public void GenerateUpsert_NeverContainsDestructiveKeywords()
    {
        var result = Run("t",
            [("id", PostgresType.Integer, true), ("name", PostgresType.Text, false)],
            [["1", "Alice"]]);

        Assert.True(result.Success);
        var sql = result.Value!;
        Assert.DoesNotContain("DROP",     sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TRUNCATE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE",   sql, StringComparison.OrdinalIgnoreCase);
    }

    // ── Full snapshot ──────────────────────────────────────────────────────────

    [Fact]
    public void GenerateUpsert_FullSchema_MatchesExpectedStructure()
    {
        var result = Run("users",
            [
                ("id",       PostgresType.Integer, true),
                ("username", PostgresType.Text,    false),
                ("score",    PostgresType.Numeric, false),
            ],
            [["1", "Alice", "9.5"], ["2", "Bob", null]]);

        Assert.True(result.Success);
        var sql = result.Value!;

        Assert.Contains("INSERT INTO users (id, username, score)",   sql);
        Assert.Contains("VALUES",                                    sql);
        Assert.Contains("(1, 'Alice', 9.5)",                         sql);
        Assert.Contains("(2, 'Bob', NULL)",                          sql);
        Assert.Contains("ON CONFLICT (id)",                          sql);
        Assert.Contains("DO UPDATE SET",                             sql);
        Assert.Contains("username = EXCLUDED.username",              sql);
        Assert.Contains("score = EXCLUDED.score",                    sql);
        Assert.DoesNotContain("id = EXCLUDED.id",                    sql);
    }

    // ── More than 10 rows (full data load scenario) ───────────────────────────

    [Fact]
    public void GenerateUpsert_15Rows_AllRowsIncludedInSql()
    {
        var rows = Enumerable.Range(1, 15)
            .Select(i => new string?[] { i.ToString(), $"User{i}" })
            .ToArray();

        var result = Run("t",
            [("id", PostgresType.Integer, true), ("name", PostgresType.Text, false)],
            rows);

        Assert.True(result.Success);
        for (int i = 1; i <= 15; i++)
            Assert.Contains($"({i}, 'User{i}')", result.Value!);
    }

    [Fact]
    public void GenerateUpsert_15Rows_SingleInsertStatement()
    {
        var rows = Enumerable.Range(1, 15)
            .Select(i => new string?[] { i.ToString(), $"User{i}" })
            .ToArray();

        var result = Run("t",
            [("id", PostgresType.Integer, true), ("name", PostgresType.Text, false)],
            rows);

        Assert.True(result.Success);
        Assert.Equal(1, result.Value!.Split("INSERT INTO").Length - 1);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private DataMigrationAssistant.Core.Results.ServiceResult<string> Run(
        string tableName,
        (string name, PostgresType type, bool candidateKey)[] columns,
        string?[][] rows)
    {
        var (preview, schema) = Build(tableName, columns, rows);
        return _sut.GenerateUpsert(preview, schema);
    }

    private static (SheetPreview Preview, TableSchema Schema) Build(
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
}
