# LocalAI

Run language, embedding, reranking, speech-to-text and text-to-speech models inside your .NET app,
and use them through [Microsoft.Extensions.AI](https://learn.microsoft.com/dotnet/ai/microsoft-extensions-ai).
Because everything goes through the standard interfaces, any capability can be pointed at a hosted
model (OpenAI, Azure OpenAI, Ollama, any OpenAI-compatible server) in configuration, with no code changes.

| Capability | Interface | Local engine |
|------------|-----------|--------------|
| Chat, streaming, reasoning, images | `IChatClient` | llama.cpp (LLamaSharp), any GGUF |
| Tool calling | `IChatClient` + `FunctionInvokingChatClient` | grammar-constrained calls |
| Structured output | `ChatResponseFormat` / `GetResponseAsync<T>` | JSON schema → GBNF grammar |
| Embeddings | `IEmbeddingGenerator<string, Embedding<float>>` | llama.cpp |
| Speech to text | `ISpeechToTextClient` | whisper.cpp (Whisper.net) |
| Text to speech | `ITextToSpeechClient` | Kokoro on ONNX Runtime (KokoroSharp) |
| Reranking | `IReranker` (LocalAI's; MEAI has none) | llama.cpp cross-encoders |
| Voice activity detection | `IVoiceActivityDetector` (LocalAI's) | Silero on ONNX Runtime |

Everything you write is C#. The inference engines come as NuGet packages with prebuilt native
binaries (CPU and Vulkan; add `LLamaSharp.Backend.Cuda12` for NVIDIA). There's no Rust, Python or
model server to install.

## Use it in an app

Reference `src/LocalAI` (or the `LocalAI` package once it's published), then register it:

```csharp
builder.Services.AddLocalAI(builder.Configuration.GetSection("LocalAI"));
```

```json
"LocalAI": {
  "Chat": { "Model": "hf://NobodyWho/Qwen_Qwen3-0.6B-GGUF/Qwen_Qwen3-0.6B-Q4_K_M.gguf" },
  "Embeddings": { "Model": "hf://CompendiumLabs/bge-small-en-v1.5-gguf/bge-small-en-v1.5-q8_0.gguf" },
  "SpeechToText": { "Model": "hf://ggerganov/whisper.cpp/ggml-base.bin" },
  "TextToSpeech": { "Model": "https://github.com/Lyrcaxis/KokoroSharpBinaries/releases/download/v2.0.0/kokoro.onnx" }
}
```

Then inject the interfaces anywhere:

```csharp
public sealed class Assistant(IChatClient chat)
{
    public async Task<string> AskAsync(string question) =>
        (await chat.GetResponseAsync(question)).Text;
}
```

Each model loads the first time something uses it. `hf://owner/repo/path` and `https://` sources
download once into `%LOCALAPPDATA%/LocalAI/models` (or `LOCALAI_CACHE`); local paths are used as
they are. Inject `LocalAIModels` to show load progress or to load a model ahead of time.

To use a hosted model for a capability, give it a provider:

```json
"Chat": { "Provider": "OpenAI", "Model": "gpt-5-mini" },
"Embeddings": { "Provider": "Ollama", "Model": "nomic-embed-text" }
```

### Without dependency injection

```csharp
using var chat = await LocalChatClient.LoadAsync("hf://NobodyWho/Qwen_Qwen3-0.6B-GGUF/Qwen_Qwen3-0.6B-Q4_K_M.gguf");

await foreach (var update in chat.GetStreamingResponseAsync("Why do cats purr?"))
    Console.Write(update.Text);

// Tools: wrap it in Microsoft.Extensions.AI's function invocation
var client = new ChatClientBuilder(chat).UseFunctionInvocation().Build();
var weather = AIFunctionFactory.Create((string city) => $"Sunny in {city}", "get_weather", "Get the weather");
var answer = await client.GetResponseAsync("Weather in Oslo?", new ChatOptions { Tools = [weather] });

// Structured output
var card = (await chat.GetResponseAsync<ContactCard>(email)).Result;
```

The other capabilities load the same way: `LocalEmbeddingGenerator.LoadAsync`,
`LocalReranker.LoadAsync`, `LocalSpeechToTextClient.LoadAsync`, `LocalTextToSpeechClient.LoadAsync`
and `SileroVoiceActivityDetector.LoadAsync`.

### Local-model extras

Settings `ChatOptions` has no property for go in `AdditionalProperties` through extension methods,
so hosted providers simply ignore them:

```csharp
var options = new ChatOptions { Temperature = 0 }
    .WithThinking(false)                      // ChatOptions.Reasoning; works for hosted reasoning models too
    .WithRegex("(positive|negative|mixed)")   // or .WithGrammar(gbnf)
    .WithMinP(0.05f);
```

Local responses report the model's context size in their usage (`usage.ContextSize()`), for a
context meter.

## How the local chat client works

- **Prompts** are rendered with the model's own Jinja chat template from its GGUF, so tools,
  reasoning and images appear the way the model was trained to see them.
- **Tool calls** are parsed from the Hermes `<tool_call>` format. Templates that use it (Qwen, Hermes)
  render the tools themselves; for models with their own format (Gemma, Llama), the tools are
  described in the system prompt instead. Once the model opens a call, sampling is constrained by a
  grammar built from the tools' JSON schemas, so even small models can only name a real tool with
  valid arguments. `FunctionInvokingChatClient` then runs them.
- **Reasoning** in `<think>` tags or Gemma 4's thought channel comes back as `TextReasoningContent`.
- **Caching:** `IChatClient` is stateless, so every request carries the whole conversation. The client
  keeps a few contexts alive (`MaxConcurrentRequests`, default 2) and serves each request from the one
  sharing the longest token prefix, so a new turn only processes the new message.
- **Long conversations** drop their oldest turns (keeping the system prompt) when they no longer fit.

## Repository

```
src/LocalAI/            The library
  Llama/                Chat, embeddings, reranking on llama.cpp
  Speech/               Whisper, Kokoro, Silero
  Grammar/              JSON schema, regex and tool-call grammars (GBNF)
  Hosting/              Configuration, DI, model slots, hosted providers
  Jinja/                Chat-template engine (vendored AcDc.Jinja, MIT; see its README)
samples/LocalAI.Demo/   Blazor demo, one page per capability (see its README)
tests/LocalAI.Tests/    xUnit v3
```

## Tests

```bash
dotnet test --project tests/LocalAI.Tests                       # unit tests
LOCALAI_INTEGRATION=1 dotnet test --project tests/LocalAI.Tests # plus real models (~2.5 GB download)
```

Override the integration models with `LOCALAI_CHAT_MODEL`, `LOCALAI_EMBEDDING_MODEL`,
`LOCALAI_RERANKER_MODEL`, `LOCALAI_STT_MODEL`, `LOCALAI_TTS_MODEL`, `LOCALAI_VISION_MODEL` and
`LOCALAI_VISION_PROJECTION`.

## Requirements

.NET 10. The engines ship native binaries for Windows, Linux and macOS; so far this has been run on
Windows x64 (AMD Radeon 780M through Vulkan). A GPU is optional: llama.cpp and Whisper use Vulkan
(or CUDA, with its backend package) when one is present and fall back to the CPU.
