using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using ClipBuffer.Core;
using Timer = System.Threading.Timer;

namespace ClipBuffer.App.Services;

public sealed class ReplayBufferService : IDisposable
{
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

    public ReplayBufferService(string ffmpegPath, string segmentDirectory)
    {
        _ffmpegPath = ffmpegPath;
        _segmentDirectory = segmentDirectory;
        _bufferSeconds = AppConfig.DefaultBufferSeconds;
    }

    public bool IsRunning { get; private set; }
    public string Status { get; private set; } = "Arrêté";
    public string? LastError { get; private set; }

    public event Action? StatusChanged;
    public event Action<string>? ClipSaved;
    public event Action<string>? SaveFailed;

    public void SetBufferSeconds(int seconds)
    {
        _bufferSeconds = seconds;
        PruneSegments();
    }

    public void Start()
    {
        lock (_gate)
        {
            if (IsRunning)
            {
                return;
            }

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

            _ffmpeg = Process.Start(start) ?? throw new InvalidOperationException("FFmpeg n'a pas démarré.");
            _ = DrainAsync(_ffmpeg.StandardError);
            _ = DrainAsync(_ffmpeg.StandardOutput);

            _audio = new AudioCaptureService();
            _audio.Start();
            _audioCts = new CancellationTokenSource();
            var stdin = _ffmpeg.StandardInput.BaseStream;
            _audioPump = Task.Run(() => PumpAudio(stdin, _audioCts.Token));

            _pruneTimer = new Timer(_ => PruneSegments(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
            IsRunning = true;
            SetStatus("Buffer actif");
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

            _pruneTimer?.Dispose();
            _pruneTimer = null;
            _audioCts?.Cancel();
            try { _audioPump?.Wait(1000); } catch { /* ignore */ }
            _audio?.Dispose();
            _audio = null;

            try
            {
                if (_ffmpeg is { HasExited: false })
                {
                    _ffmpeg.StandardInput.Close();
                    if (!_ffmpeg.WaitForExit(1500))
                    {
                        _ffmpeg.Kill(entireProcessTree: true);
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

    public async Task SaveClipAsync(string saveDirectory, string gameHint = "Valorant")
    {
        if (Interlocked.Exchange(ref _saving, 1) == 1)
        {
            return;
        }

        try
        {
            if (!IsRunning)
            {
                SaveFailed?.Invoke("Le buffer n'est pas actif.");
                return;
            }

            SetStatus("Sauvegarde…");
            Directory.CreateDirectory(saveDirectory);

            var files = Directory.GetFiles(_segmentDirectory, "seg_*.ts");
            var writing = files.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).LastOrDefault();
            var selected = SegmentRetention.FilesToSave(files, _bufferSeconds, writing);
            if (selected.Count == 0)
            {
                SaveFailed?.Invoke("Pas encore assez de replay. Attends quelques secondes.");
                return;
            }

            var output = ClipFileNamer.MakeFullPath(saveDirectory, DateTime.Now, gameHint);
            var workDir = Path.Combine(Path.GetTempPath(), "ClipBuffer", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workDir);

            try
            {
                var copied = new List<string>();
                foreach (var file in selected)
                {
                    var dest = Path.Combine(workDir, Path.GetFileName(file));
                    File.Copy(file, dest, overwrite: true);
                    copied.Add(dest);
                }

                var listPath = Path.Combine(workDir, "list.txt");
                await File.WriteAllTextAsync(listPath, FfmpegArgumentBuilder.BuildConcatList(copied), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                var concat = new ProcessStartInfo
                {
                    FileName = _ffmpegPath,
                    Arguments = FfmpegArgumentBuilder.BuildConcatArgs(listPath, output),
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(concat) ?? throw new InvalidOperationException("FFmpeg concat n'a pas démarré.");
                var stderr = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
                if (process.ExitCode != 0 || !File.Exists(output))
                {
                    SaveFailed?.Invoke(string.IsNullOrWhiteSpace(stderr) ? "Échec de la sauvegarde du clip." : stderr.Trim());
                    return;
                }

                ClipSaved?.Invoke(output);
            }
            finally
            {
                try { Directory.Delete(workDir, recursive: true); } catch { /* ignore */ }
            }
        }
        catch (Exception ex)
        {
            SaveFailed?.Invoke(ex.Message);
        }
        finally
        {
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
    }

    private void PumpAudio(Stream stdin, CancellationToken token)
    {
        var buffer = new byte[48000 * 2 * 2 / 50];
        try
        {
            while (!token.IsCancellationRequested && _audio is not null)
            {
                var read = _audio.Read(buffer, 0, buffer.Length);
                stdin.Write(buffer, 0, read);
                stdin.Flush();
            }
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            SetStatus("Erreur audio");
        }
    }

    private void PruneSegments()
    {
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

    private static async Task DrainAsync(StreamReader reader)
    {
        try
        {
            await reader.ReadToEndAsync();
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
