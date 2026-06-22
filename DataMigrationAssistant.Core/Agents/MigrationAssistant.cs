namespace DataMigrationAssistant.Core.Agents;

public sealed class MigrationAssistant : IMigrationAssistant
{
    private readonly IMigrationAgentRouter _router;

    public MigrationAssistant(IMigrationAgentRouter router)
    {
        _router = router;
    }

    public Task<MigrationAgentResponse> AskAsync(
        MigrationAgentContext context,
        CancellationToken cancellationToken = default)
    {
        var agent = _router.Route(context.Question);
        return agent.HandleAsync(context, cancellationToken);
    }
}
