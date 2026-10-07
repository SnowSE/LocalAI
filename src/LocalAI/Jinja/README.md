# Vendored Jinja engine

The chat-template engine: [AcDc.Jinja](https://www.nuget.org/packages/AcDc.Jinja) 1.0.0, a C# port of
[google/minja](https://github.com/google/minja) (the engine llama.cpp uses), MIT-licensed (see
`LICENSE`). The package publishes no source repository, so this was recovered from its assembly and
is kept internal to LocalAI.

Changes from the package:

- Out-of-range slice bounds clamp like Python instead of throwing (`""[:15]` is `""`). Qwen's
  template slices every message, so any empty assistant turn broke it.
- Adjacent string literals join (`"a" "b"` is `"ab"`), as in Python. Gemma 4's template uses it.
- `tojson` escapes like Hugging Face's (`\"`, non-ASCII kept) rather than `"`.
- All types are `internal`; source-generated regexes are declared as `[GeneratedRegex]` partials
  again; nullability warnings are off, since the recovered code has no annotations to check.
