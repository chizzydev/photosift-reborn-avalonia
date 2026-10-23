using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace PhotoSiftReborn;

/// <summary>
/// One serial background decode; one replaceable pending request; bounded cache.
/// A cancellation token does not stop an already running native Magick operation.
/// Late results are disposed without ever becoming an incorrect visible photo.
/// </summary>
internal sealed class PreviewWorker : IDisposable
{
    public sealed record Request(Guid PhotoId, string Path, long Length, long WriteTicks, long Generation);
    private readonly object _sync = new();
    private readonly SemaphoreSlim _wake = new(0);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly PreviewCache _cache = new();
    private Request? _pending;
    private sealed record InspectionTask(Request Photo, TaskCompletionSource<byte[]> Completion, CancellationToken Cancellation);
    private InspectionTask? _inspectionPending;
    private bool _signalled, _disposed;
    private long _latestGeneration;
    private int _active, _maxActive;
    public int ActiveCount => Volatile.Read(ref _active);
    public int MaxActiveObserved => Volatile.Read(ref _maxActive);
    public int PendingCount { get { lock (_sync) return _pending is null ? 0 : 1; } }
    public string CacheMetrics { get { lock (_sync) return $"cache {_cache.Count} item(s), {_cache.Bytes / 1048576.0:F1} MiB / 48 MiB encoded; hits {_cache.Hits}; evictions {_cache.Evictions}"; } }
    public event Action<Request, Bitmap?, string?>? Completed;

    public PreviewWorker() => _ = Task.Run(ProcessAsync);

    public void Enqueue(Request request)
    {
        lock (_sync)
        {
            if (_disposed) return;
            _pending = request;
            Volatile.Write(ref _latestGeneration, request.Generation);
            if (!_signalled) { _signalled = true; _wake.Release(); }
        }
    }

    /// <summary>One bounded, on-demand native-size read through the same single native decode loop.</summary>
    public Task<byte[]> DecodeOriginalAsync(Request request, CancellationToken cancellation = default)
    {
        var completion = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_sync)
        {
            if (_disposed) { completion.TrySetCanceled(); return completion.Task; }
            if (_inspectionPending is not null)
                throw new InvalidOperationException("A full-resolution inspection is already queued.");
            _inspectionPending = new InspectionTask(request,completion,cancellation);
            if (!_signalled) { _signalled=true; _wake.Release(); }
        }
        return completion.Task;
    }

    private async Task ProcessAsync()
    {
        while (true)
        {
            try { await _wake.WaitAsync(_shutdown.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
            Request? request;
            InspectionTask? inspection;
            lock (_sync)
            {
                inspection = _inspectionPending; _inspectionPending=null;
                request = inspection is null ? _pending : null;
                if (inspection is null) _pending=null;
                _signalled=false;
                if (inspection is not null && _pending is not null) { _signalled=true; _wake.Release(); }
            }
            if (inspection is not null)
            {
                try
                {
                    inspection.Cancellation.ThrowIfCancellationRequested();
                    CheckSourceVersion(inspection.Photo);
                    var bytes=MagickPreviewDecoder.DecodeToPng(inspection.Photo.Path,originalPixels:true);
                    CheckSourceVersion(inspection.Photo);
                    inspection.Cancellation.ThrowIfCancellationRequested();
                    if (_shutdown.IsCancellationRequested) inspection.Completion.TrySetCanceled();
                    else inspection.Completion.TrySetResult(bytes);
                }
                catch (OperationCanceledException) { inspection.Completion.TrySetCanceled(); }
                catch (Exception ex) { inspection.Completion.TrySetException(ex); }
                continue;
            }
            if (request is null) continue;
            Bitmap? bitmap = null;
            string? error = null;
            var running = Interlocked.Increment(ref _active);
            if (running > MaxActiveObserved) Volatile.Write(ref _maxActive, running);
            try
            {
                var key = new PreviewCache.Key(request.PhotoId, request.Path, request.Length, request.WriteTicks);
                byte[]? encoded;
                lock (_sync)
                {
                    if (!_cache.TryGet(key, out encoded)) encoded = null;
                }
                if (encoded is null)
                {
                    encoded = MagickPreviewDecoder.DecodeToPng(request.Path);
                    if (_shutdown.IsCancellationRequested || request.Generation != Volatile.Read(ref _latestGeneration))
                        continue;
                    lock (_sync) { if (!_disposed) _cache.Put(key, encoded); }
                }
                CheckSourceVersion(request);
                if (_shutdown.IsCancellationRequested || request.Generation != Volatile.Read(ref _latestGeneration)) continue;
                using var memory = new MemoryStream(encoded, writable: false);
                bitmap = new Bitmap(memory);
                CheckSourceVersion(request);
            }
            catch (Exception ex) { bitmap?.Dispose(); bitmap = null; error = ex.Message; }
            finally { Interlocked.Decrement(ref _active); }
            if (_shutdown.IsCancellationRequested) { bitmap?.Dispose(); break; }
            if (request.Generation != Volatile.Read(ref _latestGeneration)) { bitmap?.Dispose(); continue; }
            var result = bitmap;
            Dispatcher.UIThread.Post(() =>
            {
                if (_shutdown.IsCancellationRequested || request.Generation != Volatile.Read(ref _latestGeneration))
                { result?.Dispose(); return; }
                Completed?.Invoke(request, result, error);
            });
        }
    }

    private static void CheckSourceVersion(Request request)
    {
        var info = new FileInfo(request.Path);
        if (!info.Exists || info.Length != request.Length || info.LastWriteTimeUtc.Ticks != request.WriteTicks)
            throw new IOException("Photo changed since import or during decode. Reimport to continue.");
    }

    public void Dispose()
    {
        lock (_sync) { if (_disposed) return; _disposed = true; _pending = null;
            _inspectionPending?.Completion.TrySetCanceled(); _inspectionPending=null; _cache.Clear(); }
        _shutdown.Cancel();
    }
}
