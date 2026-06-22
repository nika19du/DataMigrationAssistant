using DataMigrationAssistant.Core.Models;

namespace DataMigrationAssistant.Core.Services;

public interface ISchemaInferenceService
{
    TableSchema InferSchema(SheetPreview preview);
}
