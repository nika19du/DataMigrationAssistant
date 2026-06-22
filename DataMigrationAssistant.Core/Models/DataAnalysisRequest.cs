namespace DataMigrationAssistant.Core.Models;

public sealed class DataAnalysisRequest
{
    public SheetPreview      SheetPreview     { get; init; } = new();
    public TableSchema       TableSchema      { get; init; } = new();
    public ValidationResult  ValidationResult { get; init; } = new();
}
