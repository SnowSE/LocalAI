namespace LocalAI.Hosting;

/// <summary>Where a capability's model runs.</summary>
public enum ModelProvider
{
    /// <summary>In this process, from a model file or <c>hf://</c> source.</summary>
    Local,

    /// <summary>
    /// OpenAI, or any service with an OpenAI-compatible API (Azure OpenAI's v1 endpoint, LM Studio,
    /// vLLM, llama-server, Ollama's <c>/v1</c>) when <see cref="ModelOptions.Endpoint"/> is set.
    /// </summary>
    OpenAI,

    /// <summary>An Ollama server.</summary>
    Ollama,
}

/// <summary>One capability's model.</summary>
public class ModelOptions
{
    public ModelProvider Provider { get; set; } = ModelProvider.Local;

    /// <summary>
    /// For <see cref="ModelProvider.Local"/>, a path, <c>hf://owner/repo/file</c> or <c>https://</c>
    /// URL. For hosted providers, the model id (such as <c>gpt-5-mini</c>). Empty turns the capability off.
    /// </summary>
    public string Model { get; set; } = "";

    /// <summary>Base URL of a hosted provider. Defaults to OpenAI's API or <c>http://localhost:11434</c> for Ollama.</summary>
    public string? Endpoint { get; set; }

    /// <summary>API key for a hosted provider. Defaults to the <c>OPENAI_API_KEY</c> environment variable for OpenAI.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Whether to configure this slot at all; empty <see cref="Model"/> means not.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Model);

    /// <summary>How the slot is shown: the source for local models, "provider: model" for hosted ones.</summary>
    public string Describe() => Provider == ModelProvider.Local ? Model : $"{Provider}: {Model}";
}

public sealed class ChatModelOptions : ModelOptions
{
    /// <summary>Tokens of conversation a local model can hold. Defaults to 4096.</summary>
    public int ContextSize { get; set; } = 4096;

    /// <summary>A local vision model's projection (<c>mmproj</c>) file.</summary>
    public string? Projection { get; set; }

    /// <summary>Requests a local model serves at once, each with its own context. Defaults to 2.</summary>
    public int MaxConcurrentRequests { get; set; } = 2;
}

public sealed class TextToSpeechModelOptions : ModelOptions
{
    /// <summary>The voice used when a request doesn't name one. Defaults to <c>af_heart</c> for Kokoro.</summary>
    public string? Voice { get; set; }
}

/// <summary>
/// Every model an app uses, one per capability. Bind it from configuration (the <c>LocalAI</c>
/// section by convention), where each capability is a local model or a hosted one:
/// <code>
/// "LocalAI": {
///   "Chat": { "Model": "hf://NobodyWho/Qwen_Qwen3-0.6B-GGUF/Qwen_Qwen3-0.6B-Q4_K_M.gguf" },
///   "Embeddings": { "Provider": "OpenAI", "Model": "text-embedding-3-small" }
/// }
/// </code>
/// </summary>
public sealed class LocalAIOptions
{
    /// <summary>Use a GPU for local models when there is one. Defaults to <c>true</c>.</summary>
    public bool UseGpu { get; set; } = true;

    /// <summary>Where downloaded models are cached. Defaults to <see cref="ModelSource.CacheDirectory"/>.</summary>
    public string? CacheDirectory { get; set; }

    public ChatModelOptions Chat { get; set; } = new();
    public ChatModelOptions Vision { get; set; } = new();
    public ModelOptions Embeddings { get; set; } = new();
    public ModelOptions Reranker { get; set; } = new();
    public ModelOptions SpeechToText { get; set; } = new();
    public TextToSpeechModelOptions TextToSpeech { get; set; } = new();
    public ModelOptions VoiceActivity { get; set; } = new();
}
