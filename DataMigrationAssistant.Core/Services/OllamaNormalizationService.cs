using DataMigrationAssistant.Core.Models;
using DataMigrationAssistant.Core.Results;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DataMigrationAssistant.Core.Services;

public sealed class OllamaNormalizationService : INormalizationProposalService
{
    internal const string DefaultModel = "deepseek-coder-v2";
    private const string GeneratePath = "/api/generate";

    internal static readonly string OllamaSystemPrompt = NormalizationPromptBuilder.SystemPrompt;

    private readonly HttpClient _httpClient;
    internal readonly string _model;

    public OllamaNormalizationService(HttpClient httpClient, string model = DefaultModel)
    {
        _httpClient = httpClient;
        _model      = model;
    }

    public async Task<ServiceResult<NormalizationProposal>> ProposeAsync(
        NormalizationRequest request,
        CancellationToken cancellationToken = default)
    {
        var userMessage = NormalizationPromptBuilder.BuildUserMessage(request);
        var body = new OllamaGenerateRequest
        {
            Model  = _model,
            Prompt = BuildGeneratePrompt(userMessage),
            Stream = false,
            Format = "json",
        };

        Console.Error.WriteLine($"[DIAG] Model: {_model}");
        Console.Error.WriteLine("[DIAG] Sending request to Ollama...");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsJsonAsync(GeneratePath, body, cancellationToken);
        }
        catch (TaskCanceledException)
        {
            throw new InvalidOperationException(
                "Ollama request timed out. Try a smaller model, reduce sample rows, or increase timeout.");
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException("Ollama is not running. Start it with: ollama serve");
        }

        if (!response.IsSuccessStatusCode)
        {
            string errorBody;
            try   { errorBody = await response.Content.ReadAsStringAsync(cancellationToken); }
            catch { errorBody = "(could not read response body)"; }
            throw new InvalidOperationException(
                $"Ollama returned HTTP {(int)response.StatusCode} {response.ReasonPhrase}. Body: {errorBody}");
        }

        OllamaGenerateResponse? generateResponse;
        try
        {
            generateResponse = await response.Content.ReadFromJsonAsync<OllamaGenerateResponse>(
                cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("Ollama returned an invalid response.");
        }

        var content = generateResponse?.Response ?? string.Empty;

        Console.Error.WriteLine("[DIAG] Ollama raw response:");
        Console.Error.WriteLine(content.Length > 3000 ? content[..3000] + "...(truncated)" : content);
        Console.Error.WriteLine($"[DIAG] Response length: {content.Length} chars");

        if (!string.IsNullOrWhiteSpace(content) && !content.TrimStart().StartsWith('{'))
            return ServiceResult<NormalizationProposal>.Fail(
                "Ollama did not return the required normalization JSON.");

        return NormalizationResponseParser.Parse(content);
    }

    internal static string BuildGeneratePrompt(string userMessage) =>
        OllamaSystemPrompt +
        "\nAdditionally: your response must NOT use \"response\" as a top-level JSON key. " +
        "The only allowed top-level keys are \"reasoning\" and \"tables\".\n\n" +
        userMessage;

    internal sealed class OllamaGenerateRequest
    {
        [JsonPropertyName("model")]  public string Model  { get; init; } = "";
        [JsonPropertyName("prompt")] public string Prompt { get; init; } = "";
        [JsonPropertyName("stream")] public bool   Stream { get; init; }
        [JsonPropertyName("format")] public string Format { get; init; } = "";
    }

    internal sealed class OllamaGenerateResponse
    {
        [JsonPropertyName("response")] public string? Response { get; init; }
    }
}
