using DataMigrationAssistant.Core.Models;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace DataMigrationAssistant.Core.Services;

public sealed class OllamaDataAnalysisService : IDataAnalysisService
{
    internal const string DefaultModel = "deepseek-coder-v2";
    private const  string ChatPath     = "/api/chat";

    internal static readonly string OllamaSystemPrompt = DataAnalysisPromptBuilder.SystemPrompt;

    private readonly HttpClient _httpClient;
    private readonly string     _model;

    public OllamaDataAnalysisService(HttpClient httpClient, string model = DefaultModel)
    {
        _httpClient = httpClient;
        _model      = model;
    }

    public async Task<DataAnalysisResult> AnalyzeAsync(
        DataAnalysisRequest request,
        CancellationToken cancellationToken = default)
    {
        var userMessage = DataAnalysisPromptBuilder.BuildUserMessage(request);
        var body = new OllamaChatRequest
        {
            Model  = _model,
            Stream = false,
            Format = "json",
            Messages =
            [
                new OllamaMessage { Role = "system", Content = OllamaSystemPrompt },
                new OllamaMessage { Role = "user",   Content = userMessage },
            ],
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

        var chatResponse = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(
            cancellationToken: cancellationToken);

        var content = chatResponse?.Message?.Content ?? string.Empty;
        return DataAnalysisResponseParser.Parse(content);
    }

    internal sealed class OllamaChatRequest
    {
        [JsonPropertyName("model")]    public string            Model    { get; init; } = "";
        [JsonPropertyName("stream")]   public bool              Stream   { get; init; }
        [JsonPropertyName("format")]   public string            Format   { get; init; } = "";
        [JsonPropertyName("messages")] public List<OllamaMessage> Messages { get; init; } = [];
    }

    internal sealed class OllamaMessage
    {
        [JsonPropertyName("role")]    public string Role    { get; init; } = "";
        [JsonPropertyName("content")] public string Content { get; init; } = "";
    }

    internal sealed class OllamaChatResponse
    {
        [JsonPropertyName("message")] public OllamaMessage? Message { get; init; }
    }
}
