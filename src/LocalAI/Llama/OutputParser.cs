using System.Text;

namespace LocalAI.Llama;

/// <summary>What a piece of generated text turned out to be.</summary>
internal enum OutputKind
{
    Reasoning,
    Text,
    /// <summary>The complete body of one tool call, between the call tags.</summary>
    ToolCall,
}

internal readonly record struct OutputPiece(OutputKind Kind, string Text);

/// <summary>
/// Splits a model's streamed output into reasoning, answer text and tool calls. Text that might be
/// the start of a tag is held back until the next token shows whether it is one.
/// </summary>
internal sealed class OutputParser(bool startInReasoning, bool parseToolCalls, string reasoningOpen = ChatTemplate.ThinkOpen, string reasoningClose = ChatTemplate.ThinkClose)
{
    private readonly StringBuilder _pending = new();
    private readonly StringBuilder _toolCall = new();

    /// <summary>After a closing tag, newlines that follow it in later tokens are dropped too.</summary>
    private bool _skipNewlines;

    public enum State
    {
        Reasoning,
        Text,
        ToolCall,
    }

    public State Current { get; private set; } = startInReasoning ? State.Reasoning : State.Text;

    /// <summary>Start inside a tool call, for when the prompt already opened one.</summary>
    public void BeginToolCall() => Current = State.ToolCall;

    /// <summary>Feed newly generated text; returns whatever can be emitted so far.</summary>
    public List<OutputPiece> Push(string text)
    {
        var output = new List<OutputPiece>();
        _pending.Append(text);
        if (_skipNewlines)
            TrimLeadingNewlines();
        while (_pending.Length > 0)
        {
            var buffer = _pending.ToString();
            var tags = Current switch
            {
                State.Reasoning => [reasoningClose],
                State.ToolCall => [ChatTemplate.ToolCallClose],
                _ => parseToolCalls ? new[] { reasoningOpen, ChatTemplate.ToolCallOpen } : [reasoningOpen],
            };

            var (index, tag) = FindFirst(buffer, tags);
            if (tag is null)
            {
                // Keep back a tail that could still grow into a tag.
                var keep = PartialTagLength(buffer, tags);
                Emit(output, buffer[..^keep]);
                _pending.Clear().Append(buffer[^keep..]);
                break;
            }

            Emit(output, buffer[..index]);
            _pending.Clear().Append(buffer[(index + tag.Length)..]);
            if (tag == reasoningOpen)
            {
                Current = State.Reasoning;
                TrimLeadingNewlines();
                continue;
            }
            if (tag == reasoningClose)
            {
                Current = State.Text;
                TrimLeadingNewlines();
                continue;
            }
            switch (tag)
            {
                case ChatTemplate.ToolCallOpen:
                    Current = State.ToolCall;
                    _toolCall.Clear();
                    break;
                case ChatTemplate.ToolCallClose:
                    output.Add(new OutputPiece(OutputKind.ToolCall, _toolCall.ToString().Trim()));
                    _toolCall.Clear();
                    Current = State.Text;
                    TrimLeadingNewlines();
                    break;
            }
        }
        return output;
    }

    /// <summary>Flush held-back text at the end of generation. An unfinished tool call is returned as one too, so the caller can report it.</summary>
    public List<OutputPiece> Finish()
    {
        var output = new List<OutputPiece>();
        Emit(output, _pending.ToString());
        _pending.Clear();
        if (Current == State.ToolCall && _toolCall.Length > 0)
            output.Add(new OutputPiece(OutputKind.ToolCall, _toolCall.ToString().Trim()));
        return output;
    }

    private void Emit(List<OutputPiece> output, string text)
    {
        if (text.Length == 0)
            return;
        switch (Current)
        {
            case State.ToolCall:
                _toolCall.Append(text);
                break;
            case State.Reasoning:
                output.Add(new OutputPiece(OutputKind.Reasoning, text));
                break;
            default:
                output.Add(new OutputPiece(OutputKind.Text, text));
                break;
        }
    }

    private void TrimLeadingNewlines()
    {
        var n = 0;
        while (n < _pending.Length && _pending[n] is '\n' or '\r')
            n++;
        _pending.Remove(0, n);
        _skipNewlines = _pending.Length == 0;
    }

    private static (int Index, string? Tag) FindFirst(string buffer, string[] tags)
    {
        var best = (Index: -1, Tag: (string?)null);
        foreach (var tag in tags)
        {
            var at = buffer.IndexOf(tag, StringComparison.Ordinal);
            if (at >= 0 && (best.Tag is null || at < best.Index))
                best = (at, tag);
        }
        return best;
    }

    private static int PartialTagLength(string buffer, string[] tags)
    {
        var longest = 0;
        foreach (var tag in tags)
        {
            for (var length = Math.Min(tag.Length - 1, buffer.Length); length > longest; length--)
            {
                if (buffer.AsSpan(buffer.Length - length).SequenceEqual(tag.AsSpan(0, length)))
                {
                    longest = length;
                    break;
                }
            }
        }
        return longest;
    }
}
