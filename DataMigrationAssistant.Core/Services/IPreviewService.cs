using DataMigrationAssistant.Core.Models;
using DataMigrationAssistant.Core.Results;

namespace DataMigrationAssistant.Core.Services;

public interface IPreviewService
{
    ServiceResult<SheetPreview> GeneratePreview(
        string filePath,
        int headerRow = 0,
        string? sheetName = null,
        int? sheetIndex = null);
}
