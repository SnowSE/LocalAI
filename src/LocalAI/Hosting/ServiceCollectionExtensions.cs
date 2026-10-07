using LocalAI;
using LocalAI.Hosting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

public static class LocalAIServiceCollectionExtensions
{
    /// <summary>The service key of the vision <see cref="IChatClient"/>.</summary>
    public const string VisionKey = "vision";

    /// <summary>
    /// Register the models in <paramref name="configuration"/> (usually the <c>LocalAI</c> section)
    /// as Microsoft.Extensions.AI services:
    /// <list type="bullet">
    /// <item><see cref="IChatClient"/>, with function invocation, so tools run automatically;</item>
    /// <item>a keyed <see cref="IChatClient"/> under <see cref="VisionKey"/> for the vision model;</item>
    /// <item><see cref="IEmbeddingGenerator{TInput, TEmbedding}"/>, <see cref="ISpeechToTextClient"/> and <see cref="ITextToSpeechClient"/>;</item>
    /// <item><see cref="IReranker"/> and <see cref="IVoiceActivityDetector"/>;</item>
    /// <item><see cref="LocalAIModels"/>, to watch or trigger loading.</item>
    /// </list>
    /// Each model loads on first use. Any of them can be a hosted provider instead; see <see cref="LocalAIOptions"/>.
    /// </summary>
    public static IServiceCollection AddLocalAI(this IServiceCollection services, IConfiguration configuration) =>
        services.AddLocalAI(options => configuration.Bind(options));

    /// <inheritdoc cref="AddLocalAI(IServiceCollection, IConfiguration)"/>
    public static IServiceCollection AddLocalAI(this IServiceCollection services, Action<LocalAIOptions> configure)
    {
        services.Configure(configure);
        services.TryAddSingleton<LocalAIModels>();

        services.AddChatClient(sp => new LazyChatClient(sp.GetRequiredService<LocalAIModels>().Chat))
            .UseFunctionInvocation();
        services.AddKeyedChatClient(VisionKey, sp => new LazyChatClient(sp.GetRequiredService<LocalAIModels>().Vision));
        services.AddEmbeddingGenerator(sp => new LazyEmbeddingGenerator(sp.GetRequiredService<LocalAIModels>().Embeddings));
        services.TryAddSingleton<ISpeechToTextClient>(sp => new LazySpeechToTextClient(sp.GetRequiredService<LocalAIModels>().SpeechToText));
        services.TryAddSingleton<ITextToSpeechClient>(sp => new LazyTextToSpeechClient(sp.GetRequiredService<LocalAIModels>().TextToSpeech));
        services.TryAddSingleton<IReranker>(sp => new LazyReranker(sp.GetRequiredService<LocalAIModels>().Reranker));
        services.TryAddSingleton<IVoiceActivityDetector>(sp => new LazyVoiceActivityDetector(sp.GetRequiredService<LocalAIModels>().VoiceActivity));
        return services;
    }
}
