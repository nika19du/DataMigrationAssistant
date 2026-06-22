using DataMigrationAssistant.Core.Models;

namespace DataMigrationAssistant.Core.Services;

public interface IChatAssistantService
{
    Task<string> ChatAsync(
        IReadOnlyList<ChatMessage> history,
        ChatContext context,
        CancellationToken cancellationToken = default);
}
