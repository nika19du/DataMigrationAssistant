using DataMigrationAssistant.Core.Models;

namespace DataMigrationAssistant.Core.Services;

public interface ISqlGeneratorService
{
    string GenerateCreateTable(TableSchema schema);
}
