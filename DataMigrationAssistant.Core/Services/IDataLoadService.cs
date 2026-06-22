using DataMigrationAssistant.Core.Models;
using DataMigrationAssistant.Core.Results;

namespace DataMigrationAssistant.Core.Services;

public interface IDataLoadService
{
    ServiceResult<SheetPreview> LoadAllRows(
        string filePath,
        int headerRow = 0,
        string? sheetName = null,
        int? sheetIndex = null);
}
