using LocalAI.Llama;

namespace LocalAI.Tests;

public class OutputParserTests
{
    private static List<OutputPiece> Feed(OutputParser parser, params string[] tokens)
    {
        var all = new List<OutputPiece>();
        foreach (var token in tokens)
            all.AddRange(parser.Push(token));
        all.AddRange(parser.Finish());
        return Merge(all);
    }

    /// <summary>Join consecutive pieces of the same kind, as a reader would see them.</summary>
    private static List<OutputPiece> Merge(List<OutputPiece> pieces)
    {
        var merged = new List<OutputPiece>();
        foreach (var piece in pieces)
        {
            if (merged.Count > 0 && merged[^1].Kind == piece.Kind && piece.Kind != OutputKind.ToolCall)
                merged[^1] = merged[^1] with { Text = merged[^1].Text + piece.Text };
            else
                merged.Add(piece);
        }
        return merged;
    }

    [Fact]
    public void SplitsReasoningFromTheAnswer()
    {
        var pieces = Feed(new OutputParser(false, false), "<think>", "\nHmm", ", cats.", "\n</think>", "\n\n", "They purr", ".");

        Assert.Equal([new(OutputKind.Reasoning, "Hmm, cats.\n"), new(OutputKind.Text, "They purr.")], pieces);
    }

    [Fact]
    public void TagsSplitAcrossTokensAreRecognized()
    {
        var pieces = Feed(new OutputParser(false, false), "<th", "ink>a</th", "ink>b");

        Assert.Equal([new(OutputKind.Reasoning, "a"), new(OutputKind.Text, "b")], pieces);
    }

    [Fact]
    public void TextThatOnlyLooksLikeATagIsKept()
    {
        var pieces = Feed(new OutputParser(false, false), "1 <", "2 and <th", "e end");

        Assert.Equal([new(OutputKind.Text, "1 <2 and <the end")], pieces);
    }

    [Fact]
    public void StartsInsideReasoningWhenThePromptOpenedIt()
    {
        var pieces = Feed(new OutputParser(true, false), "plan", "</think>", "done");

        Assert.Equal([new(OutputKind.Reasoning, "plan"), new(OutputKind.Text, "done")], pieces);
    }

    [Fact]
    public void ExtractsToolCalls()
    {
        var parser = new OutputParser(false, true);
        var pieces = Feed(parser, "Let me check.", "<tool_call>", "\n{\"name\": \"f\",", " \"arguments\": {}}\n", "</tool_call>", "<tool_call>", "{\"name\":\"g\",\"arguments\":{}}", "</tool_call>");

        Assert.Equal(
            [
                new(OutputKind.Text, "Let me check."),
                new(OutputKind.ToolCall, "{\"name\": \"f\", \"arguments\": {}}"),
                new(OutputKind.ToolCall, "{\"name\":\"g\",\"arguments\":{}}"),
            ],
            pieces);
    }

    [Fact]
    public void ToolCallTagsAreTextWithoutTools()
    {
        var pieces = Feed(new OutputParser(false, false), "<tool_call>x</tool_call>");

        Assert.Equal([new(OutputKind.Text, "<tool_call>x</tool_call>")], pieces);
    }

    [Fact]
    public void TracksWhetherItIsInsideACall()
    {
        var parser = new OutputParser(false, true);
        parser.Push("<tool_call>");
        Assert.Equal(OutputParser.State.ToolCall, parser.Current);
        parser.Push("{}</tool_call>");
        Assert.Equal(OutputParser.State.Text, parser.Current);
    }
}

public class GemmaOutputParserTests
{
    [Fact]
    public void SplitsGemmaThoughtChannel()
    {
        var parser = new OutputParser(false, false, ChatTemplate.GemmaThoughtOpen, ChatTemplate.GemmaThoughtClose);
        var pieces = new List<OutputPiece>();
        foreach (var token in new[] { "<|channel>", "thought\n", "Look at it.", "<channel|>", "A cat." })
            pieces.AddRange(parser.Push(token));
        pieces.AddRange(parser.Finish());

        Assert.Equal("Look at it.", string.Concat(pieces.Where(p => p.Kind == OutputKind.Reasoning).Select(p => p.Text)));
        Assert.Equal("A cat.", string.Concat(pieces.Where(p => p.Kind == OutputKind.Text).Select(p => p.Text)));
    }
}
