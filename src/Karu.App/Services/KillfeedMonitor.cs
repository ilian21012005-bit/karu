using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using Karu.Core;

namespace Karu.App.Services;

public sealed class KillfeedMonitor : IDisposable
{
    private readonly KillStreakTracker _tracker = new();
    private readonly HighlightClipScheduler _scheduler = new();
    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private AppConfig _config = AppConfig.CreateDefault();
    private bool _disposed;
    private string _lastOcr = "";

    public KillfeedMonitor()
    {
        _tracker.ThresholdReached += OnThreshold;
    }

    public event Action<HighlightClipRequest>? HighlightTriggered;
    public event Action<string>? StatusChanged;

    public void ApplyConfig(AppConfig config)
    {
        lock (_gate)
        {
            _config = CloneConfig(config);
            _config.Clamp();
        }

        // Si highlights (re)activés après un échec OCR, tenter un restart.
        if (_config.HighlightsEnabled)
        {
            Start();
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (_loop is { IsCompleted: false })
            {
                return;
            }

            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _loop = Task.Run(() => RunLoopAsync(token), token);
        }
    }

    public void Stop()
    {
        CancellationTokenSource? cts;
        Task? loop;
        lock (_gate)
        {
            cts = _cts;
            loop = _loop;
            _cts = null;
            _loop = null;
        }

        try { cts?.Cancel(); } catch { /* ignore */ }
        try { loop?.Wait(2500); } catch { /* ignore */ }
        cts?.Dispose();
        _tracker.Reset();
        _scheduler.Reset();
        _lastOcr = "";
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _tracker.ThresholdReached -= OnThreshold;
        Stop();
    }

    private void OnThreshold(int streak)
    {
        AppConfig config;
        lock (_gate)
        {
            config = _config;
        }

        var tag = streak switch
        {
            3 => ClipTag.Triple,
            4 => ClipTag.Quad,
            5 => ClipTag.Ace,
            _ => ClipTag.Manual
        };

        var enabled = tag switch
        {
            ClipTag.Triple => config.AutoTriple,
            ClipTag.Quad => config.AutoQuad,
            ClipTag.Ace => config.AutoAce,
            _ => false
        };

        if (!enabled)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var first = _tracker.FirstKillUtc;
        var last = _tracker.LastKillUtc;
        if (first == DateTime.MinValue)
        {
            first = last == DateTime.MinValue ? now : last;
        }

        if (last == DateTime.MinValue)
        {
            last = now;
        }

        _scheduler.Cooldown = TimeSpan.FromSeconds(config.HighlightCooldownSeconds);
        _scheduler.NotifyThreshold(tag, first, last, now);
    }

    private void PollScheduler()
    {
        var ready = _scheduler.TryDequeueReady(DateTime.UtcNow);
        if (ready is { } request)
        {
            HighlightTriggered?.Invoke(request);
        }
    }

    private async Task RunLoopAsync(CancellationToken token)
    {
        try
        {
            Thread.CurrentThread.Priority = ThreadPriority.Lowest;

            OcrEngine? engine = null;
            while (!token.IsCancellationRequested && engine is null)
            {
                try
                {
                    engine = OcrEngine.TryCreateFromUserProfileLanguages()
                             ?? OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en-US"));
                }
                catch
                {
                    engine = null;
                }

                if (engine is null)
                {
                    StatusChanged?.Invoke("OCR killfeed indisponible (réessai…).");
                    AppLog.Write("killfeed: OCR engine null, retry in 15s");
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(15), token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }
            }

            if (engine is null || token.IsCancellationRequested)
            {
                return;
            }

            StatusChanged?.Invoke("Killfeed OCR prêt.");

            while (!token.IsCancellationRequested)
            {
                AppConfig config;
                lock (_gate)
                {
                    config = _config;
                }

                var shouldRun = config.HighlightsEnabled
                                && !string.IsNullOrWhiteSpace(config.PlayerName)
                                && (config.AutoTriple || config.AutoQuad || config.AutoAce);

                if (shouldRun)
                {
                    try
                    {
                        await TickAsync(engine, config, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        AppLog.Write("killfeed tick: " + ex.Message);
                    }
                }

                PollScheduler();

                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(350), token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        finally
        {
            // Permet un vrai Start() ultérieur (plus de boucle zombie).
            lock (_gate)
            {
                if (_loop?.Id == Task.CurrentId || _loop?.IsCompleted == true)
                {
                    _loop = null;
                }
            }
        }
    }

    private async Task TickAsync(OcrEngine engine, AppConfig config, CancellationToken token)
    {
        var bounds = System.Windows.Forms.Screen.PrimaryScreen?.Bounds
                     ?? new Rectangle(0, 0, 1920, 1080);
        var local = config.GetKillfeedPixelRect(bounds.Width, bounds.Height);
        var rect = new ScreenRect(bounds.X + local.X, bounds.Y + local.Y, local.Width, local.Height);

        using var bitmap = CaptureRegion(rect);
        token.ThrowIfCancellationRequested();
        var text = await RecognizeAsync(engine, bitmap, token).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(text) || text == _lastOcr)
        {
            return;
        }

        _lastOcr = text;
        _tracker.ObserveOcrText(text, config.PlayerName, DateTime.UtcNow);
    }

    private static Bitmap CaptureRegion(ScreenRect rect)
    {
        var bmp = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(rect.X, rect.Y, 0, 0, new Size(rect.Width, rect.Height), CopyPixelOperation.SourceCopy);
        return bmp;
    }

    private static async Task<string> RecognizeAsync(OcrEngine engine, Bitmap bitmap, CancellationToken token)
    {
        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Png);
        var bytes = ms.ToArray();

        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(bytes.AsBuffer()).AsTask(token).ConfigureAwait(false);
        stream.Seek(0);

        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(token).ConfigureAwait(false);
        using var softwareBitmap = await decoder.GetSoftwareBitmapAsync().AsTask(token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        var result = await engine.RecognizeAsync(softwareBitmap).AsTask(token).ConfigureAwait(false);
        return result.Text ?? "";
    }

    private static AppConfig CloneConfig(AppConfig source) => new()
    {
        BufferSeconds = source.BufferSeconds,
        Hotkey = source.Hotkey,
        SaveDirectory = source.SaveDirectory,
        AutoTriple = source.AutoTriple,
        AutoQuad = source.AutoQuad,
        AutoAce = source.AutoAce,
        HighlightCooldownSeconds = source.HighlightCooldownSeconds,
        PlayerName = source.PlayerName,
        HighlightsEnabled = source.HighlightsEnabled,
        Theme = source.Theme,
        StartWithWindows = source.StartWithWindows,
        PauseBufferOnBattery = source.PauseBufferOnBattery,
        CheckUpdatesOnStartup = source.CheckUpdatesOnStartup,
        KillfeedX = source.KillfeedX,
        KillfeedY = source.KillfeedY,
        KillfeedW = source.KillfeedW,
        KillfeedH = source.KillfeedH
    };
}
