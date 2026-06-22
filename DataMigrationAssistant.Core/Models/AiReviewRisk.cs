namespace DataMigrationAssistant.Core.Models;

public sealed class AiReviewRisk
{
    public string Level { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string? Evidence { get; init; }
    /// <summary>The column this risk applies to, as declared by the AI (may be null).</summary>
    public string? Column { get; init; }
}
