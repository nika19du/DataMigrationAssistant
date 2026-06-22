using DataMigrationAssistant.Core.Models;
using DataMigrationAssistant.Core.Services;

namespace DataMigrationAssistant.Core.Tests;

public sealed class SeedDiffServiceTests
{
    private readonly SeedDiffService _sut = new();

    // ── Added rows ─────────────────────────────────────────────────────────────

    [Fact]
    public void Diff_RowInNewNotInOld_IsAdded()
    {
        var result = _sut.Diff(
            OldSeed("t", ["id", "name"], [["1", "Alice"]]),
            NewData(["id", "name"], [["1", "Alice"], ["2", "Bob"]]),
            Schema("t", [("id", true), ("name", false)]));

        Assert.True(result.Success);
        var added = result.Value!.Rows.Where(r => r.Status == SeedDiffStatus.Added).ToList();
        Assert.Single(added);
        Assert.Equal("2", added[0].KeyValue);
    }

    [Fact]
    public void Diff_AddedRow_HasEmptyChanges()
    {
        var result = _sut.Diff(
            OldSeed("t", ["id", "name"], []),
            NewData(["id", "name"], [["1", "Alice"]]),
            Schema("t", [("id", true), ("name", false)]));

        Assert.True(result.Success);
        Assert.Empty(result.Value!.Rows.Single(r => r.Status == SeedDiffStatus.Added).Changes);
    }

    // ── Removed rows ───────────────────────────────────────────────────────────

    [Fact]
    public void Diff_RowInOldNotInNew_IsRemoved()
    {
        var result = _sut.Diff(
            OldSeed("t", ["id", "name"], [["1", "Alice"], ["2", "Bob"]]),
            NewData(["id", "name"], [["1", "Alice"]]),
            Schema("t", [("id", true), ("name", false)]));

        Assert.True(result.Success);
        var removed = result.Value!.Rows.Where(r => r.Status == SeedDiffStatus.Removed).ToList();
        Assert.Single(removed);
        Assert.Equal("2", removed[0].KeyValue);
    }

    [Fact]
    public void Diff_RemovedRow_HasEmptyChanges()
    {
        var result = _sut.Diff(
            OldSeed("t", ["id", "name"], [["1", "Alice"]]),
            NewData(["id", "name"], []),
            Schema("t", [("id", true), ("name", false)]));

        Assert.True(result.Success);
        Assert.Empty(result.Value!.Rows.Single(r => r.Status == SeedDiffStatus.Removed).Changes);
    }

    // ── Changed rows ───────────────────────────────────────────────────────────

    [Fact]
    public void Diff_SameKeyDifferentValue_IsChanged()
    {
        var result = _sut.Diff(
            OldSeed("t", ["id", "name"], [["1", "Alice"]]),
            NewData(["id", "name"], [["1", "Alicia"]]),
            Schema("t", [("id", true), ("name", false)]));

        Assert.True(result.Success);
        var changed = result.Value!.Rows.Single(r => r.Status == SeedDiffStatus.Changed);
        Assert.Equal("1", changed.KeyValue);
    }

    [Fact]
    public void Diff_ChangedRow_ChangesContainsCorrectColumnAndValues()
    {
        var result = _sut.Diff(
            OldSeed("t", ["id", "name"], [["1", "Alice"]]),
            NewData(["id", "name"], [["1", "Alicia"]]),
            Schema("t", [("id", true), ("name", false)]));

        Assert.True(result.Success);
        var ch = result.Value!.Rows.Single(r => r.Status == SeedDiffStatus.Changed).Changes;
        Assert.Single(ch);
        Assert.Equal("name",   ch[0].ColumnName);
        Assert.Equal("Alice",  ch[0].OldValue);
        Assert.Equal("Alicia", ch[0].NewValue);
    }

    [Fact]
    public void Diff_ChangedRow_OnlyChangedColumnsInChanges()
    {
        // "id" is the key (same on both sides); only "name" changes; "score" stays the same
        var result = _sut.Diff(
            OldSeed("t", ["id", "name", "score"], [["1", "Alice", "9.5"]]),
            NewData(["id", "name", "score"], [["1", "Alicia", "9.5"]]),
            Schema("t", [("id", true), ("name", false), ("score", false)]));

        Assert.True(result.Success);
        var ch = result.Value!.Rows.Single(r => r.Status == SeedDiffStatus.Changed).Changes;
        Assert.Single(ch);
        Assert.Equal("name", ch[0].ColumnName);
    }

    // ── Multiple changed columns ───────────────────────────────────────────────

    [Fact]
    public void Diff_MultipleColumnsChanged_AllAppearInChanges()
    {
        var result = _sut.Diff(
            OldSeed("t", ["id", "name", "score"], [["1", "Alice", "9.5"]]),
            NewData(["id", "name", "score"], [["1", "Alicia", "10.0"]]),
            Schema("t", [("id", true), ("name", false), ("score", false)]));

        Assert.True(result.Success);
        var ch = result.Value!.Rows.Single(r => r.Status == SeedDiffStatus.Changed).Changes;
        Assert.Equal(2, ch.Count);
        Assert.Contains(ch, c => c.ColumnName == "name"  && c.OldValue == "Alice" && c.NewValue == "Alicia");
        Assert.Contains(ch, c => c.ColumnName == "score" && c.OldValue == "9.5"   && c.NewValue == "10.0");
    }

    // ── Unchanged rows ─────────────────────────────────────────────────────────

    [Fact]
    public void Diff_SameKeyAndValues_IsUnchanged()
    {
        var result = _sut.Diff(
            OldSeed("t", ["id", "name"], [["1", "Alice"]]),
            NewData(["id", "name"], [["1", "Alice"]]),
            Schema("t", [("id", true), ("name", false)]));

        Assert.True(result.Success);
        var row = result.Value!.Rows.Single();
        Assert.Equal(SeedDiffStatus.Unchanged, row.Status);
        Assert.Empty(row.Changes);
    }

    // ── NULL vs empty Excel cell ───────────────────────────────────────────────

    [Fact]
    public void Diff_SqlNullVsEmptyCell_IsUnchanged()
    {
        // Old: SQL NULL → null in SeedRecord; New: empty cell → null in SheetPreview
        var result = _sut.Diff(
            OldSeed("t", ["id", "note"], [["1", null]]),
            NewData(["id", "note"], [["1", null]]),
            Schema("t", [("id", true), ("note", false)]));

        Assert.True(result.Success);
        Assert.Equal(SeedDiffStatus.Unchanged, result.Value!.Rows.Single().Status);
    }

    [Fact]
    public void Diff_SqlNullVsWhitespaceCell_IsUnchanged()
    {
        var result = _sut.Diff(
            OldSeed("t", ["id", "note"], [["1", null]]),
            NewData(["id", "note"], [["1", "  "]]),
            Schema("t", [("id", true), ("note", false)]));

        Assert.True(result.Success);
        Assert.Equal(SeedDiffStatus.Unchanged, result.Value!.Rows.Single().Status);
    }

    [Fact]
    public void Diff_SqlNullVsNonEmptyCell_IsChanged()
    {
        var result = _sut.Diff(
            OldSeed("t", ["id", "note"], [["1", null]]),
            NewData(["id", "note"], [["1", "hello"]]),
            Schema("t", [("id", true), ("note", false)]));

        Assert.True(result.Success);
        var ch = result.Value!.Rows.Single(r => r.Status == SeedDiffStatus.Changed).Changes;
        Assert.Single(ch);
        Assert.Null(ch[0].OldValue);
        Assert.Equal("hello", ch[0].NewValue);
    }

    // ── Boolean casing ─────────────────────────────────────────────────────────

    [Fact]
    public void Diff_TrueCaseMismatch_IsUnchanged()
    {
        // Old SQL has TRUE (uppercase as generated), new Excel has "true" (lowercase)
        var result = _sut.Diff(
            OldSeed("t", ["id", "active"], [["1", "TRUE"]]),
            NewData(["id", "active"], [["1", "true"]]),
            Schema("t", [("id", true), ("active", false)]));

        Assert.True(result.Success);
        Assert.Equal(SeedDiffStatus.Unchanged, result.Value!.Rows.Single().Status);
    }

    [Fact]
    public void Diff_FalseCaseMismatch_IsUnchanged()
    {
        var result = _sut.Diff(
            OldSeed("t", ["id", "active"], [["1", "FALSE"]]),
            NewData(["id", "active"], [["1", "false"]]),
            Schema("t", [("id", true), ("active", false)]));

        Assert.True(result.Success);
        Assert.Equal(SeedDiffStatus.Unchanged, result.Value!.Rows.Single().Status);
    }

    [Fact]
    public void Diff_TrueVsFalse_IsChanged()
    {
        var result = _sut.Diff(
            OldSeed("t", ["id", "active"], [["1", "TRUE"]]),
            NewData(["id", "active"], [["1", "false"]]),
            Schema("t", [("id", true), ("active", false)]));

        Assert.True(result.Success);
        Assert.Equal(SeedDiffStatus.Changed, result.Value!.Rows.Single().Status);
    }

    // ── No candidate key ───────────────────────────────────────────────────────

    [Fact]
    public void Diff_NoCandidateKey_ReturnsFail()
    {
        var result = _sut.Diff(
            OldSeed("t", ["name"], [["Alice"]]),
            NewData(["name"], [["Alice"]]),
            Schema("t", [("name", false)]));

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Diff_NoCandidateKey_ErrorMentionsTableName()
    {
        var result = _sut.Diff(
            OldSeed("my_table", ["name"], [["Alice"]]),
            NewData(["name"], [["Alice"]]),
            Schema("my_table", [("name", false)]));

        Assert.False(result.Success);
        Assert.Contains("my_table", result.Error!);
    }

    // ── Missing key column in old seed ─────────────────────────────────────────

    [Fact]
    public void Diff_KeyColumnMissingFromOldSeed_ReturnsFail()
    {
        // Old seed has only "name", not the key column "id"
        var result = _sut.Diff(
            OldSeed("t", ["name"], [["Alice"]]),
            NewData(["id", "name"], [["1", "Alice"]]),
            Schema("t", [("id", true), ("name", false)]));

        Assert.False(result.Success);
        Assert.Contains("id", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    // ── Missing key column in new Excel ────────────────────────────────────────

    [Fact]
    public void Diff_KeyColumnMissingFromNewData_ReturnsFail()
    {
        // New Excel has only "name", not the key column "id"
        var result = _sut.Diff(
            OldSeed("t", ["id", "name"], [["1", "Alice"]]),
            NewData(["name"], [["Alice"]]),
            Schema("t", [("id", true), ("name", false)]));

        Assert.False(result.Success);
        Assert.Contains("id", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    // ── Result metadata ────────────────────────────────────────────────────────

    [Fact]
    public void Diff_TableNameInResult_MatchesSchema()
    {
        var result = _sut.Diff(
            OldSeed("my_table", ["id"], [["1"]]),
            NewData(["id"], [["1"]]),
            Schema("my_table", [("id", true)]));

        Assert.True(result.Success);
        Assert.Equal("my_table", result.Value!.TableName);
    }

    [Fact]
    public void Diff_KeyColumnName_SetInResult()
    {
        var result = _sut.Diff(
            OldSeed("t", ["id", "name"], [["1", "Alice"]]),
            NewData(["id", "name"], [["1", "Alice"]]),
            Schema("t", [("id", true), ("name", false)]));

        Assert.True(result.Success);
        Assert.Equal("id", result.Value!.KeyColumnName);
    }

    [Fact]
    public void Diff_AddedRow_NewRowValuesContainsAllColumns()
    {
        var result = _sut.Diff(
            OldSeed("t", ["id", "name"], [["1", "Alice"]]),
            NewData(["id", "name"], [["1", "Alice"], ["2", "Bob"]]),
            Schema("t", [("id", true), ("name", false)]));

        Assert.True(result.Success);
        var added = result.Value!.Rows.Single(r => r.Status == SeedDiffStatus.Added);
        Assert.Equal("2",   added.NewRowValues["id"]);
        Assert.Equal("Bob", added.NewRowValues["name"]);
    }

    [Fact]
    public void Diff_EmptyOldAndNew_ReturnsEmptyRows()
    {
        var result = _sut.Diff(
            OldSeed("t", ["id", "name"], []),
            NewData(["id", "name"], []),
            Schema("t", [("id", true), ("name", false)]));

        Assert.True(result.Success);
        Assert.Empty(result.Value!.Rows);
    }

    // ── Mixed status in one result ──────────────────────────────────────────────

    [Fact]
    public void Diff_MixedStatuses_AllDetectedCorrectly()
    {
        // Row 1: unchanged, Row 2: changed, Row 3: removed from old, Row 4: added in new
        var result = _sut.Diff(
            OldSeed("t", ["id", "val"], [["1", "same"], ["2", "old"],  ["3", "gone"]]),
            NewData(["id", "val"],      [["1", "same"], ["2", "new"],  ["4", "fresh"]]),
            Schema("t", [("id", true), ("val", false)]));

        Assert.True(result.Success);
        var rows = result.Value!.Rows;
        Assert.Single(rows, r => r.Status == SeedDiffStatus.Unchanged && r.KeyValue == "1");
        Assert.Single(rows, r => r.Status == SeedDiffStatus.Changed   && r.KeyValue == "2");
        Assert.Single(rows, r => r.Status == SeedDiffStatus.Added     && r.KeyValue == "4");
        Assert.Single(rows, r => r.Status == SeedDiffStatus.Removed   && r.KeyValue == "3");
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static SeedRecord OldSeed(string table, string[] columns, string?[][] rows)
    {
        return new SeedRecord
        {
            TableName = table,
            Columns   = columns.ToList(),
            Rows      = rows.Select(r => (IReadOnlyList<string?>)r.ToList()).ToList(),
        };
    }

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
            SheetName     = "TestSheet",
            FilePath      = "/test.xlsx",
            Columns       = colInfos,
            Rows          = rowDicts,
            TotalRowCount = rows.Length,
        };
    }

    private static TableSchema Schema(string table, (string name, bool isKey)[] columns)
    {
        var colSchemas = columns
            .Select((c, i) => new ColumnSchema
            {
                Index          = i,
                Name           = c.name,
                SnakeCaseName  = c.name,
                InferredType   = PostgresType.Text,
                IsNullable     = true,
                HasDuplicates  = false,
                IsCandidateKey = c.isKey,
            })
            .ToList();

        return new TableSchema
        {
            TableName      = table,
            SheetName      = "TestSheet",
            Columns        = colSchemas,
            SampleRowCount = 0,
        };
    }
}
