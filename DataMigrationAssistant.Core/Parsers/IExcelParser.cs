using DataMigrationAssistant.Core.Models;
using DataMigrationAssistant.Core.Results;

namespace DataMigrationAssistant.Core.Parsers;

public interface IExcelParser
{
    ServiceResult<IReadOnlyList<string>> ListSheets(string filePath);

    ServiceResult<SheetPreview> ParsePreview(
        string filePath,
        int maxRows = 10,
        int headerRow = 0,
        string? sheetName = null,
        int? sheetIndex = null);
}
