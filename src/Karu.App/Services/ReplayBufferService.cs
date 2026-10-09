using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using Karu.Core;
using Timer = System.Threading.Timer;

namespace Karu.App.Services;

public sealed class ReplayBufferService : IDisposable
{
    /// <summary>Taille max du buffer audio PCM (~20 ms à 48 kHz stéréo 16-bit).</summary>
    private const int AudioChunkBytes = 48000 * 2 * 2 / 50;

    private readonly string _ffmpegPath;
    private readonly string _segmentDirectory;
    private readonly object _gate = new();
    private Process? _ffmpeg;
    private AudioCaptureService? _audio;
    private CancellationTokenSource? _audioCts;
    private Task? _audioPump;
    private Timer? _pruneTimer;
    private int _bufferSeconds;
    private bool _disposed;
    private int _saving;
    private int _prunePaused;
    private int _stopping;
    private readonly StringBuilder _stderrTail = new();
    private string _lastStderr = "";

    public ReplayBufferService(string ffmpegPath, string segmentDirectory)
    {
        _ffmpegPath = ffmpegPath;
        _segmentDirectory = PathSafety.NormalizeDirectory(segmentDirectory);
        _bufferSeconds = AppConfig.DefaultBufferSeconds;
    }

    public bool IsRunning { get; private set; }
    public bool IsPausedForPower { get; private set; }
    public string Status { get; private set; } = "Arrêté";
    public string? LastError { get; private set; }

    public event Action? StatusChanged;
    public event Action<string, ClipTag>? ClipSaved;
    public event Action<string>? SaveFailed;

    public void SetBufferSeconds(int seconds)
    {
        _bufferSeconds = Math.Clamp(seconds, AppConfig.MinBufferSeconds, AppConfig.MaxBufferSeconds);
        PruneSegments();
    }

    /// <summary>Suspendu volontairement (batterie) — Stop + flag.</summary>
    public void PauseForPower()
    {
        lock (_gate)
        {
            if (!IsRunning && IsPausedForPower)
            {
                return;
            }
        }

        Stop();
        IsPausedForPower = true;
        SetStatus("En pause (batterie)");
    }

    public void ResumeFromPower()
    {
        if (_disposed)
        {
            return;
        }

        IsPausedForPower = false;
        if (!IsRunning)
        {
            Start();
        }
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Hors lock : tue les ffmpeg orphelins (même binaire) qui bloquent ddagrab / le pipe audio.
        ChildProcessJob.KillStaleFrom(_ffmpegPath);

        lock (_gate)
        {
            if (IsRunning)
            {
                return;
            }

            Interlocked.Exchange(ref _stopping, 0);
            IsPausedForPower = false;
            Directory.CreateDirectory(_segmentDirectory);
            foreach (var leftover in Directory.EnumerateFiles(_segmentDirectory, "seg_*.ts"))
            {
                TryDelete(leftover);
            }

            var args = FfmpegArgumentBuilder.BuildCaptureArgs(_segmentDirectory);
            var start = new ProcessStartInfo
            {
                FileName = _ffmpegPath,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            _stderrTail.Clear();
            _lastStderr = "";
            _ffmpeg = Process.Start(start) ?? throw new InvalidOperationException("FFmpeg n'a pas démarré.");
            ChildProcessJob.Assign(_ffmpeg);
            _ffmpeg.EnableRaisingEvents = true;
            _ffmpeg.Exited += OnFfmpegExited;
            _ = DrainStderrAsync(_ffmpeg.StandardError);
            _ = DrainAsync(_ffmpeg.StandardOutput);

            _audio = new AudioCaptureService();
            _audio.Start();
            _audioCts = new CancellationTokenSource();
            var stdin = _ffmpeg.StandardInput.BaseStream;
            _audioPump = Task.Run(() => PumpAudio(stdin, _audioCts.Token));

            _pruneTimer = new Timer(_ => PruneSegments(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
            IsRunning = true;
            SetStatus("Buffer actif");
            AppLog.WriteAlways("buffer started pid=" + _ffmpeg.Id);
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (!IsRunning)
            {
                return;
            }

            Interlocked.Exchange(ref _stopping, 1);
            _pruneTimer?.Dispose();
            _pruneTimer = null;
            _audioCts?.Cancel();
            try { _audioPump?.Wait(1000); } catch { /* ignore */ }
            _audioPump = null;
            _audioCts?.Dispose();
            _audioCts = null;
            _audio?.Dispose();
            _audio = null;

            try
            {
                if (_ffmpeg is not null)
                {
                    _ffmpeg.Exited -= OnFfmpegExited;
                    if (!_ffmpeg.HasExited)
                    {
                        try { _ffmpeg.StandardInput.Close(); } catch { /* ignore */ }
                        if (!_ffmpeg.WaitForExit(1500))
                        {
                            _ffmpeg.Kill(entireProcessTree: true);
                            _ffmpeg.WaitForExit(2000);
                        }
                    }
                }
            }
            catch
            {
                // ignore
            }

            _ffmpeg?.Dispose();
            _ffmpeg = null;
            IsRunning = false;
            SetStatus("Arrêté");
        }
    }

    public Task SaveClipAsync(string saveDirectory, string gameHint = "Valorant") =>
        SaveClipAsync(saveDirectory, ClipTag.Manual, gameHint);

    public Task SaveClipAsync(string saveDirectory, ClipTag tag, string gameHint = "Valorant") =>
        SaveClipCoreAsync(saveDirectory, tag, window: null, gameHint);

    public Task SaveClipAsync(string saveDirectory, HighlightClipRequest request, string gameHint = "Valorant") =>
        SaveClipCoreAsync(
            saveDirectory,
            request.Tag,
            (request.WindowStartUtc, request.WindowEndUtc),
            gameHint);

    private async Task SaveClipCoreAsync(
        string saveDirectory,
        ClipTag tag,
        (DateTime StartUtc, DateTime EndUtc)? window,
        string gameHint)
    {
        if (Interlocked.Exchange(ref _saving, 1) == 1)
        {
            return;
        }

        Interlocked.Exchange(ref _prunePaused, 1);
        try
        {
            bool running;
            lock (_gate)
            {
                running = IsRunning;
            }

            if (!running)
            {
                SaveFailed?.Invoke("Le buffer n'est pas actif.");
                return;
            }

            if (!PathSafety.TryEnsureWritableDirectory(saveDirectory, out var destDir, out var ioError))
            {
                SaveFailed?.Invoke(ioError ?? "Dossier de sauvegarde inaccessible.");
                return;
            }

            SetStatus("Sauvegarde…");

            string[] files;
            try
            {
                files = Directory.GetFiles(_segmentDirectory, "seg_*.ts");
            }
            catch (Exception ex)
            {
                SaveFailed?.Invoke(PathSafety.FriendlyIoMessage(ex));
                return;
            }

            var writing = files.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).LastOrDefault();
            IReadOnlyList<string> selected;
            if (window is { } w)
            {
                selected = SegmentRetention.FilesToSaveInWindow(files, w.StartUtc, w.EndUtc, writing);
                if (selected.Count == 0)
                {
                    // Filet : fenêtrage trop strict / timestamps → buffer classique
                    selected = SegmentRetention.FilesToSave(files, _bufferSeconds, writing);
                }
            }
            else
            {
                selected = SegmentRetention.FilesToSave(files, _bufferSeconds, writing);
            }

            if (selected.Count == 0)
            {
                SaveFailed?.Invoke("Pas encore assez de replay. Attends quelques secondes.");
                return;
            }

            var output = ClipFileNamer.MakeFullPath(destDir, DateTime.Now, gameHint);
            var workDir = Path.Combine(
                Path.GetTempPath(),
                "Karu",
                Guid.NewGuid().ToString("N"));

            try
            {
                Directory.CreateDirectory(workDir);
                var copied = new List<string>(selected.Count);
                foreach (var file in selected)
                {
                    var dest = Path.Combine(workDir, Path.GetFileName(file));
                    File.Copy(file, dest, overwrite: true);
                    copied.Add(dest);
                }

                var listPath = Path.Combine(workDir, "list.txt");
                await File.WriteAllTextAsync(
                    listPath,
                    FfmpegArgumentBuilder.BuildConcatList(copied),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                var concat = new ProcessStartInfo
                {
                    FileName = _ffmpegPath,
                    Arguments = FfmpegArgumentBuilder.BuildConcatArgs(listPath, output),
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(concat)
                                    ?? throw new InvalidOperationException("FFmpeg concat n'a pas démarré.");
                var stderr = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
                if (process.ExitCode != 0 || !File.Exists(output))
                {
                    SaveFailed?.Invoke(string.IsNullOrWhiteSpace(stderr)
                        ? "Échec de la sauvegarde du clip."
                        : "Échec FFmpeg lors de la sauvegarde.");
                    AppLog.Write("concat fail: " + stderr);
                    return;
                }

                ClipSaved?.Invoke(output, tag);
            }
            finally
            {
                try { Directory.Delete(workDir, recursive: true); } catch { /* ignore */ }
            }
        }
        catch (Exception ex)
        {
            SaveFailed?.Invoke(PathSafety.FriendlyIoMessage(ex));
            AppLog.Write("save: " + ex);
        }
        finally
        {
            Interlocked.Exchange(ref _prunePaused, 0);
            Interlocked.Exchange(ref _saving, 0);
            if (IsRunning)
            {
                SetStatus("Buffer actif");
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
        try
        {
            if (Directory.Exists(_segmentDirectory))
            {
                foreach (var leftover in Directory.EnumerateFiles(_segmentDirectory, "seg_*.ts"))
                {
                    TryDelete(leftover);
                }
            }
        }
        catch
        {
            // ignore
        }
    }

    private void OnFfmpegExited(object? sender, EventArgs e)
    {
        if (_disposed || Volatile.Read(ref _stopping) != 0)
        {
            return;
        }

        var code = -1;
        try { code = _ffmpeg?.ExitCode ?? -1; } catch { /* ignore */ }
        lock (_stderrTail)
        {
            _lastStderr = _stderrTail.ToString();
        }

        AppLog.WriteAlways("ffmpeg exited code=" + code + " " + _lastStderr);
        ThreadPool.QueueUserWorkItem(_ => HandleUnexpectedFfmpegExit());
    }

    private void HandleUnexpectedFfmpegExit()
    {
        if (_disposed || Volatile.Read(ref _stopping) != 0)
        {
            return;
        }

        lock (_gate)
        {
            if (!IsRunning)
            {
                return;
            }
        }

        try { Stop(); } catch { /* ignore */ }

        if (!_disposed && !IsPausedForPower)
        {
            LastError = string.IsNullOrWhiteSpace(_lastStderr) ? "FFmpeg s'est arrêté." : _lastStderr.Trim();
            SetStatus("Capture interrompue");
            AppLog.WriteAlways("buffer unexpected stop: " + LastError);
        }
    }

    private void PumpAudio(Stream stdin, CancellationToken token)
    {
        var buffer = new byte[AudioChunkBytes];
        while (!token.IsCancellationRequested && _audio is not null)
        {
            try
            {
                if (_ffmpeg is { HasExited: true })
                {
                    break;
                }

                var read = _audio.Read(buffer, 0, buffer.Length);
                if (read <= 0)
                {
                    Array.Clear(buffer, 0, buffer.Length);
                    read = buffer.Length;
                }

                stdin.Write(buffer, 0, read);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LastError = ex.Message;
                AppLog.WriteAlways("audio pump: " + ex.Message);
                if (ex is IOException or ObjectDisposedException || _ffmpeg is { HasExited: true })
                {
                    if (IsRunning && Volatile.Read(ref _stopping) == 0 && !IsPausedForPower)
                    {
                        SetStatus("Erreur capture (pipe)");
                    }

                    break;
                }

                Thread.Sleep(20);
            }
        }
    }

    private void PruneSegments()
    {
        if (Volatile.Read(ref _prunePaused) != 0 || Volatile.Read(ref _saving) != 0)
        {
            return;
        }

        try
        {
            if (!Directory.Exists(_segmentDirectory))
            {
                return;
            }

            var files = Directory.GetFiles(_segmentDirectory, "seg_*.ts");
            if (files.Length == 0)
            {
                return;
            }

            var writing = files.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).Last();
            foreach (var doomed in SegmentRetention.FilesToDelete(files, _bufferSeconds, writing))
            {
                TryDelete(doomed);
            }
        }
        catch
        {
            // ignore
        }
    }

    private void SetStatus(string status)
    {
        Status = status;
        StatusChanged?.Invoke();
    }

    private async Task DrainStderrAsync(StreamReader reader)
    {
        try
        {
            var buffer = new char[512];
            int n;
            while ((n = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                lock (_stderrTail)
                {
                    _stderrTail.Append(buffer, 0, n);
                    if (_stderrTail.Length > 2000)
                    {
                        _stderrTail.Remove(0, _stderrTail.Length - 1500);
                    }
                }
            }
        }
        catch
        {
            // ignore
        }
    }

    private static async Task DrainAsync(StreamReader reader)
    {
        try
        {
            var buffer = new char[1024];
            while (await reader.ReadAsync(buffer, 0, buffer.Length) > 0)
            {
                // discard
            }
        }
        catch
        {
            // ignore
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* still being written */ }
    }
}
