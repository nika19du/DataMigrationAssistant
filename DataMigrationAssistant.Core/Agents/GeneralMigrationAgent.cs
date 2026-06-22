using DataMigrationAssistant.Core.Services;

namespace DataMigrationAssistant.Core.Agents;

public sealed class GeneralMigrationAgent : IMigrationAgent
{
    private readonly IChatAssistantServiceFactory _chatFactory;

    public GeneralMigrationAgent(IChatAssistantServiceFactory chatFactory)
    {
        _chatFactory = chatFactory;
    }

    public string Name => "General Migration Agent";

    // Fallback — always matches last
    public bool CanHandle(string question) => true;

    public async Task<MigrationAgentResponse> HandleAsync(
        MigrationAgentContext context,
        CancellationToken cancellationToken = default)
    {
        var service = _chatFactory.Create(context.Provider);
        var answer  = await service.ChatAsync(context.History, context.ChatContext, cancellationToken);

        return new MigrationAgentResponse
        {
            AgentName         = Name,
            Answer            = answer,
            WasHandledLocally = false,
        };
    }
}
