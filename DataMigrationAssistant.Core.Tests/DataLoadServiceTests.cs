using DataMigrationAssistant.Core.Services;
using DataMigrationAssistant.Core.Tests.Helpers;

namespace DataMigrationAssistant.Core.Tests;

public sealed class DataLoadServiceTests
{
    // ── Parser is called with int.MaxValue ────────────────────────────────────

    [Fact]
    public void LoadAllRows_PassesIntMaxValueToParser()
    {
        var parser  = new FakeExcelParser(availableRows: 15);
        var service = new DataLoadService(parser);

        service.LoadAllRows("test.xlsx");

        Assert.Equal(int.MaxValue, parser.LastMaxRows);
    }

    [Fact]
    public void LoadAllRows_PassesHeaderRowToParser()
    {
        var parser  = new FakeExcelParser(availableRows: 5);
        var service = new DataLoadService(parser);

        service.LoadAllRows("test.xlsx", headerRow: 4);

        Assert.Equal(4, parser.LastHeaderRow);
    }

    [Fact]
    public void LoadAllRows_DefaultHeaderRow_PassesZeroToParser()
    {
        var parser  = new FakeExcelParser(availableRows: 5);
        var service = new DataLoadService(parser);

        service.LoadAllRows("test.xlsx");

        Assert.Equal(0, parser.LastHeaderRow);
    }

    [Fact]
    public void LoadAllRows_PassesSheetNameToParser()
    {
        var parser  = new FakeExcelParser(availableRows: 5);
        var service = new DataLoadService(parser);

        service.LoadAllRows("test.xlsx", sheetName: "DataSheet");

        Assert.Equal("DataSheet", parser.LastSheetName);
    }

    [Fact]
    public void LoadAllRows_PassesSheetIndexToParser()
    {
        var parser  = new FakeExcelParser(availableRows: 5);
        var service = new DataLoadService(parser);

        service.LoadAllRows("test.xlsx", sheetIndex: 3);

        Assert.Equal(3, parser.LastSheetIndex);
    }

    [Fact]
    public void LoadAllRows_DefaultSheetArgs_PassesNullsToParser()
    {
        var parser  = new FakeExcelParser(availableRows: 5);
        var service = new DataLoadService(parser);

        service.LoadAllRows("test.xlsx");

        Assert.Null(parser.LastSheetName);
        Assert.Null(parser.LastSheetIndex);
    }

    // ── All rows are returned regardless of the 10-row preview cap ───────────

    [Fact]
    public void LoadAllRows_MoreThan10RowsAvailable_ReturnsAllRows()
    {
        var parser  = new FakeExcelParser(availableRows: 15);
        var service = new DataLoadService(parser);

        var result = service.LoadAllRows("test.xlsx");

        Assert.True(result.Success);
        Assert.Equal(15, result.Value!.Rows.Count);
    }

    [Fact]
    public void LoadAllRows_FewerThan10RowsAvailable_ReturnsAllRows()
    {
        var parser  = new FakeExcelParser(availableRows: 5);
        var service = new DataLoadService(parser);

        var result = service.LoadAllRows("test.xlsx");

        Assert.True(result.Success);
        Assert.Equal(5, result.Value!.Rows.Count);
    }

    [Fact]
    public void LoadAllRows_ReturnsMoreRowsThanPreviewWould()
    {
        const int previewCap  = 10;
        const int totalInFile = 25;

        var parser   = new FakeExcelParser(availableRows: totalInFile);
        var preview  = new PreviewService(parser);
        var loader   = new DataLoadService(parser);

        var previewResult = preview.GeneratePreview("test.xlsx");
        var loadResult    = loader.LoadAllRows("test.xlsx");

        Assert.Equal(previewCap, previewResult.Value!.Rows.Count);
        Assert.Equal(totalInFile, loadResult.Value!.Rows.Count);
        Assert.True(loadResult.Value.Rows.Count > previewResult.Value.Rows.Count);
    }

    // ── Row data is preserved correctly ──────────────────────────────────────

    [Fact]
    public void LoadAllRows_RowValuesMatchExpected()
    {
        var parser  = new FakeExcelParser(availableRows: 3);
        var service = new DataLoadService(parser);

        var result = service.LoadAllRows("test.xlsx");

        Assert.True(result.Success);
        var rows = result.Value!.Rows;
        Assert.Equal("1", rows[0]["id"]);
        Assert.Equal("2", rows[1]["id"]);
        Assert.Equal("3", rows[2]["id"]);
    }
}
