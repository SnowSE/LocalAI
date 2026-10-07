using Microsoft.Extensions.AI;

namespace LocalAI.Demo.Services;

/// <summary>
/// A streamed reply as the page shows it: reasoning (from <see cref="TextReasoningContent"/>)
/// apart from the answer, plus usage once the stream ends. Works for local and hosted models alike.
/// </summary>
public sealed class Reply
{
    public string Thought { get; private set; } = "";
    public string Text { get; private set; } = "";
    public UsageDetails? Usage { get; private set; }
    public bool Done { get; set; }
    public bool Stopped { get; set; }

    /// <summary>Updates received that carried text, a stand-in for tokens when a provider reports no usage.</summary>
    public int Pieces { get; private set; }

    /// <summary>Still reasoning: it has thought but not started answering.</summary>
    public bool Thinking => !Done && Text.Length == 0 && Thought.Length > 0;

    public long Tokens => Usage?.OutputTokenCount ?? Pieces;

    public void Add(ChatResponseUpdate update)
    {
        foreach (var content in update.Contents)
        {
            switch (content)
            {
                case TextReasoningContent reasoning:
                    Thought += reasoning.Text;
                    Pieces++;
                    break;
                case TextContent text:
                    Text += text.Text;
                    Pieces++;
                    break;
                case UsageContent usage:
                    Usage = usage.Details;
                    break;
            }
        }
    }

    public void Clear()
    {
        Thought = Text = "";
        Usage = null;
        Pieces = 0;
        Done = Stopped = false;
    }
}
