using DataMigrationAssistant.Core.Models;
using DataMigrationAssistant.Core.Results;

namespace DataMigrationAssistant.Core.Services;

public interface ISeedDiffService
{
    ServiceResult<SeedDiffResult> Diff(
        SeedRecord oldSeed,
        SheetPreview newData,
        TableSchema schema);
}
