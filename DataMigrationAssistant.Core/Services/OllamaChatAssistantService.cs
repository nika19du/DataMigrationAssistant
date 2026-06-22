using DataMigrationAssistant.Core.Models;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace DataMigrationAssistant.Core.Services;

public sealed class OllamaChatAssistantService : IChatAssistantService
{
    internal const string DefaultModel = "deepseek-coder-v2";
    private const string ChatPath = "/api/chat";

    private readonly HttpClient _httpClient;
    private readonly string _model;

    public OllamaChatAssistantService(HttpClient httpClient, string model = DefaultModel)
    {
        _httpClient = httpClient;
        _model      = model;
    }

    public async Task<string> ChatAsync(
        IReadOnlyList<ChatMessage> history,
        ChatContext context,
        CancellationToken cancellationToken = default)
    {
        var systemPrompt = ChatContextBuilder.BuildSystemPrompt(context);

        var messages = new List<OllamaMsg>
        {
            new() { Role = "system", Content = systemPrompt },
        };

        foreach (var msg in history)
        {
            messages.Add(new OllamaMsg
            {
                Role    = msg.Role == ChatRole.User ? "user" : "assistant",
                Content = msg.Content,
            });
        }

        var body = new OllamaChatReq
        {
            Model    = _model,
            Stream   = false,
            Messages = messages,
        };

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsJsonAsync(ChatPath, body, cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException("Ollama is not running. Start it with: ollama serve");
        }

        response.EnsureSuccessStatusCode();

        var chatResponse = await response.Content.ReadFromJsonAsync<OllamaChatResp>(
            cancellationToken: cancellationToken);

        return chatResponse?.Message?.Content ?? string.Empty;
    }

    internal sealed class OllamaChatReq
    {
        [JsonPropertyName("model")]    public string       Model    { get; init; } = "";
        [JsonPropertyName("stream")]   public bool         Stream   { get; init; }
        [JsonPropertyName("messages")] public List<OllamaMsg> Messages { get; init; } = [];
    }

    internal sealed class OllamaMsg
    {
        [JsonPropertyName("role")]    public string Role    { get; init; } = "";
        [JsonPropertyName("content")] public string Content { get; init; } = "";
    }

    internal sealed class OllamaChatResp
    {
        [JsonPropertyName("message")] public OllamaMsg? Message { get; init; }
    }
}
