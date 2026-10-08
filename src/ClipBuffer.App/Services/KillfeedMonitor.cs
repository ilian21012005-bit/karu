using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using ClipBuffer.Core;

namespace ClipBuffer.App.Services;

public sealed class KillfeedMonitor : IDisposable
{
    private readonly KillStreakTracker _tracker = new();
    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private AppConfig _config = AppConfig.CreateDefault();
    private DateTime _lastFireUtc = DateTime.MinValue;
    private bool _disposed;
    private string _lastOcr = "";

    public KillfeedMonitor()
    {
        _tracker.ThresholdReached += OnThreshold;
    }

    public event Action<ClipTag>? HighlightTriggered;

    public void ApplyConfig(AppConfig config)
    {
        lock (_gate)
        {
            _config = CloneConfig(config);
            _config.Clamp();
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_loop is not null)
            {
                return;
            }

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
        try { loop?.Wait(1000); } catch { /* ignore */ }
        cts?.Dispose();
        _tracker.Reset();
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

        var cooldown = TimeSpan.FromSeconds(config.HighlightCooldownSeconds);
        if (DateTime.UtcNow - _lastFireUtc < cooldown)
        {
            return;
        }

        _lastFireUtc = DateTime.UtcNow;
        HighlightTriggered?.Invoke(tag);
    }

    private async Task RunLoopAsync(CancellationToken token)
    {
        Thread.CurrentThread.Priority = ThreadPriority.Lowest;
        OcrEngine? engine = null;
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
            return;
        }

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
                catch
                {
                    // keep looping
                }
            }

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

    private async Task TickAsync(OcrEngine engine, AppConfig config, CancellationToken token)
    {
        var bounds = System.Windows.Forms.Screen.PrimaryScreen?.Bounds
                     ?? new Rectangle(0, 0, 1920, 1080);
        var local = config.GetKillfeedPixelRect(bounds.Width, bounds.Height);
        var rect = new ScreenRect(bounds.X + local.X, bounds.Y + local.Y, local.Width, local.Height);

        using var bitmap = CaptureRegion(rect);
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
        await stream.WriteAsync(bytes.AsBuffer());
        stream.Seek(0);

        var decoder = await BitmapDecoder.CreateAsync(stream);
        using var softwareBitmap = await decoder.GetSoftwareBitmapAsync();
        token.ThrowIfCancellationRequested();
        var result = await engine.RecognizeAsync(softwareBitmap);
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
        KillfeedX = source.KillfeedX,
        KillfeedY = source.KillfeedY,
        KillfeedW = source.KillfeedW,
        KillfeedH = source.KillfeedH
    };
}
