# LocalAI demo

A Blazor Server app that shows what LocalAI can do, one page per capability, each next to the C# it
runs:

| Page | Shows |
|------|-------|
| Overview | A streaming answer, every model's status (local or hosted), and the download cache |
| Chat | Multi-turn streaming, system prompt, sampler settings, reasoning ("thinking"), stopping a reply, context usage |
| Tool calling | Three C# lambdas the model calls, with each call's arguments and result as it happens |
| Structured output | `GetResponseAsync<T>` into a record, and regex-constrained classification |
| Search and rerank | Embedding search, cross-encoder reranking, and an answer grounded in the top results |
| Images | Questions about an uploaded image (needs a vision model; see below) |
| Speech | Text to speech, transcription, and voice activity detection, in one loop with no microphone |

The pages only use Microsoft.Extensions.AI interfaces (`IChatClient`, `IEmbeddingGenerator`,
`ISpeechToTextClient`, `ITextToSpeechClient`) plus LocalAI's `IReranker` and `IVoiceActivityDetector`,
all injected. `Program.cs` is one line of setup: `builder.Services.AddLocalAI(...)`.

## Run it

```bash
cd samples/LocalAI.Demo
dotnet run
```

Then open the URL it prints. Each model downloads the first time a page needs it (the Overview page
shows progress) and is cached in `%LOCALAPPDATA%/LocalAI/models`. About 2 GB in total with the defaults, plus 4 GB if you load the vision model.

## Models

Set them under `LocalAI` in `appsettings.json`, or override any one with an environment variable such
as `LocalAI__Chat__Model=/path/to/model.gguf`.

| Setting | Default | Used by |
|---------|---------|---------|
| `Chat` | Qwen3 0.6B | Overview, Chat, Tool calling, Structured output, answers on Search, translation on Speech |
| `Vision` (`Model`, `Projection`) | Gemma 4 E2B (about 4 GB; loads only when you ask) | Images |
| `Embeddings` | bge-small-en-v1.5 | Search |
| `Reranker` | bge-reranker-v2-m3 | Rerank on Search |
| `SpeechToText` | Whisper base | Speech |
| `TextToSpeech` (`Model`, `Voice`) | Kokoro v1.0, `bf_emma` | Speech |
| `VoiceActivity` | Silero VAD | Speech |
| `UseGpu` | `true` | All llama.cpp and Whisper models; falls back to the CPU without a GPU |

The Images page doesn't load its model until you click, since Gemma 4 E2B is about 4 GB. For a quick
try, SmolVLM is about 400 MB (much weaker):

```json
"Vision": {
  "Model": "hf://ggml-org/SmolVLM-256M-Instruct-GGUF/SmolVLM-256M-Instruct-Q8_0.gguf",
  "Projection": "hf://ggml-org/SmolVLM-256M-Instruct-GGUF/mmproj-SmolVLM-256M-Instruct-Q8_0.gguf"
}
```

### Hosted models instead

Any slot can call a hosted model; the pages don't change. For example, chat through Ollama and
embeddings through OpenAI:

```json
"Chat": { "Provider": "Ollama", "Model": "gemma4:26b" },
"Embeddings": { "Provider": "OpenAI", "Model": "text-embedding-3-small" }
```

`OpenAI` reads `ApiKey` or the `OPENAI_API_KEY` environment variable, and with `Endpoint` set works
with any OpenAI-compatible server (Azure OpenAI's v1 endpoint, LM Studio, vLLM, llama-server).
Reranking and voice activity detection are local only, since Microsoft.Extensions.AI has no
abstraction for them that providers implement.

## How it's put together

- `LocalAIModels` (from the library) loads each model once for the whole app, on first use, and
  reports download progress. The injected clients wait for it the first time they're called.
- `Components/Shared/ModelGate.razor` shows a model's loading state and renders the demo once it's ready.
- `Components/Shared/DemoPage.cs` cancels generation when the visitor leaves a page.
- `Services/Reply.cs` collects a streamed reply, keeping reasoning (`TextReasoningContent`) apart from
  the answer.
- Prerendering is off: prerendered buttons do nothing until the live connection starts.
