using System.Net.Http.Headers;

namespace LocalAI;

/// <summary>How far a model download has got.</summary>
/// <param name="Downloaded">Bytes received so far.</param>
/// <param name="Total">Size of the file in bytes, or 0 when the server doesn't say.</param>
public readonly record struct DownloadProgress(long Downloaded, long Total);

/// <summary>A model file in the download cache.</summary>
public sealed record CachedModel(string Path, long Size);

/// <summary>
/// Turns a model source into a file on disk. A source is a local path, a Hugging Face file given as
/// <c>hf://owner/repo/path/to/file</c> (optionally <c>hf://owner/repo@revision/file</c>), or an
/// <c>https://</c> URL. Downloads happen once into the cache and are reused after that.
/// </summary>
public static class ModelSource
{
    private static readonly HttpClient Http = CreateClient();

    /// <summary>
    /// Where downloads are cached. Defaults to <c>LOCALAI_CACHE</c> if set, otherwise
    /// <c>%LOCALAPPDATA%/LocalAI/models</c> (<c>~/.local/share/LocalAI/models</c> on Linux).
    /// </summary>
    public static string CacheDirectory { get; set; } =
        Environment.GetEnvironmentVariable("LOCALAI_CACHE") is { Length: > 0 } dir
            ? dir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LocalAI", "models");

    /// <summary>Whether <paramref name="source"/> is a Hugging Face reference rather than a local path.</summary>
    public static bool IsHuggingFace(string source) => source.StartsWith("hf://", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether <paramref name="source"/> is downloaded (<c>hf://</c> or <c>https://</c>) rather than a local path.</summary>
    public static bool IsRemote(string source) =>
        IsHuggingFace(source) || source.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The local path for <paramref name="source"/>, downloading it first if it's an <c>hf://</c>
    /// file that isn't cached yet.
    /// </summary>
    /// <exception cref="FileNotFoundException">A local path that doesn't exist.</exception>
    /// <exception cref="HttpRequestException">The download failed.</exception>
    public static async Task<string> ResolveAsync(string source, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (!IsRemote(source))
        {
            var full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(source));
            if (!File.Exists(full) && !Directory.Exists(full))
                throw new FileNotFoundException($"Model file not found: {full}", full);
            return full;
        }

        string url, target;
        var huggingFace = IsHuggingFace(source);
        if (huggingFace)
        {
            var (repo, revision, file) = Parse(source);
            url = $"https://huggingface.co/{repo}/resolve/{revision}/{file}";
            target = Path.Combine(CacheDirectory, repo.Replace('/', Path.DirectorySeparatorChar), revision, file.Replace('/', Path.DirectorySeparatorChar));
        }
        else
        {
            var uri = new Uri(source);
            url = uri.AbsoluteUri;
            target = Path.Combine(CacheDirectory, "web", uri.Host, uri.AbsolutePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        }
        if (File.Exists(target))
            return target;

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var partial = target + ".part";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (huggingFace && Environment.GetEnvironmentVariable("HF_TOKEN") is { Length: > 0 } token)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Downloading {source} failed: {(int)response.StatusCode} {response.ReasonPhrase}.", null, response.StatusCode);

        var total = response.Content.Headers.ContentLength ?? 0;
        await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true))
        {
            var buffer = new byte[1 << 20];
            long downloaded = 0;
            int read;
            progress?.Report(new DownloadProgress(0, total));
            while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                downloaded += read;
                progress?.Report(new DownloadProgress(downloaded, total));
            }
        }
        File.Move(partial, target, overwrite: true);
        return target;
    }

    /// <summary>Every finished download in <see cref="CacheDirectory"/>.</summary>
    public static IReadOnlyList<CachedModel> GetCachedModels()
    {
        if (!Directory.Exists(CacheDirectory))
            return [];
        return Directory.EnumerateFiles(CacheDirectory, "*", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith(".part", StringComparison.Ordinal))
            .Select(path => new CachedModel(Path.GetRelativePath(CacheDirectory, path).Replace('\\', '/'), new FileInfo(path).Length))
            .OrderBy(m => m.Path, StringComparer.Ordinal)
            .ToArray();
    }

    internal static (string Repo, string Revision, string File) Parse(string source)
    {
        var parts = source["hf://".Length..].Split('/', 3);
        if (parts.Length < 3 || parts.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException($"Expected hf://owner/repo/path/to/file, got {source}.", nameof(source));
        var name = parts[1];
        var revision = "main";
        if (name.IndexOf('@') is var at and > 0)
        {
            revision = name[(at + 1)..];
            name = name[..at];
        }
        return ($"{parts[0]}/{name}", revision, parts[2]);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LocalAI/0.1");
        return client;
    }
}
