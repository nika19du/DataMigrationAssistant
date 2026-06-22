namespace DataMigrationAssistant.Core.Services;

public sealed class NormalizationServiceFactory : INormalizationServiceFactory
{
    private readonly NullNormalizationService   _nullService;
    private readonly ClaudeNormalizationService _claudeService;
    private readonly OllamaNormalizationService _ollamaService;

    public NormalizationServiceFactory(
        NullNormalizationService   nullService,
        ClaudeNormalizationService claudeService,
        OllamaNormalizationService ollamaService)
    {
        _nullService   = nullService;
        _claudeService = claudeService;
        _ollamaService = ollamaService;
    }

    public INormalizationProposalService Create(string? provider) =>
        provider?.ToLowerInvariant() switch
        {
            "claude" => _claudeService,
            "ollama" => _ollamaService,
            _        => _nullService,
        };
}
