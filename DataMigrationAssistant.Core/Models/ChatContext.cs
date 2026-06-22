namespace DataMigrationAssistant.Core.Models;

public sealed class ChatContext
{
    public SheetPreview?            Preview               { get; init; }
    public TableSchema?             Schema                { get; init; }
    public ValidationResult?        Validation            { get; init; }
    public DataAnalysisResult?      AnalysisResult        { get; init; }
    public NormalizationProposal?   NormalizationProposal { get; init; }
    public string?                  GeneratedMigrationSql { get; init; }
    public string?                  GeneratedSeedSql      { get; init; }
    public GtnSeedGenerationResult? GtnResult             { get; init; }
}
