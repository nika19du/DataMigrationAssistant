using DataMigrationAssistant.Core.Models;

namespace DataMigrationAssistant.Core.Services;

public sealed class NullChatAssistantService : IChatAssistantService
{
    public Task<string> ChatAsync(
        IReadOnlyList<ChatMessage> history,
        ChatContext context,
        CancellationToken cancellationToken = default)
        => Task.FromResult(
            "No AI provider is configured. Select Claude or Ollama from the provider dropdown to enable the chat assistant.");
}
