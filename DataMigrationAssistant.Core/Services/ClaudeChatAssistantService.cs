using Anthropic;
using Anthropic.Core;
using Anthropic.Models.Messages;
using DataMigrationAssistant.Core.Models;

namespace DataMigrationAssistant.Core.Services;

public sealed class ClaudeChatAssistantService : IChatAssistantService
{
    private const string ModelId = "claude-sonnet-4-6";

    private readonly string? _apiKey;

    public ClaudeChatAssistantService()
        => _apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");

    internal ClaudeChatAssistantService(string? apiKey) => _apiKey = apiKey;

    public async Task<string> ChatAsync(
        IReadOnlyList<ChatMessage> history,
        ChatContext context,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            throw new InvalidOperationException("ANTHROPIC_API_KEY environment variable is not set.");

        var client       = new AnthropicClient(new ClientOptions { ApiKey = _apiKey });
        var systemPrompt = ChatContextBuilder.BuildSystemPrompt(context);

        var messages = history
            .Select(m => new MessageParam
            {
                Role    = m.Role == ChatRole.User ? Role.User : Role.Assistant,
                Content = m.Content,
            })
            .ToList();

        var parameters = new MessageCreateParams
        {
            Model     = ModelId,
            MaxTokens = 2048,
            System    = systemPrompt,
            Messages  = messages,
        };

        var response = await client.Messages.Create(parameters, cancellationToken);

        foreach (var block in response.Content)
        {
            if (block.TryPickText(out var textBlock))
                return textBlock.Text;
        }

        return string.Empty;
    }
}
