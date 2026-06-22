using DataMigrationAssistant.Core.Models;

namespace DataMigrationAssistant.Core.Services;

public interface IValidationService
{
    ValidationResult Validate(SheetPreview preview, TableSchema schema);
}
