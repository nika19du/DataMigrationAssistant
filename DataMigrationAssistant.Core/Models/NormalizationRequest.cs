namespace DataMigrationAssistant.Core.Models;

public sealed class NormalizationRequest
{
    public SheetPreview SheetPreview { get; init; } = new();
    public TableSchema FlatSchema { get; init; } = new();
}
