using ClosedXML.Excel;
using DataMigrationAssistant.Core.Parsers;

namespace DataMigrationAssistant.Core.Tests;

public sealed class ExcelParserTests : IDisposable
{
    private readonly List<string> _tempFiles = [];
    private readonly ExcelParser _sut = new();

    public void Dispose()
    {
        foreach (var f in _tempFiles)
            if (File.Exists(f)) File.Delete(f);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private string CreateTempExcel(Action<IXLWorksheet> configure)
    {
        var path = Path.ChangeExtension(Path.GetTempFileName(), ".xlsx");
        _tempFiles.Add(path);
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Sheet1");
        configure(ws);
        wb.SaveAs(path);
        return path;
    }

    private string CreateTempMultiSheetExcel(Action<IXLWorkbook> configure)
    {
        var path = Path.ChangeExtension(Path.GetTempFileName(), ".xlsx");
        _tempFiles.Add(path);
        using var wb = new XLWorkbook();
        configure(wb);
        wb.SaveAs(path);
        return path;
    }

    // ── Default (headerRow = 0) uses FirstRowUsed ──────────────────────────────

    [Fact]
    public void ParsePreview_Default_UsesFirstRowUsed()
    {
        var path = CreateTempExcel(ws =>
        {
            ws.Cell(1, 1).Value = "Id";
            ws.Cell(1, 2).Value = "Name";
            ws.Cell(2, 1).Value = 1;
            ws.Cell(2, 2).Value = "Alice";
        });

        var result = _sut.ParsePreview(path);

        Assert.True(result.Success);
        Assert.Equal(2, result.Value!.Columns.Count);
        Assert.Equal("id", result.Value.Columns[0].SnakeCaseName);
        Assert.Equal("name", result.Value.Columns[1].SnakeCaseName);
    }

    [Fact]
    public void ParsePreview_Default_HeaderRowNumberIsFirstUsedRow()
    {
        var path = CreateTempExcel(ws =>
        {
            ws.Cell(1, 1).Value = "Id";
            ws.Cell(2, 1).Value = 42;
        });

        var result = _sut.ParsePreview(path);

        Assert.True(result.Success);
        Assert.Equal(1, result.Value!.HeaderRowNumber);
    }

    // ── Explicit header row ────────────────────────────────────────────────────

    [Fact]
    public void ParsePreview_ExplicitHeaderRow_UsesSpecifiedRow()
    {
        var path = CreateTempExcel(ws =>
        {
            ws.Cell(1, 1).Value = "Report Title";
            ws.Cell(2, 1).Value = "Q1 2025";
            ws.Cell(3, 1).Value = "Id";
            ws.Cell(3, 2).Value = "Name";
            ws.Cell(4, 1).Value = 1;
            ws.Cell(4, 2).Value = "Alice";
        });

        var result = _sut.ParsePreview(path, headerRow: 3);

        Assert.True(result.Success);
        Assert.Equal(2, result.Value!.Columns.Count);
        Assert.Equal("id", result.Value.Columns[0].SnakeCaseName);
        Assert.Equal("name", result.Value.Columns[1].SnakeCaseName);
    }

    [Fact]
    public void ParsePreview_ExplicitHeaderRow_HeaderRowNumberIsPopulated()
    {
        var path = CreateTempExcel(ws =>
        {
            ws.Cell(1, 1).Value = "Title";
            ws.Cell(3, 1).Value = "Id";
            ws.Cell(4, 1).Value = 1;
        });

        var result = _sut.ParsePreview(path, headerRow: 3);

        Assert.True(result.Success);
        Assert.Equal(3, result.Value!.HeaderRowNumber);
    }

    // ── Data starts after the header row ──────────────────────────────────────

    [Fact]
    public void ParsePreview_ExplicitHeaderRow_DataStartsAfterHeaderRow()
    {
        var path = CreateTempExcel(ws =>
        {
            ws.Cell(1, 1).Value = "Report Title";
            ws.Cell(2, 1).Value = "Subtitle";
            ws.Cell(3, 1).Value = "Id";
            ws.Cell(3, 2).Value = "Name";
            ws.Cell(4, 1).Value = 1;
            ws.Cell(4, 2).Value = "Alice";
            ws.Cell(5, 1).Value = 2;
            ws.Cell(5, 2).Value = "Bob";
        });

        var result = _sut.ParsePreview(path, headerRow: 3);

        Assert.True(result.Success);
        var rows = result.Value!.Rows;
        Assert.Equal(2, rows.Count);
        Assert.Equal("1", rows[0]["id"]);
        Assert.Equal("Alice", rows[0]["name"]);
        Assert.Equal("2", rows[1]["id"]);
        Assert.Equal("Bob", rows[1]["name"]);
    }

    [Fact]
    public void ParsePreview_Default_DataStartsAfterRow1()
    {
        var path = CreateTempExcel(ws =>
        {
            ws.Cell(1, 1).Value = "Id";
            ws.Cell(2, 1).Value = 10;
            ws.Cell(3, 1).Value = 20;
        });

        var result = _sut.ParsePreview(path, maxRows: 100);

        Assert.True(result.Success);
        Assert.Equal(2, result.Value!.Rows.Count);
        Assert.Equal("10", result.Value.Rows[0]["id"]);
        Assert.Equal("20", result.Value.Rows[1]["id"]);
    }

    // ── Invalid header row returns failure ─────────────────────────────────────

    [Fact]
    public void ParsePreview_EmptyExplicitHeaderRow_ReturnsFailure()
    {
        var path = CreateTempExcel(ws =>
        {
            ws.Cell(1, 1).Value = "Id";
            ws.Cell(2, 1).Value = 1;
        });

        var result = _sut.ParsePreview(path, headerRow: 99);

        Assert.False(result.Success);
        Assert.Contains("99", result.Error);
    }

    [Fact]
    public void ParsePreview_NegativeHeaderRow_ReturnsFailure()
    {
        var path = CreateTempExcel(ws =>
        {
            ws.Cell(1, 1).Value = "Id";
        });

        var result = _sut.ParsePreview(path, headerRow: -1);

        Assert.False(result.Success);
    }

    // ── SheetPreview.HeaderRowNumber is always populated ──────────────────────

    [Fact]
    public void ParsePreview_HeaderRowNumber_SetOnDefaultBehavior()
    {
        var path = CreateTempExcel(ws =>
        {
            ws.Cell(2, 1).Value = "Id";
            ws.Cell(3, 1).Value = 1;
        });

        var result = _sut.ParsePreview(path);

        Assert.True(result.Success);
        Assert.Equal(2, result.Value!.HeaderRowNumber);
    }

    // ── Sheet selection — default (first sheet) ───────────────────────────────

    [Fact]
    public void ParsePreview_NoSheetArgs_ReadsFirstSheet()
    {
        var path = CreateTempMultiSheetExcel(wb =>
        {
            var ws1 = wb.Worksheets.Add("First");
            ws1.Cell(1, 1).Value = "From First";
            var ws2 = wb.Worksheets.Add("Second");
            ws2.Cell(1, 1).Value = "From Second";
        });

        var result = _sut.ParsePreview(path);

        Assert.True(result.Success);
        Assert.Equal("First", result.Value!.SheetName);
        Assert.Equal("from_first", result.Value.Columns[0].SnakeCaseName);
    }

    // ── Sheet selection — by name ──────────────────────────────────────────────

    [Fact]
    public void ParsePreview_SheetByName_ReadsCorrectSheet()
    {
        var path = CreateTempMultiSheetExcel(wb =>
        {
            var ws1 = wb.Worksheets.Add("Summary");
            ws1.Cell(1, 1).Value = "SummaryCol";
            var ws2 = wb.Worksheets.Add("Global PayGroup GTN Validation");
            ws2.Cell(1, 1).Value = "Id";
            ws2.Cell(1, 2).Value = "Amount";
            ws2.Cell(2, 1).Value = 1;
            ws2.Cell(2, 2).Value = 99.5;
        });

        var result = _sut.ParsePreview(path, sheetName: "Global PayGroup GTN Validation");

        Assert.True(result.Success);
        Assert.Equal("Global PayGroup GTN Validation", result.Value!.SheetName);
        Assert.Equal("id", result.Value.Columns[0].SnakeCaseName);
        Assert.Equal("amount", result.Value.Columns[1].SnakeCaseName);
    }

    [Fact]
    public void ParsePreview_SheetByName_IsCaseInsensitive()
    {
        var path = CreateTempMultiSheetExcel(wb =>
        {
            var ws = wb.Worksheets.Add("DataSheet");
            ws.Cell(1, 1).Value = "Id";
        });

        var result = _sut.ParsePreview(path, sheetName: "datasheet");

        Assert.True(result.Success);
        Assert.Equal("DataSheet", result.Value!.SheetName);
    }

    [Fact]
    public void ParsePreview_SheetByName_NotFound_ReturnsFailure()
    {
        var path = CreateTempMultiSheetExcel(wb =>
        {
            wb.Worksheets.Add("Sheet1");
            wb.Worksheets.Add("Sheet2");
        });

        var result = _sut.ParsePreview(path, sheetName: "DoesNotExist");

        Assert.False(result.Success);
        Assert.Contains("DoesNotExist", result.Error);
        Assert.Contains("Sheet1", result.Error);
        Assert.Contains("Sheet2", result.Error);
    }

    // ── Sheet selection — by index ─────────────────────────────────────────────

    [Fact]
    public void ParsePreview_SheetByIndex_ReadsCorrectSheet()
    {
        var path = CreateTempMultiSheetExcel(wb =>
        {
            var ws1 = wb.Worksheets.Add("First");
            ws1.Cell(1, 1).Value = "FirstCol";
            var ws2 = wb.Worksheets.Add("Second");
            ws2.Cell(1, 1).Value = "SecondCol";
            var ws3 = wb.Worksheets.Add("Third");
            ws3.Cell(1, 1).Value = "ThirdCol";
        });

        var result = _sut.ParsePreview(path, sheetIndex: 2);

        Assert.True(result.Success);
        Assert.Equal("Second", result.Value!.SheetName);
        Assert.Equal("second_col", result.Value.Columns[0].SnakeCaseName);
    }

    [Fact]
    public void ParsePreview_SheetIndex1_ReadsFirstSheet()
    {
        var path = CreateTempMultiSheetExcel(wb =>
        {
            var ws = wb.Worksheets.Add("OnlySheet");
            ws.Cell(1, 1).Value = "Id";
        });

        var result = _sut.ParsePreview(path, sheetIndex: 1);

        Assert.True(result.Success);
        Assert.Equal("OnlySheet", result.Value!.SheetName);
    }

    [Fact]
    public void ParsePreview_SheetIndexOutOfRange_ReturnsFailure()
    {
        var path = CreateTempMultiSheetExcel(wb =>
        {
            wb.Worksheets.Add("OnlySheet");
        });

        var result = _sut.ParsePreview(path, sheetIndex: 5);

        Assert.False(result.Success);
        Assert.Contains("5", result.Error);
        Assert.Contains("1", result.Error);
    }

    [Fact]
    public void ParsePreview_SheetIndexZero_ReturnsFailure()
    {
        var path = CreateTempMultiSheetExcel(wb =>
        {
            wb.Worksheets.Add("Sheet1");
        });

        var result = _sut.ParsePreview(path, sheetIndex: 0);

        Assert.False(result.Success);
    }

    // ── Both sheet and sheet-index specified ──────────────────────────────────

    [Fact]
    public void ParsePreview_BothSheetNameAndIndex_ReturnsFailure()
    {
        var path = CreateTempMultiSheetExcel(wb =>
        {
            wb.Worksheets.Add("Sheet1");
        });

        var result = _sut.ParsePreview(path, sheetName: "Sheet1", sheetIndex: 1);

        Assert.False(result.Success);
        Assert.Contains("--sheet", result.Error);
        Assert.Contains("--sheet-index", result.Error);
    }

    // ── Duplicate column names ─────────────────────────────────────────────────

    [Fact]
    public void ParsePreview_DuplicateColumnNames_DoesNotThrow()
    {
        var path = CreateTempExcel(ws =>
        {
            ws.Cell(1, 1).Value = "Name";
            ws.Cell(1, 2).Value = "Name";
            ws.Cell(2, 1).Value = "Alice";
            ws.Cell(2, 2).Value = "AliceDupe";
        });

        var result = _sut.ParsePreview(path);

        Assert.True(result.Success);
        Assert.Equal(2, result.Value!.Columns.Count);
    }

    // ── ListSheets ─────────────────────────────────────────────────────────────

    [Fact]
    public void ListSheets_MultipleSheets_ReturnsAllNamesInOrder()
    {
        var path = CreateTempMultiSheetExcel(wb =>
        {
            wb.Worksheets.Add("Alpha");
            wb.Worksheets.Add("Beta");
            wb.Worksheets.Add("Gamma");
        });

        var result = _sut.ListSheets(path);

        Assert.True(result.Success);
        Assert.Equal(["Alpha", "Beta", "Gamma"], result.Value!);
    }

    [Fact]
    public void ListSheets_SingleSheet_ReturnsSingleName()
    {
        var path = CreateTempExcel(ws =>
        {
            ws.Cell(1, 1).Value = "Id";
        });

        var result = _sut.ListSheets(path);

        Assert.True(result.Success);
        Assert.Single(result.Value!);
        Assert.Equal("Sheet1", result.Value![0]);
    }

    [Fact]
    public void ListSheets_FileNotFound_ReturnsFailure()
    {
        var result = _sut.ListSheets(@"C:\nonexistent_file_xyz.xlsx");

        Assert.False(result.Success);
        Assert.Contains("nonexistent_file_xyz", result.Error);
    }

    [Fact]
    public void ListSheets_InvalidFile_ReturnsFailure()
    {
        var path = Path.ChangeExtension(Path.GetTempFileName(), ".xlsx");
        _tempFiles.Add(path);
        File.WriteAllText(path, "this is not a valid xlsx file");

        var result = _sut.ListSheets(path);

        Assert.False(result.Success);
    }
}
