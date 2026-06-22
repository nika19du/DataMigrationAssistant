using ClosedXML.Excel;
using DataMigrationAssistant.Core.Models;
using DataMigrationAssistant.Core.Results;
using DataMigrationAssistant.Core.Utilities;

namespace DataMigrationAssistant.Core.Parsers;

public sealed class ExcelParser : IExcelParser
{
    public ServiceResult<IReadOnlyList<string>> ListSheets(string filePath)
    {
        if (!File.Exists(filePath))
            return ServiceResult<IReadOnlyList<string>>.Fail($"File not found: {filePath}");

        try
        {
            // Read into memory first so the file handle is released before we parse.
            // This prevents ClosedXML from holding a lock on corrupt files.
            byte[] bytes;
            try { bytes = File.ReadAllBytes(filePath); }
            catch (Exception ex) { return ServiceResult<IReadOnlyList<string>>.Fail($"Failed to read file: {ex.Message}"); }

            using var ms = new MemoryStream(bytes);
            using var workbook = new XLWorkbook(ms);
            IReadOnlyList<string> names = workbook.Worksheets.Select(w => w.Name).ToList();
            return ServiceResult<IReadOnlyList<string>>.Ok(names);
        }
        catch (Exception ex)
        {
            return ServiceResult<IReadOnlyList<string>>.Fail($"Failed to read Excel file: {ex.Message}");
        }
    }

    public ServiceResult<SheetPreview> ParsePreview(
        string filePath,
        int maxRows = 10,
        int headerRow = 0,
        string? sheetName = null,
        int? sheetIndex = null)
    {
        if (!File.Exists(filePath))
            return ServiceResult<SheetPreview>.Fail($"File not found: {filePath}");

        if (headerRow < 0)
            return ServiceResult<SheetPreview>.Fail("Header row must be 0 (auto-detect) or a positive 1-based row number.");

        if (sheetName is not null && sheetIndex is not null)
            return ServiceResult<SheetPreview>.Fail("Specify either --sheet or --sheet-index, not both.");

        try
        {
            using var workbook = new XLWorkbook(filePath);

            IXLWorksheet sheet;

            if (sheetName is not null)
            {
                var found = workbook.Worksheets.FirstOrDefault(
                    w => w.Name.Equals(sheetName, StringComparison.OrdinalIgnoreCase));
                if (found is null)
                {
                    var available = string.Join(", ", workbook.Worksheets.Select(w => $"\"{w.Name}\""));
                    return ServiceResult<SheetPreview>.Fail(
                        $"Sheet \"{sheetName}\" not found. Available sheets: {available}");
                }
                sheet = found;
            }
            else if (sheetIndex is not null)
            {
                var sheets = workbook.Worksheets.ToList();
                if (sheetIndex.Value < 1 || sheetIndex.Value > sheets.Count)
                    return ServiceResult<SheetPreview>.Fail(
                        $"Sheet index {sheetIndex.Value} is out of range. The workbook has {sheets.Count} sheet(s).");
                sheet = sheets[sheetIndex.Value - 1];
            }
            else
            {
                sheet = workbook.Worksheets.First();
            }

            IXLRow resolvedHeaderRow;

            if (headerRow == 0)
            {
                var firstUsed = sheet.FirstRowUsed();
                if (firstUsed is null)
                    return ServiceResult<SheetPreview>.Fail($"Sheet \"{sheet.Name}\" appears to be empty.");
                resolvedHeaderRow = firstUsed;
            }
            else
            {
                var candidate = sheet.Row(headerRow);
                if (!candidate.CellsUsed().Any())
                    return ServiceResult<SheetPreview>.Fail(
                        $"Row {headerRow} in sheet \"{sheet.Name}\" has no content and cannot be used as a header row.");
                resolvedHeaderRow = candidate;
            }

            var headerRowNumber = resolvedHeaderRow.RowNumber();

            var columns = resolvedHeaderRow.CellsUsed()
                .Select((cell, i) => new ColumnInfo
                {
                    Index = i,
                    Name = cell.GetString(),
                    SnakeCaseName = NamingUtility.ToSnakeCase(cell.GetString())
                })
                .ToList();

            var lastRow = sheet.LastRowUsed()?.RowNumber() ?? headerRowNumber;
            var totalDataRows = Math.Max(0, lastRow - headerRowNumber);

            var endRow = (int)Math.Min(lastRow, (long)headerRowNumber + maxRows);
            var firstColumnNumber = resolvedHeaderRow.FirstCellUsed()!.Address.ColumnNumber;

            var rows = new List<IReadOnlyDictionary<string, string?>>();
            for (int r = headerRowNumber + 1; r <= endRow; r++)
            {
                var row = new Dictionary<string, string?>();
                foreach (var col in columns)
                {
                    var cell = sheet.Cell(r, col.Index + firstColumnNumber);
                    row[col.SnakeCaseName] = cell.IsEmpty() ? null : cell.GetString();
                }
                rows.Add(row);
            }

            var preview = new SheetPreview
            {
                SheetName = sheet.Name,
                FilePath = filePath,
                Columns = columns,
                Rows = rows,
                TotalRowCount = totalDataRows,
                HeaderRowNumber = headerRowNumber,
            };

            return ServiceResult<SheetPreview>.Ok(preview);
        }
        catch (Exception ex)
        {
            return ServiceResult<SheetPreview>.Fail($"Failed to parse Excel file: {ex.Message}");
        }
    }
}
