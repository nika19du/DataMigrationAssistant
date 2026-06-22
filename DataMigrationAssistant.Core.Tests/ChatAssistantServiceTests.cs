using DataMigrationAssistant.Core.Models;
using DataMigrationAssistant.Core.Services;

namespace DataMigrationAssistant.Core.Tests;

// ── Null service ──────────────────────────────────────────────────────────────

public class NullChatAssistantServiceTests
{
    private readonly IChatAssistantService _sut = new NullChatAssistantService();

    [Fact]
    public async Task ChatAsync_ReturnsNonEmptyMessage()
    {
        var result = await _sut.ChatAsync([], new ChatContext());
        Assert.False(string.IsNullOrWhiteSpace(result));
    }

    [Fact]
    public async Task ChatAsync_EmptyHistory_DoesNotThrow()
    {
        var result = await _sut.ChatAsync([], new ChatContext());
        Assert.NotNull(result);
    }

    [Fact]
    public async Task ChatAsync_WithHistory_DoesNotThrow()
    {
        var history = new List<ChatMessage>
        {
            new() { Role = ChatRole.User, Content = "Hello" },
        };

        var result = await _sut.ChatAsync(history, new ChatContext());
        Assert.NotNull(result);
    }
}

// ── Claude service — API key guard ───────────────────────────────────────────

public class ClaudeChatAssistantServiceTests
{
    [Fact]
    public async Task ChatAsync_NullApiKey_ThrowsInvalidOperationException()
    {
        var sut = new ClaudeChatAssistantService(null);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.ChatAsync([], new ChatContext()));
    }

    [Fact]
    public async Task ChatAsync_EmptyApiKey_ThrowsInvalidOperationException()
    {
        var sut = new ClaudeChatAssistantService(string.Empty);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.ChatAsync([], new ChatContext()));
    }

    [Fact]
    public async Task ChatAsync_WhitespaceApiKey_ThrowsInvalidOperationException()
    {
        var sut = new ClaudeChatAssistantService("   ");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.ChatAsync([], new ChatContext()));
    }
}

// ── Ollama service — connection guard ────────────────────────────────────────

public class OllamaChatAssistantServiceTests
{
    [Fact]
    public async Task ChatAsync_OllamaNotRunning_ThrowsInvalidOperationException()
    {
        // Point at a port that refuses connections
        var http = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:19999") };
        var sut  = new OllamaChatAssistantService(http);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.ChatAsync(
                [new ChatMessage { Role = ChatRole.User, Content = "hi" }],
                new ChatContext()));
    }

    [Fact]
    public void DefaultModel_IsDeepseekCoderV2()
    {
        Assert.Equal("deepseek-coder-v2", OllamaChatAssistantService.DefaultModel);
    }
}

// ── Factory ───────────────────────────────────────────────────────────────────

public class ChatAssistantServiceFactoryTests
{
    private static ChatAssistantServiceFactory MakeFactory() =>
        new(
            new ClaudeChatAssistantService(null),
            new OllamaChatAssistantService(new HttpClient()),
            new NullChatAssistantService());

    [Fact]
    public void Create_Claude_ReturnsClaudeService()
    {
        var result = MakeFactory().Create("claude");
        Assert.IsType<ClaudeChatAssistantService>(result);
    }

    [Fact]
    public void Create_Ollama_ReturnsOllamaService()
    {
        var result = MakeFactory().Create("ollama");
        Assert.IsType<OllamaChatAssistantService>(result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown")]
    public void Create_UnknownProvider_ReturnsNullService(string? provider)
    {
        var result = MakeFactory().Create(provider);
        Assert.IsType<NullChatAssistantService>(result);
    }

    [Fact]
    public void Create_ProviderCaseInsensitive_Claude()
    {
        var result = MakeFactory().Create("CLAUDE");
        Assert.IsType<ClaudeChatAssistantService>(result);
    }

    [Fact]
    public void Create_ProviderCaseInsensitive_Ollama()
    {
        var result = MakeFactory().Create("OLLAMA");
        Assert.IsType<OllamaChatAssistantService>(result);
    }
}

// ── ChatMessage model ─────────────────────────────────────────────────────────

public class ChatMessageTests
{
    [Fact]
    public void ChatMessage_DefaultRole_IsUser()
    {
        var msg = new ChatMessage();
        Assert.Equal(default(ChatRole), msg.Role);
    }

    [Fact]
    public void ChatMessage_DefaultContent_IsEmpty()
    {
        var msg = new ChatMessage();
        Assert.Equal(string.Empty, msg.Content);
    }

    [Fact]
    public void ChatMessage_Timestamp_IsSetToUtcNow()
    {
        var before = DateTimeOffset.UtcNow;
        var msg    = new ChatMessage();
        var after  = DateTimeOffset.UtcNow;

        Assert.InRange(msg.Timestamp, before, after);
    }
}

// ── ChatContext model ─────────────────────────────────────────────────────────

public class ChatContextTests
{
    [Fact]
    public void ChatContext_AllProperties_DefaultToNull()
    {
        var ctx = new ChatContext();

        Assert.Null(ctx.Preview);
        Assert.Null(ctx.Schema);
        Assert.Null(ctx.Validation);
        Assert.Null(ctx.AnalysisResult);
        Assert.Null(ctx.NormalizationProposal);
    }
}
