namespace DataMigrationAssistant.Core.Models;

public sealed class AiReviewRequest
{
    public AiReviewMode Mode { get; init; } = AiReviewMode.Migration;
    public SheetPreview SheetPreview { get; init; } = new();
    public TableSchema TableSchema { get; init; } = new();
    public ValidationResult ValidationResult { get; init; } = new();
    public SeedDiffResult? SeedDiffResult { get; init; }
    public string? MigrationSql { get; init; }
    public DataAnalysisResult? DataAnalysisResult { get; init; }
}
