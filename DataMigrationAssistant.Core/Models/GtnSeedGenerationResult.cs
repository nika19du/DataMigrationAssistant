namespace DataMigrationAssistant.Core.Models;

public sealed class GtnSeedGenerationResult
{
    public string ScenariosSql { get; init; } = string.Empty;
    public int ScenarioCount { get; init; }
    public IReadOnlyList<GtnSeedWarning> Warnings { get; init; } = [];
}
