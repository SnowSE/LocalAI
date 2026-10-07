using LocalAI.Audio;

namespace LocalAI.Tests;

public class AudioDataTests
{
    [Fact]
    public void WavRoundTrips()
    {
        var samples = Enumerable.Range(0, 1000).Select(i => MathF.Sin(i / 10f) * 0.5f).ToArray();
        var decoded = AudioData.Decode(new AudioData(samples, 24000).ToWav());

        Assert.Equal(24000, decoded.SampleRate);
        Assert.Equal(samples.Length, decoded.Samples.Length);
        Assert.All(samples.Zip(decoded.Samples), pair => Assert.Equal(pair.First, pair.Second, 0.001f));
    }

    [Fact]
    public void ResamplingKeepsDuration()
    {
        var audio = new AudioData(new float[24000], 24000).Resample(16000);

        Assert.Equal(16000, audio.Samples.Length);
        Assert.Equal(TimeSpan.FromSeconds(1), audio.Duration);
    }

    [Fact]
    public void RejectsOtherFormats() =>
        Assert.Throws<FormatException>(() => AudioData.Decode("not audio at all"u8));
}

public class ModelSourceTests
{
    [Theory]
    [InlineData("hf://owner/repo/model.gguf", "owner/repo", "main", "model.gguf")]
    [InlineData("hf://owner/repo@v2/sub/dir/model.onnx", "owner/repo", "v2", "sub/dir/model.onnx")]
    public void ParsesHuggingFaceSources(string source, string repo, string revision, string file) =>
        Assert.Equal((repo, revision, file), ModelSource.Parse(source));

    [Fact]
    public void RejectsSourcesWithoutAFile() =>
        Assert.Throws<ArgumentException>(() => ModelSource.Parse("hf://owner/repo"));

    [Fact]
    public async Task MissingLocalFilesThrow() =>
        await Assert.ThrowsAsync<FileNotFoundException>(() => ModelSource.ResolveAsync(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".gguf"), cancellationToken: TestContext.Current.CancellationToken));
}
