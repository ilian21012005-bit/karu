using System.Collections.Concurrent;
using System.Diagnostics;
using ClipBuffer.Core;

namespace ClipBuffer.App.Services;

public sealed class ThumbnailService : IDisposable
{
    private readonly string _ffmpegPath;
    private readonly ClipLibrary _library = new();
    private readonly object _gate = new();
    private readonly HashSet<string> _inFlight = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _failed = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<string> _queue = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _waiters =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _signal = new(0);
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _worker;
    private bool _disposed;

    public ThumbnailService(string ffmpegPath)
    {
        _ffmpegPath = ffmpegPath;
        _worker = Task.Run(() => WorkerLoopAsync(_cts.Token));
    }

    public static string ThumbPathFor(string videoPath) =>
        ClipArtifactPaths.ThumbnailPathFor(videoPath);

    public Task GenerateAsync(string videoPath, CancellationToken cancellationToken = default)
    {
        if (_disposed || string.IsNullOrWhiteSpace(videoPath))
        {
            return Task.CompletedTask;
        }

        if (File.Exists(ThumbPathFor(videoPath)))
        {
            return Task.CompletedTask;
        }

        var tcs = _waiters.GetOrAdd(
            videoPath,
            _ => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously));
        Enqueue(videoPath);
        if (cancellationToken.CanBeCanceled)
        {
            cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        }

        return tcs.Task;
    }

    public Task GenerateMissingAsync(IEnumerable<string> videoPaths, CancellationToken cancellationToken = default)
    {
        foreach (var path in videoPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Enqueue(path);
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cts.Cancel();
        _signal.Release();
        try { _worker.Wait(2000); } catch { /* ignore */ }
        foreach (var waiter in _waiters.Values)
        {
            waiter.TrySetCanceled();
        }

        _waiters.Clear();
        _cts.Dispose();
        _signal.Dispose();
    }

    private void Enqueue(string videoPath)
    {
        if (_disposed || string.IsNullOrWhiteSpace(videoPath))
        {
            return;
        }

        if (!File.Exists(videoPath))
        {
            CompleteWaiter(videoPath, false);
            return;
        }

        if (File.Exists(ThumbPathFor(videoPath)))
        {
            CompleteWaiter(videoPath, true);
            return;
        }

        lock (_gate)
        {
            if (_failed.Contains(videoPath))
            {
                CompleteWaiter(videoPath, false);
                return;
            }

            if (_inFlight.Contains(videoPath))
            {
                return;
            }
        }

        _queue.Enqueue(videoPath);
        _signal.Release();
    }

    private async Task WorkerLoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await _signal.WaitAsync(token).ConfigureAwait(false);
                while (_queue.TryDequeue(out var path))
                {
                    if (token.IsCancellationRequested)
                    {
                        return;
                    }

                    Generate(path);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutdown
        }
    }

    private void Generate(string videoPath)
    {
        try
        {
            if (!File.Exists(videoPath) || !File.Exists(_ffmpegPath))
            {
                CompleteWaiter(videoPath, false);
                return;
            }

            var thumb = ThumbPathFor(videoPath);
            lock (_gate)
            {
                if (_failed.Contains(videoPath) || !_inFlight.Add(videoPath))
                {
                    return;
                }
            }

            try
            {
                if (File.Exists(thumb))
                {
                    _library.SetThumbnail(videoPath, thumb);
                    CompleteWaiter(videoPath, true);
                    return;
                }

                Thread.CurrentThread.Priority = ThreadPriority.Lowest;
                ClipArtifactPaths.EnsureMetaDirectory(videoPath);
                ClipArtifactPaths.MigrateLegacyIfNeeded(videoPath);

                var args =
                    "-hide_banner -loglevel error -y " +
                    $"-i \"{videoPath}\" -an -frames:v 1 -vf \"scale=320:-1\" -q:v 4 \"{thumb}\"";

                var start = new ProcessStartInfo
                {
                    FileName = _ffmpegPath,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };

                using var process = Process.Start(start);
                if (process is null)
                {
                    MarkFailed(videoPath);
                    CompleteWaiter(videoPath, false);
                    return;
                }

                var stderr = process.StandardError.ReadToEnd();
                if (!process.WaitForExit(30_000))
                {
                    try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
                    MarkFailed(videoPath);
                    CompleteWaiter(videoPath, false);
                    AppLog.Write("thumb timeout: " + videoPath);
                    return;
                }

                if (process.ExitCode == 0 && File.Exists(thumb) && new FileInfo(thumb).Length > 0)
                {
                    _library.SetThumbnail(videoPath, thumb);
                    CompleteWaiter(videoPath, true);
                    AppLog.Write("thumb ok: " + Path.GetFileName(thumb));
                }
                else
                {
                    MarkFailed(videoPath);
                    CompleteWaiter(videoPath, false);
                    AppLog.Write("thumb fail: " + Path.GetFileName(videoPath) + " " + stderr);
                    try { if (File.Exists(thumb)) File.Delete(thumb); } catch { /* ignore */ }
                }
            }
            finally
            {
                lock (_gate)
                {
                    _inFlight.Remove(videoPath);
                }
            }
        }
        catch (Exception ex)
        {
            MarkFailed(videoPath);
            CompleteWaiter(videoPath, false);
            AppLog.Write("thumb ex: " + ex.Message);
        }
    }

    private void MarkFailed(string videoPath)
    {
        lock (_gate)
        {
            _failed.Add(videoPath);
        }
    }

    private void CompleteWaiter(string videoPath, bool ok)
    {
        if (_waiters.TryRemove(videoPath, out var tcs))
        {
            tcs.TrySetResult(ok);
        }
    }
}
