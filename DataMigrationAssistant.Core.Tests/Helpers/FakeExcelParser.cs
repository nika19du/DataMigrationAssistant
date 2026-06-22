using DataMigrationAssistant.Core.Models;
using DataMigrationAssistant.Core.Parsers;
using DataMigrationAssistant.Core.Results;

namespace DataMigrationAssistant.Core.Tests.Helpers;

/// <summary>
/// Simulates ExcelParser behaviour: returns Math.Min(availableRows, maxRows) rows.
/// Captures the last call arguments so tests can assert them.
/// </summary>
internal sealed class FakeExcelParser : IExcelParser
{
    private readonly int _availableRows;

    public int     LastMaxRows   { get; private set; }
    public int     LastHeaderRow { get; private set; }
    public string? LastSheetName { get; private set; }
    public int?    LastSheetIndex { get; private set; }

    public FakeExcelParser(int availableRows) => _availableRows = availableRows;

    public ServiceResult<IReadOnlyList<string>> ListSheets(string filePath) =>
        ServiceResult<IReadOnlyList<string>>.Ok(new List<string> { "TestSheet" });

    public ServiceResult<SheetPreview> ParsePreview(
        string filePath,
        int maxRows = 10,
        int headerRow = 0,
        string? sheetName = null,
        int? sheetIndex = null)
    {
        LastMaxRows    = maxRows;
        LastHeaderRow  = headerRow;
        LastSheetName  = sheetName;
        LastSheetIndex = sheetIndex;

        var count = maxRows == int.MaxValue ? _availableRows : Math.Min(_availableRows, maxRows);

        var columns = new List<ColumnInfo>
        {
            new() { Index = 0, Name = "id", SnakeCaseName = "id" }
        };

        var rows = Enumerable.Range(1, count)
            .Select(i => (IReadOnlyDictionary<string, string?>)
                new Dictionary<string, string?> { ["id"] = i.ToString() })
            .ToList();

        return ServiceResult<SheetPreview>.Ok(new SheetPreview
        {
            SheetName       = sheetName ?? "TestSheet",
            FilePath        = filePath,
            Columns         = columns,
            Rows            = rows,
            TotalRowCount   = _availableRows,
            HeaderRowNumber = headerRow == 0 ? 1 : headerRow,
        });
    }
}
