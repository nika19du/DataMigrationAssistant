using DataMigrationAssistant.Core.Models;
using DataMigrationAssistant.Core.Parsers;
using DataMigrationAssistant.Core.Results;

namespace DataMigrationAssistant.Core.Services;

public sealed class DataLoadService : IDataLoadService
{
    private readonly IExcelParser _excelParser;

    public DataLoadService(IExcelParser excelParser)
    {
        _excelParser = excelParser;
    }

    public ServiceResult<SheetPreview> LoadAllRows(
        string filePath,
        int headerRow = 0,
        string? sheetName = null,
        int? sheetIndex = null)
    {
        return _excelParser.ParsePreview(filePath, maxRows: int.MaxValue, headerRow: headerRow,
            sheetName: sheetName, sheetIndex: sheetIndex);
    }
}
