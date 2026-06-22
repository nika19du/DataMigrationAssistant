using DataMigrationAssistant.Core.Models;

namespace DataMigrationAssistant.Core.Agents;

public sealed class MigrationAgentContext
{
    public required string Question { get; init; }
    public IReadOnlyList<ChatMessage> History { get; init; } = [];
    public required ChatContext ChatContext { get; init; }
    public string? Provider { get; init; }
}
