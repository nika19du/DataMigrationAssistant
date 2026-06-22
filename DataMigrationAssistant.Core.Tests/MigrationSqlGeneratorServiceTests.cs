using DataMigrationAssistant.Core.Models;
using DataMigrationAssistant.Core.Services;

namespace DataMigrationAssistant.Core.Tests;

public sealed class MigrationSqlGeneratorServiceTests
{
    private readonly MigrationSqlGeneratorService _sut = new();

    // ── Failure: missing key column name ──────────────────────────────────────

    [Fact]
    public void GenerateMigration_EmptyKeyColumnName_ReturnsFail()
    {
        var diff   = Diff("users", string.Empty, []);
        var schema = Schema("users", ("id", PostgresType.Integer, true));

        var result = _sut.GenerateMigration(diff, schema);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void GenerateMigration_EmptyKeyColumnName_ErrorMentionsTableName()
    {
        var diff   = Diff("my_table", string.Empty, []);
        var schema = Schema("my_table", ("id", PostgresType.Integer, true));

        var result = _sut.GenerateMigration(diff, schema);

        Assert.False(result.Success);
        Assert.Contains("my_table", result.Error!);
    }

    // ── Summary header ─────────────────────────────────────────────────────────

    [Fact]
    public void GenerateMigration_SummaryHeader_AlwaysPresent()
    {
        var diff   = Diff("users", "id", []);
        var schema = Schema("users", ("id", PostgresType.Integer, true));

        var result = _sut.GenerateMigration(diff, schema);

        Assert.True(result.Success);
        Assert.Contains("-- Migration for: users", result.Value!);
    }

    [Fact]
    public void GenerateMigration_SummaryHeader_ShowsAddedCount()
    {
        var diff = Diff("users", "id",
        [
            AddedRow("1", new Dictionary<string, string?> { ["id"] = "1" }),
            AddedRow("2", new Dictionary<string, string?> { ["id"] = "2" }),
        ]);
        var schema = Schema("users", ("id", PostgresType.Integer, true));

        var result = _sut.GenerateMigration(diff, schema);

        Assert.True(result.Success);
        Assert.Contains("-- Added:     2", result.Value!);
    }

    [Fact]
    public void GenerateMigration_SummaryHeader_ShowsChangedCount()
    {
        var diff = Diff("users", "id",
        [
            ChangedRow("1", Chg("name", "Alice", "Alicia")),
            ChangedRow("2", Chg("name", "Bob",   "Robert")),
        ]);
        var schema = Schema("users",
            ("id",   PostgresType.Integer, true),
            ("name", PostgresType.Text,    false));

        var result = _sut.GenerateMigration(diff, schema);

        Assert.True(result.Success);
        Assert.Contains("-- Changed:   2", result.Value!);
    }

    [Fact]
    public void GenerateMigration_SummaryHeader_ShowsRemovedCount()
    {
        var diff   = Diff("users", "id", [RemovedRow("3"), RemovedRow("4")]);
        var schema = Schema("users", ("id", PostgresType.Integer, true));

        var result = _sut.GenerateMigration(diff, schema);

        Assert.True(result.Success);
        Assert.Contains("-- Removed:   2", result.Value!);
    }

    [Fact]
    public void GenerateMigration_SummaryHeader_ShowsUnchangedCount()
    {
        var diff   = Diff("users", "id", [UnchangedRow("5"), UnchangedRow("6"), UnchangedRow("7")]);
        var schema = Schema("users", ("id", PostgresType.Integer, true));

        var result = _sut.GenerateMigration(diff, schema);

        Assert.True(result.Success);
        Assert.Contains("-- Unchanged: 3", result.Value!);
    }

    [Fact]
    public void GenerateMigration_SummaryHeader_AllZeroCounts_StillPresent()
    {
        var diff   = Diff("users", "id", []);
        var schema = Schema("users", ("id", PostgresType.Integer, true));

        var result = _sut.GenerateMigration(diff, schema);

        Assert.True(result.Success);
        Assert.Contains("-- Added:     0",     result.Value!);
        Assert.Contains("-- Changed:   0",     result.Value!);
        Assert.Contains("-- Removed:   0",     result.Value!);
        Assert.Contains("-- Unchanged: 0",     result.Value!);
    }

    // ── Added rows → INSERT ────────────────────────────────────────────────────

    [Fact]
    public void GenerateMigration_AddedRow_ContainsInsertIntoTableName()
    {
        var diff = Diff("users", "id",
        [
            AddedRow("1", new Dictionary<string, string?> { ["id"] = "1", ["name"] = "Alice" }),
        ]);
        var schema = Schema("users",
            ("id",   PostgresType.Integer, true),
            ("name", PostgresType.Text,    false));

        var result = _sut.GenerateMigration(diff, schema);

        Assert.True(result.Success);
        Assert.Contains("INSERT INTO users", result.Value!);
    }

    [Fact]
    public void GenerateMigration_AddedRow_InsertContainsColumnList()
    {
        var diff = Diff("users", "id",
        [
            AddedRow("1", new Dictionary<string, string?> { ["id"] = "1", ["name"] = "Alice" }),
        ]);
        var schema = Schema("users",
            ("id",   PostgresType.Integer, true),
            ("name", PostgresType.Text,    false));

        var result = _sut.GenerateMigration(diff, schema);

        Assert.True(result.Success);
        Assert.Contains("(id, name)", result.Value!);
    }

    [Fact]
    public void GenerateMigration_AddedRow_InsertContainsCorrectValues()
    {
        var diff = Diff("users", "id",
        [
            AddedRow("1", new Dictionary<string, string?> { ["id"] = "1", ["name"] = "Alice" }),
        ]);
        var schema = Schema("users",
            ("id",   PostgresType.Integer, true),
            ("name", PostgresType.Text,    false));

        var result = _sut.GenerateMigration(diff, schema);

        Assert.True(result.Success);
        Assert.Contains("VALUES (1, 'Alice')", result.Value!);
    }

    [Fact]
    public void GenerateMigration_AddedRow_InsertUsesOnConflictDoNothing()
    {
        var diff = Diff("users", "id",
        [
            AddedRow("1", new Dictionary<string, string?> { ["id"] = "1" }),
        ]);
        var schema = Schema("users", ("id", PostgresType.Integer, true));

        var result = _sut.GenerateMigration(diff, schema);

        Assert.True(result.Success);
        Assert.Contains("ON CONFLICT (id) DO NOTHING;", result.Value!);
    }

    // ── Changed rows → UPDATE ──────────────────────────────────────────────────

    [Fact]
    public void GenerateMigration_ChangedRow_ContainsUpdateTableName()
    {
        var diff = Diff("users", "id",
        [
            ChangedRow("1", Chg("name", "Alice", "Alicia")),
        ]);
        var schema = Schema("users",
            ("id",   PostgresType.Integer, true),
            ("name", PostgresType.Text,    false));

        var result = _sut.GenerateMigration(diff, schema);

        Assert.True(result.Success);
        Assert.Contains("UPDATE users SET", result.Value!);
    }

    [Fact]
    public void GenerateMigration_ChangedRow_UpdateSetContainsChangedColumn()
    {
        var diff = Diff("users", "id",
        [
            ChangedRow("1", Chg("name", "Alice", "Alicia")),
        ]);
        var schema = Schema("users",
            ("id",   PostgresType.Integer, true),
            ("name", PostgresType.Text,    false));

        var result = _sut.GenerateMigration(diff, schema);

        Assert.True(result.Success);
        Assert.Contains("name = 'Alicia'", result.Value!);
    }

    [Fact]
    public void GenerateMigration_ChangedRow_UpdateHasWhereWithKeyValue()
    {
        var diff = Diff("users", "id",
        [
            ChangedRow("42", Chg("name", "Alice", "Alicia")),
        ]);
        var schema = Schema("users",
            ("id",   PostgresType.Integer, true),
            ("name", PostgresType.Text,    false));

        var result = _sut.GenerateMigration(diff, schema);

        Assert.True(result.Success);
        Assert.Contains("WHERE id = 42;", result.Value!);
    }

    [Fact]
    public void GenerateMigration_ChangedRow_UpdateIncludesOnlyChangedColumns()
    {
        // "score" is unchanged; only "name" should appear in SET
        var diff = Diff("users", "id",
        [
            ChangedRow("1", Chg("name", "Alice", "Alicia")),
        ]);
        var schema = Schema("users",
            ("id",    PostgresType.Integer, true),
            ("name",  PostgresType.Text,    false),
            ("score", PostgresType.Numeric, false));

        var result = _sut.GenerateMigration(diff, schema);

        Assert.True(result.Success);
        Assert.Contains("name = 'Alicia'", result.Value!);
        Assert.DoesNotContain("score",      result.Value!);
    }

    [Fact]
    public void GenerateMigration_ChangedRow_MultipleChangedColumns_AllInUpdateSet()
    {
        var diff = Diff("users", "id",
        [
            ChangedRow("1",
                Chg("name",  "Alice", "Alicia"),
                Chg("score", "9.5",   "10.0")),
        ]);
        var schema = Schema("users",
            ("id",    PostgresType.Integer, true),
            ("name",  PostgresType.Text,    false),
            ("score", PostgresType.Numeric, false));

        var result = _sut.GenerateMigration(diff, schema);

        Assert.True(result.Success);
        Assert.Contains("name = 'Alicia'", result.Value!);
        Assert.Contains("score = 10.0",    result.Value!);
    }

    [Fact]
    public void GenerateMigration_ChangedRow_KeyColumnNotInUpdateSet()
    {
        var diff = Diff("users", "id",
        [
            ChangedRow("1", Chg("name", "Alice", "Alicia")),
        ]);
        var schema = Schema("users",
            ("id",   PostgresType.Integer, true),
            ("name", PostgresType.Text,    false));

        var result = _sut.GenerateMigration(diff, schema);

        Assert.True(result.Success);
        // The key column must not appear in the SET block (indented form); it's only
        // allowed in the WHERE clause which uses a different prefix.
        Assert.DoesNotContain("    id = ", result.Value!);
    }

    // ── Removed rows → comments only ──────────────────────────────────────────

    [Fact]
    public void GenerateMigration_RemovedRow_ContainsKeyValueInComment()
    {
        var diff   = Diff("users", "id", [RemovedRow("99")]);
        var schema = Schema("users", ("id", PostgresType.Integer, true));

        var result = _sut.GenerateMigration(diff, schema);

        Assert.True(result.Success);
        Assert.Contains("-- id = 99", result.Value!);
    }

    [Fact]
    public void GenerateMigration_RemovedRow_NoInsertOrUpdateGenerated()
    {
        var diff   = Diff("users", "id", [RemovedRow("3")]);
        var schema = Schema("users", ("id", PostgresType.Integer, true));

        var result = _sut.GenerateMigration(diff, schema);

        Assert.True(result.Success);
        Assert.DoesNotContain("INSERT", result.Value!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE", result.Value!, StringComparison.OrdinalIgnoreCase);
    }

    // ── Unchanged rows → no SQL ────────────────────────────────────────────────

    [Fact]
    public void GenerateMigration_UnchangedRow_NoSqlStatementGenerated()
    {
        var diff   = Diff("users", "id", [UnchangedRow("1"), UnchangedRow("2")]);
        var schema = Schema("users", ("id", PostgresType.Integer, true));

        var result = _sut.GenerateMigration(diff, schema);

        Assert.True(result.Success);
        Assert.DoesNotContain("INSERT", result.Value!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE", result.Value!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("WHERE",  result.Value!, StringComparison.OrdinalIgnoreCase);
    }

    // ── NULL values ────────────────────────────────────────────────────────────

    [Fact]
    public void GenerateMigration_NullValueInAddedRow_InsertEmitsNull()
    {
        var diff = Diff("t", "id",
        [
            AddedRow("1", new Dictionary<string, string?> { ["id"] = "1", ["note"] = null }),
        ]);
        var schema = Schema("t",
            ("id",   PostgresType.Integer, true),
            ("note", PostgresType.Text,    false));

        var result = _sut.GenerateMigration(diff, schema);

        Assert.True(result.Success);
        Assert.Contains("VALUES (1, NULL)", result.Value!);
    }

    [Fact]
    public void GenerateMigration_NullNewValueInChangedRow_UpdateEmitsNull()
    {
        var diff = Diff("t", "id",
        [
            ChangedRow("1", Chg("note", "old", null)),
        ]);
        var schema = Schema("t",
            ("id",   PostgresType.Integer, true),
            ("note", PostgresType.Text,    false));

        var result = _sut.GenerateMigration(diff, schema);

        Assert.True(result.Success);
        Assert.Contains("note = NULL", result.Value!);
    }

    // ── String escaping ────────────────────────────────────────────────────────

    [Fact]
    public void GenerateMigration_StringWithSingleQuote_IsEscaped()
    {
        var diff = Diff("t", "id",
        [
            AddedRow("1", new Dictionary<string, string?> { ["id"] = "1", ["name"] = "Alice's" }),
        ]);
        var schema = Schema("t",
            ("id",   PostgresType.Integer, true),
            ("name", PostgresType.Text,    false));

        var result = _sut.GenerateMigration(diff, schema);

        Assert.True(result.Success);
        Assert.Contains("'Alice''s'", result.Value!);
    }

    [Fact]
    public void GenerateMigration_StringWithSingleQuoteInUpdate_IsEscaped()
    {
        var diff = Diff("t", "id",
        [
            ChangedRow("1", Chg("note", "old", "it's fine")),
        ]);
        var schema = Schema("t",
            ("id",   PostgresType.Integer, true),
            ("note", PostgresType.Text,    false));

        var result = _sut.GenerateMigration(diff, schema);

        Assert.True(result.Success);
        Assert.Contains("'it''s fine'", result.Value!);
    }

    // ── Numeric values ─────────────────────────────────────────────────────────

    [Fact]
    public void GenerateMigration_IntegerValueInInsert_NotQuoted()
    {
        var diff = Diff("t", "id",
        [
            AddedRow("42", new Dictionary<string, string?> { ["id"] = "42" }),
        ]);
        var schema = Schema("t", ("id", PostgresType.Integer, true));

        var result = _sut.GenerateMigration(diff, schema);

        Assert.True(result.Success);
        Assert.Contains("(42)",  result.Value!);
        Assert.DoesNotContain("'42'", result.Value!);
    }

    [Fact]
    public void GenerateMigration_NumericValueInUpdate_NotQuoted()
    {
        var diff = Diff("t", "id",
        [
            ChangedRow("1", Chg("price", "9.5", "10.99")),
        ]);
        var schema = Schema("t",
            ("id",    PostgresType.Integer, true),
            ("price", PostgresType.Numeric, false));

        var result = _sut.GenerateMigration(diff, schema);

        Assert.True(result.Success);
        Assert.Contains("price = 10.99", result.Value!);
        Assert.DoesNotContain("'10.99'",  result.Value!);
    }

    // ── Boolean values ─────────────────────────────────────────────────────────

    [Fact]
    public void GenerateMigration_BooleanTrueValueInInsert_EmitsTRUE()
    {
        var diff = Diff("t", "id",
        [
            AddedRow("1", new Dictionary<string, string?> { ["id"] = "1", ["active"] = "TRUE" }),
        ]);
        var schema = Schema("t",
            ("id",     PostgresType.Integer, true),
            ("active", PostgresType.Boolean, false));

        var result = _sut.GenerateMigration(diff, schema);

        Assert.True(result.Success);
        Assert.Contains("(1, TRUE)", result.Value!);
    }

    [Fact]
    public void GenerateMigration_BooleanFalseValueInUpdate_EmitsFALSE()
    {
        var diff = Diff("t", "id",
        [
            ChangedRow("1", Chg("active", "TRUE", "FALSE")),
        ]);
        var schema = Schema("t",
            ("id",     PostgresType.Integer, true),
            ("active", PostgresType.Boolean, false));

        var result = _sut.GenerateMigration(diff, schema);

        Assert.True(result.Success);
        Assert.Contains("active = FALSE", result.Value!);
    }

    // ── Safety: no destructive SQL ─────────────────────────────────────────────

    [Fact]
    public void GenerateMigration_NeverContainsDestructiveKeywords()
    {
        var diff = Diff("users", "id",
        [
            AddedRow("4",  new Dictionary<string, string?> { ["id"] = "4", ["name"] = "Dave" }),
            ChangedRow("1", Chg("name", "Alice", "Alicia")),
            RemovedRow("3"),
            UnchangedRow("2"),
        ]);
        var schema = Schema("users",
            ("id",   PostgresType.Integer, true),
            ("name", PostgresType.Text,    false));

        var result = _sut.GenerateMigration(diff, schema);

        Assert.True(result.Success);
        Assert.DoesNotContain("DELETE",   result.Value!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP",     result.Value!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TRUNCATE", result.Value!, StringComparison.OrdinalIgnoreCase);
    }

    // ── Mixed statuses in one result ───────────────────────────────────────────

    [Fact]
    public void GenerateMigration_MixedStatuses_AllSectionsPresent()
    {
        var diff = Diff("users", "id",
        [
            AddedRow("4",  new Dictionary<string, string?> { ["id"] = "4", ["name"] = "Dave" }),
            ChangedRow("1", Chg("name", "Alice", "Alicia")),
            RemovedRow("3"),
            UnchangedRow("2"),
        ]);
        var schema = Schema("users",
            ("id",   PostgresType.Integer, true),
            ("name", PostgresType.Text,    false));

        var result = _sut.GenerateMigration(diff, schema);

        Assert.True(result.Success);
        Assert.Contains("INSERT INTO users", result.Value!);
        Assert.Contains("UPDATE users",      result.Value!);
        Assert.Contains("-- id = 3",         result.Value!);
        Assert.Contains("-- Added:     1",   result.Value!);
        Assert.Contains("-- Changed:   1",   result.Value!);
        Assert.Contains("-- Removed:   1",   result.Value!);
        Assert.Contains("-- Unchanged: 1",   result.Value!);
    }

    // ── Text key value in WHERE is quoted ──────────────────────────────────────

    [Fact]
    public void GenerateMigration_TextKeyColumn_WhereValueIsQuoted()
    {
        var diff = Diff("t", "code",
        [
            ChangedRow("ADMIN", Chg("label", "Admin", "Administrator")),
        ]);
        var schema = Schema("t",
            ("code",  PostgresType.Text, true),
            ("label", PostgresType.Text, false));

        var result = _sut.GenerateMigration(diff, schema);

        Assert.True(result.Success);
        Assert.Contains("WHERE code = 'ADMIN';", result.Value!);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static SeedDiffResult Diff(string tableName, string keyColName, SeedDiffRow[] rows) =>
        new() { TableName = tableName, KeyColumnName = keyColName, Rows = rows.ToList() };

    private static TableSchema Schema(string name, params (string col, PostgresType type, bool isKey)[] columns) =>
        new()
        {
            TableName      = name,
            SheetName      = name,
            SampleRowCount = 0,
            Columns        = columns
                .Select((c, i) => new ColumnSchema
                {
                    Index          = i,
                    Name           = c.col,
                    SnakeCaseName  = c.col,
                    InferredType   = c.type,
                    IsNullable     = true,
                    HasDuplicates  = false,
                    IsCandidateKey = c.isKey,
                })
                .ToList(),
        };

    private static SeedDiffRow AddedRow(string keyValue, Dictionary<string, string?> newValues) =>
        new()
        {
            Status       = SeedDiffStatus.Added,
            KeyValue     = keyValue,
            NewRowValues = newValues,
        };

    private static SeedDiffRow ChangedRow(string keyValue, params SeedDiffCellChange[] changes) =>
        new()
        {
            Status   = SeedDiffStatus.Changed,
            KeyValue = keyValue,
            Changes  = changes.ToList(),
        };

    private static SeedDiffRow RemovedRow(string keyValue) =>
        new() { Status = SeedDiffStatus.Removed, KeyValue = keyValue };

    private static SeedDiffRow UnchangedRow(string keyValue) =>
        new() { Status = SeedDiffStatus.Unchanged, KeyValue = keyValue };

    private static SeedDiffCellChange Chg(string col, string? oldVal, string? newVal) =>
        new() { ColumnName = col, OldValue = oldVal, NewValue = newVal };
}
