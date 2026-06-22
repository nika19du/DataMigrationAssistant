using DataMigrationAssistant.Core.Models;
using DataMigrationAssistant.Core.Services;

namespace DataMigrationAssistant.Core.Tests;

public sealed class DiffReportGeneratorServiceTests
{
    private readonly DiffReportGeneratorService _sut = new();

    // ── Title ──────────────────────────────────────────────────────────────────

    [Fact]
    public void GenerateReport_ContainsTableNameInTitle()
    {
        var report = _sut.GenerateReport(Result("users", []));

        Assert.Contains("# Seed Diff: users", report);
    }

    [Fact]
    public void GenerateReport_SnakeCaseTableName_AppearExactly()
    {
        var report = _sut.GenerateReport(Result("order_items", []));

        Assert.Contains("order_items", report);
    }

    // ── Summary counts ─────────────────────────────────────────────────────────

    [Fact]
    public void GenerateReport_SummaryShowsAddedCount()
    {
        var report = _sut.GenerateReport(Result("t",
        [
            Added("1"), Added("2"),
        ]));

        Assert.Contains("| Added     | 2 |", report);
    }

    [Fact]
    public void GenerateReport_SummaryShowsRemovedCount()
    {
        var report = _sut.GenerateReport(Result("t", [Removed("1")]));

        Assert.Contains("| Removed   | 1 |", report);
    }

    [Fact]
    public void GenerateReport_SummaryShowsChangedCount()
    {
        var report = _sut.GenerateReport(Result("t",
        [
            Changed("1", Chg("name", "A", "B")),
            Changed("2", Chg("name", "C", "D")),
            Changed("3", Chg("name", "E", "F")),
        ]));

        Assert.Contains("| Changed   | 3 |", report);
    }

    [Fact]
    public void GenerateReport_SummaryShowsUnchangedCount()
    {
        var report = _sut.GenerateReport(Result("t",
        [
            Unchanged("1"), Unchanged("2"),
        ]));

        Assert.Contains("| Unchanged | 2 |", report);
    }

    [Fact]
    public void GenerateReport_AllZeroCounts_SummaryStillPresent()
    {
        var report = _sut.GenerateReport(Result("t", []));

        Assert.Contains("| Added     | 0 |", report);
        Assert.Contains("| Removed   | 0 |", report);
        Assert.Contains("| Changed   | 0 |", report);
        Assert.Contains("| Unchanged | 0 |", report);
    }

    // ── Added section ──────────────────────────────────────────────────────────

    [Fact]
    public void GenerateReport_AddedRows_SectionPresent()
    {
        var report = _sut.GenerateReport(Result("t", [Added("42")]));

        Assert.Contains("## Added", report);
    }

    [Fact]
    public void GenerateReport_AddedRows_KeyValueListed()
    {
        var report = _sut.GenerateReport(Result("t", [Added("42")]));

        Assert.Contains("`42`", report);
    }

    [Fact]
    public void GenerateReport_MultipleAddedRows_AllKeyValuesListed()
    {
        var report = _sut.GenerateReport(Result("t", [Added("1"), Added("2"), Added("3")]));

        Assert.Contains("`1`", report);
        Assert.Contains("`2`", report);
        Assert.Contains("`3`", report);
    }

    [Fact]
    public void GenerateReport_NoAddedRows_AddedSectionAbsent()
    {
        var report = _sut.GenerateReport(Result("t", [Removed("1")]));

        Assert.DoesNotContain("## Added", report);
    }

    // ── Removed section ────────────────────────────────────────────────────────

    [Fact]
    public void GenerateReport_RemovedRows_SectionPresent()
    {
        var report = _sut.GenerateReport(Result("t", [Removed("5")]));

        Assert.Contains("## Removed", report);
    }

    [Fact]
    public void GenerateReport_RemovedRows_KeyValueListed()
    {
        var report = _sut.GenerateReport(Result("t", [Removed("5")]));

        Assert.Contains("`5`", report);
    }

    [Fact]
    public void GenerateReport_NoRemovedRows_RemovedSectionAbsent()
    {
        var report = _sut.GenerateReport(Result("t", [Added("1")]));

        Assert.DoesNotContain("## Removed", report);
    }

    // ── Changed section ────────────────────────────────────────────────────────

    [Fact]
    public void GenerateReport_ChangedRows_SectionPresent()
    {
        var report = _sut.GenerateReport(Result("t",
        [
            Changed("1", Chg("name", "Alice", "Alicia")),
        ]));

        Assert.Contains("## Changed", report);
    }

    [Fact]
    public void GenerateReport_ChangedRow_KeyValueInSubheading()
    {
        var report = _sut.GenerateReport(Result("t",
        [
            Changed("99", Chg("name", "Alice", "Alicia")),
        ]));

        Assert.Contains("### Row `99`", report);
    }

    [Fact]
    public void GenerateReport_ChangedRow_ColumnNameInTable()
    {
        var report = _sut.GenerateReport(Result("t",
        [
            Changed("1", Chg("email", "a@b.com", "c@d.com")),
        ]));

        Assert.Contains("email", report);
    }

    [Fact]
    public void GenerateReport_ChangedRow_OldAndNewValuesInTable()
    {
        var report = _sut.GenerateReport(Result("t",
        [
            Changed("1", Chg("score", "9.5", "10.0")),
        ]));

        Assert.Contains("9.5",  report);
        Assert.Contains("10.0", report);
    }

    [Fact]
    public void GenerateReport_ChangedRow_MultipleChangedColumns_AllInTable()
    {
        var report = _sut.GenerateReport(Result("t",
        [
            Changed("1",
                Chg("name",  "Alice",  "Alicia"),
                Chg("score", "9.5",    "10.0")),
        ]));

        Assert.Contains("name",   report);
        Assert.Contains("Alice",  report);
        Assert.Contains("Alicia", report);
        Assert.Contains("score",  report);
        Assert.Contains("9.5",    report);
        Assert.Contains("10.0",   report);
    }

    [Fact]
    public void GenerateReport_NoChangedRows_ChangedSectionAbsent()
    {
        var report = _sut.GenerateReport(Result("t", [Unchanged("1")]));

        Assert.DoesNotContain("## Changed", report);
    }

    // ── NULL value rendering ───────────────────────────────────────────────────

    [Fact]
    public void GenerateReport_NullOldValue_RenderedAsNULL()
    {
        var report = _sut.GenerateReport(Result("t",
        [
            Changed("1", Chg("note", null, "hello")),
        ]));

        Assert.Contains("NULL", report);
    }

    [Fact]
    public void GenerateReport_NullNewValue_RenderedAsNULL()
    {
        var report = _sut.GenerateReport(Result("t",
        [
            Changed("1", Chg("note", "hello", null)),
        ]));

        Assert.Contains("NULL", report);
    }

    // ── Pipe character escaping ────────────────────────────────────────────────

    [Fact]
    public void GenerateReport_PipeInOldValue_IsEscaped()
    {
        var report = _sut.GenerateReport(Result("t",
        [
            Changed("1", Chg("name", "a|b", "c")),
        ]));

        Assert.Contains(@"a\|b", report);
        Assert.DoesNotContain("| a|b |", report);
    }

    [Fact]
    public void GenerateReport_PipeInNewValue_IsEscaped()
    {
        var report = _sut.GenerateReport(Result("t",
        [
            Changed("1", Chg("name", "a", "x|y")),
        ]));

        Assert.Contains(@"x\|y", report);
    }

    [Fact]
    public void GenerateReport_PipeInColumnName_IsEscaped()
    {
        var report = _sut.GenerateReport(Result("t",
        [
            Changed("1", Chg("col|name", "a", "b")),
        ]));

        Assert.Contains(@"col\|name", report);
    }

    // ── Unchanged rows not listed ──────────────────────────────────────────────

    [Fact]
    public void GenerateReport_UnchangedRows_NotListedIndividually()
    {
        var report = _sut.GenerateReport(Result("t",
        [
            Unchanged("1"), Unchanged("2"),
        ]));

        // Key values of unchanged rows should NOT appear as list items or subheadings
        Assert.DoesNotContain("- `1`", report);
        Assert.DoesNotContain("- `2`", report);
        Assert.DoesNotContain("### Row", report);
    }

    // ── Summary table header always present ───────────────────────────────────

    [Fact]
    public void GenerateReport_Always_ContainsSummaryTableHeader()
    {
        var report = _sut.GenerateReport(Result("t", []));

        Assert.Contains("| Status    | Count |", report);
        Assert.Contains("|-----------|-------|", report);
    }

    // ── Mixed statuses ─────────────────────────────────────────────────────────

    [Fact]
    public void GenerateReport_MixedStatuses_AllSectionsPresent()
    {
        var report = _sut.GenerateReport(Result("users",
        [
            Added("10"),
            Removed("20"),
            Changed("30", Chg("val", "old", "new")),
            Unchanged("40"),
        ]));

        Assert.Contains("## Added",   report);
        Assert.Contains("## Removed", report);
        Assert.Contains("## Changed", report);
        // Unchanged only in summary
        Assert.DoesNotContain("## Unchanged", report);
        Assert.Contains("| Unchanged | 1 |", report);
    }

    [Fact]
    public void GenerateReport_MixedStatuses_CountsCorrect()
    {
        var report = _sut.GenerateReport(Result("users",
        [
            Added("1"), Added("2"),
            Removed("3"),
            Changed("4", Chg("x", "a", "b")),
            Changed("5", Chg("x", "c", "d")),
            Changed("6", Chg("x", "e", "f")),
            Unchanged("7"), Unchanged("8"), Unchanged("9"), Unchanged("10"),
        ]));

        Assert.Contains("| Added     | 2 |",  report);
        Assert.Contains("| Removed   | 1 |",  report);
        Assert.Contains("| Changed   | 3 |",  report);
        Assert.Contains("| Unchanged | 4 |",  report);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static SeedDiffResult Result(string table, SeedDiffRow[] rows) =>
        new() { TableName = table, Rows = rows.ToList() };

    private static SeedDiffRow Added(string key) =>
        new() { Status = SeedDiffStatus.Added, KeyValue = key };

    private static SeedDiffRow Removed(string key) =>
        new() { Status = SeedDiffStatus.Removed, KeyValue = key };

    private static SeedDiffRow Unchanged(string key) =>
        new() { Status = SeedDiffStatus.Unchanged, KeyValue = key };

    private static SeedDiffRow Changed(string key, params SeedDiffCellChange[] changes) =>
        new() { Status = SeedDiffStatus.Changed, KeyValue = key, Changes = changes.ToList() };

    private static SeedDiffCellChange Chg(string col, string? oldVal, string? newVal) =>
        new() { ColumnName = col, OldValue = oldVal, NewValue = newVal };
}
