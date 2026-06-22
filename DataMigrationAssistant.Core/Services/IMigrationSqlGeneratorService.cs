using DataMigrationAssistant.Core.Models;
using DataMigrationAssistant.Core.Results;

namespace DataMigrationAssistant.Core.Services;

public interface IMigrationSqlGeneratorService
{
    ServiceResult<string> GenerateMigration(SeedDiffResult diff, TableSchema schema);
}
