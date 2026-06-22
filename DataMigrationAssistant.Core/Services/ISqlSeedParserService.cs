using DataMigrationAssistant.Core.Models;
using DataMigrationAssistant.Core.Results;

namespace DataMigrationAssistant.Core.Services;

public interface ISqlSeedParserService
{
    ServiceResult<SeedRecord> Parse(string sql);
}
