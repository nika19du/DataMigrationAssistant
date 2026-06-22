namespace DataMigrationAssistant.Core.Agents;

public interface IMigrationAgent
{
    string Name { get; }
    bool CanHandle(string question);
    Task<MigrationAgentResponse> HandleAsync(MigrationAgentContext context, CancellationToken cancellationToken = default);
}
