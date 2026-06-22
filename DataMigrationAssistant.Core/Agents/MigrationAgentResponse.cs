namespace DataMigrationAssistant.Core.Agents;

public sealed class MigrationAgentResponse
{
    public required string AgentName { get; init; }
    public required string Answer { get; init; }
    public IReadOnlyList<string> Sources { get; init; } = [];
    public bool WasHandledLocally { get; init; }
    public string? FallbackReason { get; init; }
}
