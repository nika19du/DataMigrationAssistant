using DataMigrationAssistant.Core.Models;

namespace DataMigrationAssistant.Core.Services;

public interface ISeedSqlGeneratorService
{
    string GenerateSeed(SheetPreview preview, TableSchema schema);
}
