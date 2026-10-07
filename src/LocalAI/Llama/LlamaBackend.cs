using LLama.Native;
using Microsoft.Extensions.Logging;

namespace LocalAI.Llama;

/// <summary>Picks llama.cpp's native backend once, before the first model loads.</summary>
internal static class LlamaBackend
{
    private static readonly Lock Gate = new();
    private static bool _configured;

    [ThreadStatic]
    private static LogLevel _previous;

    /// <summary>
    /// Prefer a GPU backend (CUDA, then Vulkan) when <paramref name="useGpu"/> is set, falling back
    /// to the CPU. The choice is process-wide, so the first model to load decides it.
    /// </summary>
    public static void EnsureConfigured(bool useGpu)
    {
        lock (Gate)
        {
            if (_configured)
                return;
            _configured = true;
            try
            {
                NativeLibraryConfig.All
                    .WithCuda(useGpu)
                    .WithVulkan(useGpu)
                    .WithAutoFallback()
                    .WithLogCallback(Log);
            }
            catch (InvalidOperationException)
            {
                // Already loaded by someone else using LLamaSharp directly; keep their choice.
            }
        }
    }

    private static void Log(LLamaLogLevel level, string message)
    {
        if (LocalAIRuntime.Logger is not { } logger)
            return;
        // "Continue" carries on the previous message at its level.
        var mapped = level switch
        {
            LLamaLogLevel.Debug => LogLevel.Debug,
            LLamaLogLevel.Info => LogLevel.Debug, // llama.cpp is chatty at info; keep it out of normal logs.
            LLamaLogLevel.Warning => LogLevel.Warning,
            LLamaLogLevel.Error => LogLevel.Error,
            LLamaLogLevel.Continue => _previous,
            _ => LogLevel.None,
        };
        _previous = mapped;
        var text = message.TrimEnd();
        if (text.Length > 0 && logger.IsEnabled(mapped))
            logger.Log(mapped, "{Message}", text);
    }
}
