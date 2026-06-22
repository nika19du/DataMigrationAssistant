namespace DataMigrationAssistant.Core.Services;

public sealed class DataAnalysisServiceFactory : IDataAnalysisServiceFactory
{
    private readonly DeterministicDataAnalysisService _deterministicService;
    private readonly ClaudeDataAnalysisService        _claudeService;
    private readonly OllamaDataAnalysisService        _ollamaService;

    public DataAnalysisServiceFactory(
        DeterministicDataAnalysisService deterministicService,
        ClaudeDataAnalysisService        claudeService,
        OllamaDataAnalysisService        ollamaService)
    {
        _deterministicService = deterministicService;
        _claudeService        = claudeService;
        _ollamaService        = ollamaService;
    }

    public IDataAnalysisService Create(string? provider) =>
        provider?.ToLowerInvariant() switch
        {
            "claude" => _claudeService,
            "ollama" => _ollamaService,
            _        => _deterministicService,
        };
}
