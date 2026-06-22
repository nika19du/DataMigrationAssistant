using DataMigrationAssistant.Core.Models;
using DataMigrationAssistant.Core.Results;

namespace DataMigrationAssistant.Core.Services;

public interface IUpsertSqlGeneratorService
{
    ServiceResult<string> GenerateUpsert(SheetPreview preview, TableSchema schema);
}
