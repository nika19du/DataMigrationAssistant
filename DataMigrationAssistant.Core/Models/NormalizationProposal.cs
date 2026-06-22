namespace DataMigrationAssistant.Core.Models;

public sealed class NormalizationProposal
{
    public string Reasoning { get; init; } = string.Empty;
    public IReadOnlyList<ProposedTable> Tables { get; init; } = [];
    public string CombinedMigrationSql { get; init; } = string.Empty;
    public string CombinedSeedSql { get; init; } = string.Empty;
    public string MarkdownReport { get; init; } = string.Empty;
}
