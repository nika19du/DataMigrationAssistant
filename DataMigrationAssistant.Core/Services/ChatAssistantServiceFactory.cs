namespace DataMigrationAssistant.Core.Services;

public sealed class ChatAssistantServiceFactory : IChatAssistantServiceFactory
{
    private readonly ClaudeChatAssistantService _claude;
    private readonly OllamaChatAssistantService _ollama;
    private readonly NullChatAssistantService   _null;

    public ChatAssistantServiceFactory(
        ClaudeChatAssistantService claude,
        OllamaChatAssistantService ollama,
        NullChatAssistantService   nullService)
    {
        _claude = claude;
        _ollama = ollama;
        _null   = nullService;
    }

    public IChatAssistantService Create(string? provider) =>
        provider?.ToLowerInvariant() switch
        {
            "claude" => _claude,
            "ollama" => _ollama,
            _        => _null,
        };
}
