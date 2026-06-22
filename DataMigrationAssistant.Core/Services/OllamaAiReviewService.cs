using DataMigrationAssistant.Core.Models;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace DataMigrationAssistant.Core.Services;

public sealed class OllamaAiReviewService : IAiReviewService
{
    internal const string DefaultModel = "deepseek-coder-v2";
    private const string ChatPath = "/api/chat";

    internal static readonly string OllamaSystemPrompt = AiReviewPromptBuilder.SystemPrompt;

    private readonly HttpClient _httpClient;
    private readonly string _model;

    public OllamaAiReviewService(HttpClient httpClient, string model = DefaultModel)
    {
        _httpClient = httpClient;
        _model = model;
    }

    public async Task<AiReviewResult> ReviewAsync(AiReviewRequest request, CancellationToken cancellationToken = default)
    {
        var userMessage  = AiReviewPromptBuilder.BuildUserMessage(request);
        var systemPrompt = AiReviewPromptBuilder.GetSystemPrompt(request.Mode);
        var body = new OllamaChatRequest
        {
            Model  = _model,
            Stream = false,
            Format = "json",
            Messages =
            [
                new OllamaMessage { Role = "system", Content = systemPrompt },
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

        var chatResponse = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(cancellationToken: cancellationToken);
        var content = chatResponse?.Message?.Content ?? string.Empty;
        var raw              = AiReviewResponseParser.Parse(content);
        var evidenceFiltered = AiReviewEvidenceFilter.Apply(raw, request.Mode);
        var claimValidated   = ContradictionEngine.Apply(evidenceFiltered, request);
        return AiReviewGroundedFallback.Apply(claimValidated, request);
    }

    internal sealed class OllamaChatRequest
    {
        [JsonPropertyName("model")]    public string Model    { get; init; } = "";
        [JsonPropertyName("stream")]   public bool   Stream   { get; init; }
        [JsonPropertyName("format")]   public string Format   { get; init; } = "";
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
