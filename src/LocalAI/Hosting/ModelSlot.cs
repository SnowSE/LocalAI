namespace LocalAI.Hosting;

public enum SlotState
{
    /// <summary>Configured, but nothing has needed it yet.</summary>
    NotLoaded,
    Loading,
    Ready,
    Failed,
    /// <summary>No model is set for this capability.</summary>
    NotConfigured,
}

/// <summary>Non-generic view of a <see cref="ModelSlot{T}"/>, for listing every model in one place.</summary>
public interface IModelSlot
{
    /// <summary>Short name, such as "Chat".</summary>
    string Name { get; }

    /// <summary>What the app uses it for.</summary>
    string Purpose { get; }

    /// <summary>Where it comes from: a model path or <c>hf://</c> source, or a hosted provider and model id.</summary>
    string Source { get; }

    /// <summary>Whether this slot runs in-process rather than calling a hosted service.</summary>
    bool IsLocal { get; }

    SlotState State { get; }
    DownloadProgress? Progress { get; }
    string? Error { get; }
    TimeSpan? LoadTime { get; }

    /// <summary>Raised whenever <see cref="State"/> or <see cref="Progress"/> changes, on whatever thread made the change.</summary>
    event Action? Changed;

    Task LoadAsync();
}

/// <summary>
/// One model the whole app shares. It loads on first use (or when <see cref="GetAsync"/> is
/// called), reports download progress while it does, and can be retried after a failure.
/// </summary>
public sealed class ModelSlot<T> : IModelSlot, IDisposable where T : class, IDisposable
{
    private readonly Func<IProgress<DownloadProgress>, CancellationToken, Task<T>>? _load;
    private readonly Lock _lock = new();
    private readonly CancellationTokenSource _disposed = new();
    private Task<T>? _task;
    private long _lastNotify;

    /// <param name="load">Loads the model; <c>null</c> when nothing is configured.</param>
    public ModelSlot(string name, string purpose, string source, bool isLocal, Func<IProgress<DownloadProgress>, CancellationToken, Task<T>>? load)
    {
        Name = name;
        Purpose = purpose;
        Source = source;
        IsLocal = isLocal;
        _load = load;
        State = load is null ? SlotState.NotConfigured : SlotState.NotLoaded;
    }

    public string Name { get; }
    public string Purpose { get; }
    public string Source { get; }
    public bool IsLocal { get; }
    public SlotState State { get; private set; }
    public DownloadProgress? Progress { get; private set; }
    public string? Error { get; private set; }
    public TimeSpan? LoadTime { get; private set; }

    /// <summary>The loaded model, or <c>null</c> until it is ready.</summary>
    public T? Value { get; private set; }

    public event Action? Changed;

    /// <summary>The model, loading it first if needed. Concurrent callers share one load.</summary>
    /// <exception cref="InvalidOperationException">No model is configured for this slot.</exception>
    public Task<T> GetAsync()
    {
        if (_load is null)
            throw new InvalidOperationException($"No {Name.ToLowerInvariant()} model is configured.");
        lock (_lock)
        {
            if (_task is null || _task.IsFaulted || _task.IsCanceled)
                _task = Run(_load);
            return _task;
        }
    }

    Task IModelSlot.LoadAsync() => GetAsync();

    private async Task<T> Run(Func<IProgress<DownloadProgress>, CancellationToken, Task<T>> load)
    {
        State = SlotState.Loading;
        Error = null;
        Progress = null;
        Changed?.Invoke();
        var started = DateTime.UtcNow;
        try
        {
            Value = await Task.Run(() => load(new SlotProgress(this), _disposed.Token)).ConfigureAwait(false);
            LoadTime = DateTime.UtcNow - started;
            State = SlotState.Ready;
            return Value;
        }
        catch (Exception e)
        {
            Error = e.Message;
            State = SlotState.Failed;
            throw;
        }
        finally
        {
            Changed?.Invoke();
        }
    }

    private void Report(DownloadProgress progress)
    {
        Progress = progress;
        // Downloads report many times a second; listeners need far fewer updates.
        var now = Environment.TickCount64;
        if (now - Interlocked.Read(ref _lastNotify) > 250 || progress.Downloaded == progress.Total)
        {
            Interlocked.Exchange(ref _lastNotify, now);
            Changed?.Invoke();
        }
    }

    public void Dispose()
    {
        _disposed.Cancel();
        Value?.Dispose();
        _disposed.Dispose();
    }

    private sealed class SlotProgress(ModelSlot<T> slot) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => slot.Report(value);
    }
}
