using DataMigrationAssistant.Core.Models;
using DataMigrationAssistant.Core.Services;
using System.Net;
using System.Text;
using System.Text.Json;

namespace DataMigrationAssistant.Core.Tests;

// ─── Helpers ────────────────────────────────────────────────────────────────

internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send;

    public FakeHttpMessageHandler(HttpResponseMessage response)
        : this((_, _) => Task.FromResult(response)) { }

    public FakeHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        => _send = send;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => _send(request, cancellationToken);
}

// ─── Provider selection ──────────────────────────────────────────────────────

public class AiReviewServiceFactoryTests
{
    private static AiReviewServiceFactory CreateFactory() => new(
        new NullAiReviewService(),
        new ClaudeAiReviewService(null),
        new OllamaAiReviewService(new HttpClient()));

    [Theory]
    [InlineData(null)]
    [InlineData("null")]
    [InlineData("")]
    [InlineData("unknown")]
    public void Create_UnknownOrNullProvider_ReturnsNullAiReviewService(string? provider)
    {
        var result = CreateFactory().Create(provider);
        Assert.IsType<NullAiReviewService>(result);
    }

    [Fact]
    public void Create_Claude_ReturnsClaudeAiReviewService()
    {
        var result = CreateFactory().Create("claude");
        Assert.IsType<ClaudeAiReviewService>(result);
    }

    [Fact]
    public void Create_Ollama_ReturnsOllamaAiReviewService()
    {
        var result = CreateFactory().Create("ollama");
        Assert.IsType<OllamaAiReviewService>(result);
    }

    [Theory]
    [InlineData("CLAUDE")]
    [InlineData("Claude")]
    [InlineData("OLLAMA")]
    [InlineData("Ollama")]
    public void Create_IsCaseInsensitive(string provider)
    {
        var factory = CreateFactory();
        var result  = factory.Create(provider);
        Assert.True(result is ClaudeAiReviewService or OllamaAiReviewService);
    }
}

// ─── Ollama request payload ──────────────────────────────────────────────────

public class OllamaAiReviewServiceRequestTests
{
    private static HttpResponseMessage MakeOllamaResponse(string reviewJson)
    {
        var body = JsonSerializer.Serialize(new
        {
            message = new { role = "assistant", content = reviewJson }
        });
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }

    private static readonly string EmptyReviewJson =
        """{"summary":"","risks":[],"recommendations":[]}""";

    [Fact]
    public async Task ReviewAsync_SendsPostToApiChat()
    {
        HttpRequestMessage? captured = null;
        var handler = new FakeHttpMessageHandler((req, _) =>
        {
            captured = req;
            return Task.FromResult(MakeOllamaResponse(EmptyReviewJson));
        });
        var sut = new OllamaAiReviewService(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") });

        await sut.ReviewAsync(new AiReviewRequest());

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Contains("/api/chat", captured.RequestUri!.ToString());
    }

    [Fact]
    public async Task ReviewAsync_RequestBody_UsesDefaultModel()
    {
        string? bodyJson = null;
        var handler = new FakeHttpMessageHandler(async (req, _) =>
        {
            bodyJson = await req.Content!.ReadAsStringAsync();
            return MakeOllamaResponse(EmptyReviewJson);
        });
        var sut = new OllamaAiReviewService(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") });

        await sut.ReviewAsync(new AiReviewRequest());

        using var doc = JsonDocument.Parse(bodyJson!);
        Assert.Equal(OllamaAiReviewService.DefaultModel,
            doc.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public async Task ReviewAsync_RequestBody_UsesCustomModel()
    {
        string? bodyJson = null;
        var handler = new FakeHttpMessageHandler(async (req, _) =>
        {
            bodyJson = await req.Content!.ReadAsStringAsync();
            return MakeOllamaResponse(EmptyReviewJson);
        });
        var sut = new OllamaAiReviewService(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") },
            model: "llama3");

        await sut.ReviewAsync(new AiReviewRequest());

        using var doc = JsonDocument.Parse(bodyJson!);
        Assert.Equal("llama3", doc.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public async Task ReviewAsync_RequestBody_HasStreamFalse()
    {
        string? bodyJson = null;
        var handler = new FakeHttpMessageHandler(async (req, _) =>
        {
            bodyJson = await req.Content!.ReadAsStringAsync();
            return MakeOllamaResponse(EmptyReviewJson);
        });
        var sut = new OllamaAiReviewService(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") });

        await sut.ReviewAsync(new AiReviewRequest());

        using var doc = JsonDocument.Parse(bodyJson!);
        Assert.False(doc.RootElement.GetProperty("stream").GetBoolean());
    }

    [Fact]
    public async Task ReviewAsync_RequestBody_HasFormatJson()
    {
        string? bodyJson = null;
        var handler = new FakeHttpMessageHandler(async (req, _) =>
        {
            bodyJson = await req.Content!.ReadAsStringAsync();
            return MakeOllamaResponse(EmptyReviewJson);
        });
        var sut = new OllamaAiReviewService(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") });

        await sut.ReviewAsync(new AiReviewRequest());

        using var doc = JsonDocument.Parse(bodyJson!);
        Assert.Equal("json", doc.RootElement.GetProperty("format").GetString());
    }

    [Fact]
    public async Task ReviewAsync_RequestBody_SystemMessageIncludesSqlCommentsInstruction()
    {
        string? bodyJson = null;
        var handler = new FakeHttpMessageHandler(async (req, _) =>
        {
            bodyJson = await req.Content!.ReadAsStringAsync();
            return MakeOllamaResponse(EmptyReviewJson);
        });
        var sut = new OllamaAiReviewService(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") });

        await sut.ReviewAsync(new AiReviewRequest());

        using var doc = JsonDocument.Parse(bodyJson!);
        var messages = doc.RootElement.GetProperty("messages").EnumerateArray().ToList();
        var systemMsg = messages.First(m => m.GetProperty("role").GetString() == "system");
        var content = systemMsg.GetProperty("content").GetString();
        Assert.Contains("SQL comments", content);
        Assert.Contains("--", content);
    }

    [Fact]
    public async Task ReviewAsync_RequestBody_HasBothSystemAndUserMessages()
    {
        string? bodyJson = null;
        var handler = new FakeHttpMessageHandler(async (req, _) =>
        {
            bodyJson = await req.Content!.ReadAsStringAsync();
            return MakeOllamaResponse(EmptyReviewJson);
        });
        var sut = new OllamaAiReviewService(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") });

        await sut.ReviewAsync(new AiReviewRequest());

        using var doc = JsonDocument.Parse(bodyJson!);
        var roles = doc.RootElement.GetProperty("messages")
            .EnumerateArray()
            .Select(m => m.GetProperty("role").GetString())
            .ToList();
        Assert.Contains("system", roles);
        Assert.Contains("user", roles);
    }
}

// ─── Ollama response parsing ─────────────────────────────────────────────────

public class OllamaAiReviewServiceResponseTests
{
    private static OllamaAiReviewService BuildSut(HttpResponseMessage response)
    {
        var handler = new FakeHttpMessageHandler(response);
        return new OllamaAiReviewService(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") });
    }

    private static HttpResponseMessage MakeOllamaResponse(string reviewJson)
    {
        var body = JsonSerializer.Serialize(new
        {
            message = new { role = "assistant", content = reviewJson }
        });
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }

    [Fact]
    public async Task ReviewAsync_ParsesValidResponse_ReturnsSummary()
    {
        var reviewJson = """{"summary":"All looks good.","risks":[],"recommendations":[]}""";
        var sut = BuildSut(MakeOllamaResponse(reviewJson));

        var result = await sut.ReviewAsync(new AiReviewRequest());

        Assert.Equal("All looks good.", result.Summary);
    }

    [Fact]
    public async Task ReviewAsync_ParsesRisks()
    {
        var reviewJson = """
            {
              "summary":"s",
              "risks":[{"level":"HIGH","description":"Large table"}],
              "recommendations":[]
            }
            """;
        var sut = BuildSut(MakeOllamaResponse(reviewJson));

        var result = await sut.ReviewAsync(new AiReviewRequest());

        Assert.Single(result.Risks);
        Assert.Equal("HIGH",        result.Risks[0].Level);
        Assert.Equal("Large table", result.Risks[0].Description);
    }

    [Fact]
    public async Task ReviewAsync_ParsesRecommendations()
    {
        var reviewJson = """
            {
              "summary":"s",
              "risks":[],
              "recommendations":[{"priority":"LOW","description":"Add index","action":"CREATE INDEX ..."}]
            }
            """;
        var sut = BuildSut(MakeOllamaResponse(reviewJson));

        var result = await sut.ReviewAsync(new AiReviewRequest());

        Assert.Single(result.Recommendations);
        Assert.Equal("LOW",           result.Recommendations[0].Priority);
        Assert.Equal("Add index",     result.Recommendations[0].Description);
        Assert.Equal("CREATE INDEX ...", result.Recommendations[0].Action);
    }

    [Fact]
    public async Task ReviewAsync_EmptyMessageContent_ReturnsFallback()
    {
        var body = JsonSerializer.Serialize(new
        {
            message = new { role = "assistant", content = "" }
        });
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        var sut = BuildSut(response);

        var result = await sut.ReviewAsync(new AiReviewRequest());

        Assert.Equal("No response from AI.", result.Summary);
    }

    [Fact]
    public async Task ReviewAsync_InvalidJsonContent_ReturnsFallback()
    {
        var body = JsonSerializer.Serialize(new
        {
            message = new { role = "assistant", content = "not valid json {{{" }
        });
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        var sut = BuildSut(response);

        var result = await sut.ReviewAsync(new AiReviewRequest());

        Assert.Equal("Could not parse AI response.", result.Summary);
    }
}

// ─── Ollama connection failure ───────────────────────────────────────────────

public class OllamaAiReviewServiceConnectionTests
{
    [Fact]
    public async Task ReviewAsync_ConnectionRefused_ThrowsWithClearMessage()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("Connection refused")));
        var sut = new OllamaAiReviewService(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.ReviewAsync(new AiReviewRequest()));

        Assert.Equal("Ollama is not running. Start it with: ollama serve", ex.Message);
    }

    [Fact]
    public async Task ReviewAsync_NetworkFailure_ThrowsWithClearMessage()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            Task.FromException<HttpResponseMessage>(
                new HttpRequestException("No route to host",
                    new System.Net.Sockets.SocketException())));
        var sut = new OllamaAiReviewService(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.ReviewAsync(new AiReviewRequest()));

        Assert.Equal("Ollama is not running. Start it with: ollama serve", ex.Message);
    }
}

// ─── OllamaSystemPrompt ──────────────────────────────────────────────────────

public class OllamaSystemPromptTests
{
    [Fact]
    public void OllamaSystemPrompt_ContainsBaseSystemPrompt()
    {
        Assert.Contains(AiReviewPromptBuilder.SystemPrompt, OllamaAiReviewService.OllamaSystemPrompt);
    }

    [Fact]
    public void OllamaSystemPrompt_ContainsSqlCommentsInstruction()
    {
        Assert.Contains(
            "Lines starting with -- are SQL comments and must not be treated as executable SQL.",
            OllamaAiReviewService.OllamaSystemPrompt);
    }
}
