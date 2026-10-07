using Microsoft.Extensions.Logging;

namespace LocalAI;

/// <summary>Process-wide settings for the native inference engines.</summary>
public static class LocalAIRuntime
{
    /// <summary>
    /// Receives llama.cpp's own log messages. Without one they are dropped; failures still surface
    /// as exceptions. <c>AddLocalAI</c> sets this from the app's logging, under the category
    /// <c>LocalAI.Native</c>. Set it before the first model loads.
    /// </summary>
    public static ILogger? Logger { get; set; }
}
