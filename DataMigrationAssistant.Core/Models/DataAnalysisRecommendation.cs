namespace DataMigrationAssistant.Core.Models;

public sealed class DataAnalysisRecommendation
{
    public string Priority    { get; init; } = string.Empty;
    public string Type        { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
}
