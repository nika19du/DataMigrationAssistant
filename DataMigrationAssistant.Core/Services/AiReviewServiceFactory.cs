namespace DataMigrationAssistant.Core.Services;

public sealed class AiReviewServiceFactory : IAiReviewServiceFactory
{
    private readonly NullAiReviewService   _nullService;
    private readonly ClaudeAiReviewService _claudeService;
    private readonly OllamaAiReviewService _ollamaService;

    public AiReviewServiceFactory(
        NullAiReviewService nullService,
        ClaudeAiReviewService claudeService,
        OllamaAiReviewService ollamaService)
    {
        _nullService   = nullService;
        _claudeService = claudeService;
        _ollamaService = ollamaService;
    }

    public IAiReviewService Create(string? provider) =>
        provider?.ToLowerInvariant() switch
        {
            "claude" => _claudeService,
            "ollama" => _ollamaService,
            _        => _nullService,
        };
}
