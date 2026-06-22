namespace DataMigrationAssistant.Core.Models;

public sealed class AiReviewResult
{
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyList<AiReviewRisk> Risks { get; init; } = [];
    public IReadOnlyList<AiReviewRecommendation> Recommendations { get; init; } = [];
}
