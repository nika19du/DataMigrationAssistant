using DataMigrationAssistant.Core.Models;
using DataMigrationAssistant.Core.Parsers;
using DataMigrationAssistant.Core.Results;

namespace DataMigrationAssistant.Core.Services;

public sealed class PreviewService : IPreviewService
{
    private readonly IExcelParser _excelParser;

    public PreviewService(IExcelParser excelParser)
    {
        _excelParser = excelParser;
    }

    public ServiceResult<SheetPreview> GeneratePreview(
        string filePath,
        int headerRow = 0,
        string? sheetName = null,
        int? sheetIndex = null)
    {
        return _excelParser.ParsePreview(filePath, maxRows: 10, headerRow: headerRow,
            sheetName: sheetName, sheetIndex: sheetIndex);
    }
}
