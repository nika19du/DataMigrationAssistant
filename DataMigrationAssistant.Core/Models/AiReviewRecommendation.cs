namespace DataMigrationAssistant.Core.Models;

public sealed class AiReviewRecommendation
{
    public string Priority { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string? Action { get; init; }
    public string? Evidence { get; init; }
    /// <summary>The column this recommendation applies to, as declared by the AI (may be null).</summary>
    public string? Column { get; init; }
}
