using DataMigrationAssistant.Core.Services;
using DataMigrationAssistant.Core.Tests.Helpers;

namespace DataMigrationAssistant.Core.Tests;

public sealed class PreviewServiceTests
{
    // ── Parser is called with maxRows: 10 ─────────────────────────────────────

    [Fact]
    public void GeneratePreview_PassesMaxRows10ToParser()
    {
        var parser  = new FakeExcelParser(availableRows: 15);
        var service = new PreviewService(parser);

        service.GeneratePreview("test.xlsx");

        Assert.Equal(10, parser.LastMaxRows);
    }

    [Fact]
    public void GeneratePreview_PassesHeaderRowToParser()
    {
        var parser  = new FakeExcelParser(availableRows: 5);
        var service = new PreviewService(parser);

        service.GeneratePreview("test.xlsx", headerRow: 3);

        Assert.Equal(3, parser.LastHeaderRow);
    }

    [Fact]
    public void GeneratePreview_DefaultHeaderRow_PassesZeroToParser()
    {
        var parser  = new FakeExcelParser(availableRows: 5);
        var service = new PreviewService(parser);

        service.GeneratePreview("test.xlsx");

        Assert.Equal(0, parser.LastHeaderRow);
    }

    [Fact]
    public void GeneratePreview_PassesSheetNameToParser()
    {
        var parser  = new FakeExcelParser(availableRows: 5);
        var service = new PreviewService(parser);

        service.GeneratePreview("test.xlsx", sheetName: "MySheet");

        Assert.Equal("MySheet", parser.LastSheetName);
    }

    [Fact]
    public void GeneratePreview_PassesSheetIndexToParser()
    {
        var parser  = new FakeExcelParser(availableRows: 5);
        var service = new PreviewService(parser);

        service.GeneratePreview("test.xlsx", sheetIndex: 2);

        Assert.Equal(2, parser.LastSheetIndex);
    }

    [Fact]
    public void GeneratePreview_DefaultSheetArgs_PassesNullsToParser()
    {
        var parser  = new FakeExcelParser(availableRows: 5);
        var service = new PreviewService(parser);

        service.GeneratePreview("test.xlsx");

        Assert.Null(parser.LastSheetName);
        Assert.Null(parser.LastSheetIndex);
    }

    // ── Row count is capped at 10 ─────────────────────────────────────────────

    [Fact]
    public void GeneratePreview_MoreThan10RowsAvailable_ReturnsOnly10()
    {
        var parser  = new FakeExcelParser(availableRows: 15);
        var service = new PreviewService(parser);

        var result = service.GeneratePreview("test.xlsx");

        Assert.True(result.Success);
        Assert.Equal(10, result.Value!.Rows.Count);
    }

    [Fact]
    public void GeneratePreview_FewerThan10RowsAvailable_ReturnsAllRows()
    {
        var parser  = new FakeExcelParser(availableRows: 5);
        var service = new PreviewService(parser);

        var result = service.GeneratePreview("test.xlsx");

        Assert.True(result.Success);
        Assert.Equal(5, result.Value!.Rows.Count);
    }

    // ── TotalRowCount reflects the full sheet, not the preview ────────────────

    [Fact]
    public void GeneratePreview_TotalRowCount_ReflectsFullSheet()
    {
        var parser  = new FakeExcelParser(availableRows: 15);
        var service = new PreviewService(parser);

        var result = service.GeneratePreview("test.xlsx");

        Assert.True(result.Success);
        Assert.Equal(15, result.Value!.TotalRowCount);
        Assert.Equal(10, result.Value!.Rows.Count);
    }
}
