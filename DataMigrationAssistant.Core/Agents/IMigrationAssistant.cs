namespace DataMigrationAssistant.Core.Agents;

public interface IMigrationAssistant
{
    Task<MigrationAgentResponse> AskAsync(MigrationAgentContext context, CancellationToken cancellationToken = default);
}
