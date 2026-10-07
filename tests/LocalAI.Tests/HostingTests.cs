using LocalAI.Hosting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LocalAI.Tests;

public class HostingTests
{
    private static ServiceProvider Build(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new ServiceCollection().AddLocalAI(configuration).BuildServiceProvider();
    }

    [Fact]
    public void RegistersEveryCapability()
    {
        using var services = Build(new() { ["Chat:Model"] = "model.gguf", ["Embeddings:Provider"] = "OpenAI", ["Embeddings:Model"] = "text-embedding-3-small" });

        Assert.NotNull(services.GetRequiredService<IChatClient>().GetService<FunctionInvokingChatClient>());
        Assert.NotNull(services.GetRequiredKeyedService<IChatClient>(LocalAIServiceCollectionExtensions.VisionKey));
        Assert.NotNull(services.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>());
        Assert.NotNull(services.GetRequiredService<ISpeechToTextClient>());
        Assert.NotNull(services.GetRequiredService<ITextToSpeechClient>());
        Assert.NotNull(services.GetRequiredService<IReranker>());
        Assert.NotNull(services.GetRequiredService<IVoiceActivityDetector>());

        var models = services.GetRequiredService<LocalAIModels>();
        Assert.Equal(SlotState.NotLoaded, models.Chat.State);
        Assert.True(models.Chat.IsLocal);
        Assert.False(models.Embeddings.IsLocal);
        Assert.Equal("OpenAI: text-embedding-3-small", models.Embeddings.Source);
        Assert.Equal(SlotState.NotConfigured, models.Vision.State);
    }

    [Fact]
    public async Task UnconfiguredCapabilitiesSayWhat()
    {
        using var services = Build([]);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            services.GetRequiredService<IChatClient>().GetResponseAsync("Hi", cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("No chat model is configured", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HostedProvidersWithoutAKeyFailClearly()
    {
        var key = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (!string.IsNullOrEmpty(key))
            return;
        using var services = Build(new() { ["Chat:Provider"] = "OpenAI", ["Chat:Model"] = "gpt-5-mini" });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            services.GetRequiredService<IChatClient>().GetResponseAsync("Hi", cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("OPENAI_API_KEY", error.Message, StringComparison.Ordinal);
    }

    [IntegrationFact]
    public async Task InjectedChatClientStreamsToolCalls()
    {
        using var services = Build(new() { ["Chat:Model"] = "hf://NobodyWho/Qwen_Qwen3-0.6B-GGUF/Qwen_Qwen3-0.6B-Q4_K_M.gguf" });
        var chat = services.GetRequiredService<IChatClient>();
        var clock = AIFunctionFactory.Create(() => "Wednesday 7 October 2026, 08:18", "current_time", "Get the current date and time");

        var contents = new List<AIContent>();
        await foreach (var update in chat.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.System, "Use the tools to answer, then reply in one sentence."), new ChatMessage(ChatRole.User, "What time is it right now?")],
            new ChatOptions { Tools = [clock] }.WithThinking(false), TestContext.Current.CancellationToken))
            contents.AddRange(update.Contents);

        Assert.Contains(contents, c => c is FunctionCallContent { Name: "current_time" });
        Assert.Contains(contents, c => c is FunctionResultContent);
        Assert.Contains("08:18", string.Concat(contents.OfType<TextContent>().Select(t => t.Text)), StringComparison.Ordinal);
    }
}
