using System.IO;
using System.Windows;
using Karu.App.Services;
using Karu.Core;

namespace Karu.App;

public partial class App : System.Windows.Application
{
    private Mutex? _mutex;
    private AppConfig _config = AppConfig.CreateDefault();
    private MainWindow? _window;
    private TrayService? _tray;
    private GlobalHotkeyService? _hotkey;
    private ReplayBufferService? _buffer;
    private ThumbnailService? _thumbnails;
    private KillfeedMonitor? _killfeed;
    private PowerMonitorService? _power;
    private GameForegroundMonitor? _gameFocus;
    private Timer? _capturePolicyTimer;
    private readonly ClipLibrary _library = new();
    private int _thumbRefreshQueued;
    private int _policyBusy;
    private int _policyPending;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            AppLog.Write("Dispatcher: " + args.Exception);
            args.Handled = true;
        };

        try
        {
            StartCore();
        }
        catch (Exception ex)
        {
            AppLog.Write("Startup fatal: " + ex);
            MessageBox.Show(ex.Message, "Karu", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    private void StartCore()
    {
        _mutex = new Mutex(true, @"Local\Karu.SingleInstance", out var created);
        if (!created)
        {
            _mutex.Dispose();
            _mutex = null;
            MessageBox.Show("Karu est déjà lancé.", "Karu", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        try
        {
            System.Diagnostics.Process.GetCurrentProcess().PriorityClass =
                System.Diagnostics.ProcessPriorityClass.BelowNormal;
        }
        catch
        {
            // ignore
        }

        _config = ConfigStore.Load(ConfigStore.DefaultPath);
        try
        {
            ConfigStore.Save(ConfigStore.DefaultPath, _config);
        }
        catch (Exception ex)
        {
            AppLog.Write("config save: " + ex.Message);
        }

        if (!PathSafety.TryEnsureWritableDirectory(_config.SaveDirectory, out var saveDir, out _))
        {
            saveDir = AppConfig.DefaultSaveDirectory();
            PathSafety.TryEnsureWritableDirectory(saveDir, out saveDir, out _);
            _config.SaveDirectory = saveDir;
        }
        else
        {
            _config.SaveDirectory = saveDir;
        }

        ThemeService.Apply(_config.Theme);
        GpuPreference.PreferHighPerformanceGpu();
        try
        {
            StartupRegistration.SetEnabled(_config.StartWithWindows);
        }
        catch (Exception ex)
        {
            AppLog.Write("startup reg: " + ex.Message);
        }

        _window = new MainWindow(_config);
        _window.SettingsChanged += OnSettingsChanged;
        _window.MissingThumbnailsRequested += OnMissingThumbnails;

        _tray = new TrayService();
        _tray.ShowSettingsRequested += ShowSettings;
        _tray.ExitRequested += ExitApp;

        _window.Show();
        RunOnboardingIfNeeded();

        var ffmpeg = FfmpegLocator.Find();
        AppLog.Write("ffmpeg=" + (ffmpeg ?? "(null)"));
        if (ffmpeg is null)
        {
            _window.SetStatus("FFmpeg introuvable. Place ffmpeg.exe dans tools/ffmpeg.");
            return;
        }

        _window.SetStatus("Détection NVENC…");

        _ = Task.Run(() =>
        {
            NvencProbeResult probe;
            try
            {
                probe = NvencProbe.Run(ffmpeg);
            }
            catch (Exception ex)
            {
                AppLog.Write("nvenc probe: " + ex);
                Dispatcher.BeginInvoke(() =>
                {
                    _window?.SetStatus("Erreur NVENC : " + ex.Message);
                });
                return;
            }

            Dispatcher.BeginInvoke(() => ContinueAfterNvenc(ffmpeg, probe));
        });
    }

    private void ContinueAfterNvenc(string ffmpeg, NvencProbeResult probe)
    {
        AppLog.Write("nvenc=" + probe.Ok + " " + probe.Message);
        if (!probe.Ok)
        {
            _window?.SetStatus(probe.Message);
            return;
        }

        try
        {
            _thumbnails = new ThumbnailService(ffmpeg);
            _window?.RefreshClips();

            var segmentDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Karu",
                "buffer");

            _buffer = new ReplayBufferService(ffmpeg, segmentDir);
            _buffer.SetBufferSeconds(_config.BufferSeconds);
            _buffer.StatusChanged += () => Dispatcher.BeginInvoke(OnBufferStatusChanged);
            _buffer.ClipSaved += (path, tag) => Dispatcher.BeginInvoke(() => OnClipSaved(path, tag));
            _buffer.SaveFailed += message => Dispatcher.BeginInvoke(() =>
            {
                _window?.SetStatus(message);
                _tray?.ShowBalloon("Karu", message);
            });

            _killfeed = new KillfeedMonitor();
            _killfeed.ApplyConfig(_config);
            _killfeed.HighlightTriggered += request => _ = SaveHighlightAsync(request);
            _killfeed.OcrLineObserved += (line, utc) =>
                Dispatcher.BeginInvoke(() => _window?.SetOcrHealth(line, utc));
            // Buffer = process Valo ; OCR = focus Valo (perf)

            _power = new PowerMonitorService();
            _power.PowerSourceChanged += _ =>
                Dispatcher.BeginInvoke(() => ApplyCapturePolicy());
            _power.Start();

            _gameFocus = new GameForegroundMonitor();
            _gameFocus.Changed += _ =>
                Dispatcher.BeginInvoke(() => ApplyCapturePolicy());
            _gameFocus.Start();

            // Si Valo se ferme en arrière-plan : arrêter le buffer sans attendre un alt-tab
            _capturePolicyTimer = new Timer(
                _ => Dispatcher.BeginInvoke(() => ApplyCapturePolicy()),
                null,
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(5));

            ApplyCapturePolicy();
            AppLog.Write("capture policy ready gameFocus=" + _gameFocus.IsGameFocused);

            try
            {
                var chord = HotkeyParser.TryParse(_config.Hotkey, out var parsed)
                    ? parsed
                    : HotkeyParser.Parse(AppConfig.DefaultHotkey);
                _hotkey = new GlobalHotkeyService();
                _hotkey.SetChord(chord);
                _hotkey.Triggered += () => _ = SaveClipAsync(ClipTag.Manual);
                _hotkey.Start();
                AppLog.Write("hotkey installed " + HotkeyParser.ToDisplay(_hotkey.Chord));
            }
            catch (Exception ex)
            {
                AppLog.Write("hotkey: " + ex.Message);
                _window?.SetStatus("Raccourci global indisponible : " + ex.Message);
            }

            if (_config.CheckUpdatesOnStartup)
            {
                _ = CheckUpdatesInBackgroundAsync();
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("buffer start failed: " + ex);
            _window?.SetStatus("Erreur NVENC : " + ex.Message);
        }
    }

    /// <summary>
    /// Batterie prioritaire.
    /// Buffer : ON dès que le process Valorant tourne (F9 OK en alt-tab).
    /// OCR killfeed : seulement fenêtre Valorant au premier plan.
    /// </summary>
    private void ApplyCapturePolicy()
    {
        if (_buffer is null)
        {
            return;
        }

        if (Interlocked.Exchange(ref _policyBusy, 1) == 1)
        {
            Interlocked.Exchange(ref _policyPending, 1);
            return;
        }

        var pauseBattery = _config.PauseBufferOnBattery && (_power?.IsOnBattery ?? false);
        var gameFocused = _gameFocus?.IsGameFocused ?? false;
        var gameRunning = GameProcessNames.IsValorantRunning();
        _ = Task.Run(() =>
        {
            try
            {
                if (pauseBattery)
                {
                    if (_buffer!.IsRunning || !_buffer.IsPausedForPower)
                    {
                        _buffer.PauseForPower();
                    }

                    _killfeed?.Stop();
                    Dispatcher.BeginInvoke(PushUnifiedStatus);
                    return;
                }

                if (!gameRunning)
                {
                    _killfeed?.Stop();
                    if (_buffer!.IsRunning)
                    {
                        _buffer.Stop();
                    }

                    Dispatcher.BeginInvoke(PushUnifiedStatus);
                    return;
                }

                try
                {
                    if (_buffer!.IsPausedForPower)
                    {
                        _buffer.ResumeFromPower();
                    }
                    else if (!_buffer.IsRunning)
                    {
                        _buffer.Start();
                    }

                    if (gameFocused)
                    {
                        _killfeed?.Start();
                    }
                    else
                    {
                        _killfeed?.Stop();
                    }

                    Dispatcher.BeginInvoke(PushUnifiedStatus);
                }
                catch (Exception ex)
                {
                    Dispatcher.BeginInvoke(() =>
                        _window?.SetStatus("Erreur NVENC : " + ex.Message));
                    AppLog.Write("capture start: " + ex);
                }
            }
            finally
            {
                Interlocked.Exchange(ref _policyBusy, 0);
                if (Interlocked.Exchange(ref _policyPending, 0) == 1)
                {
                    Dispatcher.BeginInvoke(() => ApplyCapturePolicy());
                }
            }
        });
    }

    private void PushUnifiedStatus()
    {
        var pauseBattery = _config.PauseBufferOnBattery && (_power?.IsOnBattery ?? false);
        var gameFocused = _gameFocus?.IsGameFocused ?? false;
        var gameRunning = GameProcessNames.IsValorantRunning();
        var text = CaptureStatusText.Build(
            pauseBattery,
            gameRunning,
            gameFocused,
            _config.HighlightsEnabled,
            !string.IsNullOrWhiteSpace(_config.PlayerName));
        _window?.SetStatus(text);
        _window?.SetPlayerNameBanner(
            _config.HighlightsEnabled && string.IsNullOrWhiteSpace(_config.PlayerName));
    }

    private void OnBufferStatusChanged()
    {
        var s = _buffer?.Status ?? "";
        if (s.Contains("Sauvegarde", StringComparison.OrdinalIgnoreCase) ||
            s.StartsWith("Erreur", StringComparison.OrdinalIgnoreCase) ||
            s.StartsWith("Capture", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("Pas encore", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("Dossier", StringComparison.OrdinalIgnoreCase))
        {
            _window?.SetStatus(s);
            return;
        }

        PushUnifiedStatus();
    }

    private void RunOnboardingIfNeeded()
    {
        if (_config.OnboardingCompleted || _window is null)
        {
            return;
        }

        try
        {
            var onboard = new OnboardingWindow(_config) { Owner = _window };
            var ok = onboard.ShowDialog() == true && onboard.Completed;
            if (ok)
            {
                _config.PlayerName = onboard.PlayerName;
                _config.Hotkey = onboard.Hotkey;
                _config.HighlightsEnabled = onboard.AutoClipsEnabled;
                if (onboard.AutoClipsEnabled)
                {
                    _config.AutoTriple = true;
                    _config.AutoQuad = true;
                    _config.AutoAce = true;
                }

                if (onboard.CalibrateRequested)
                {
                    _window.RunKillfeedCalibration();
                }
            }

            _config.OnboardingCompleted = true;
            ConfigStore.Save(ConfigStore.DefaultPath, _config);
            _window.ApplyConfig(_config);
            PushUnifiedStatus();
        }
        catch (Exception ex)
        {
            AppLog.Write("onboarding: " + ex.Message);
            _config.OnboardingCompleted = true;
            try
            {
                ConfigStore.Save(ConfigStore.DefaultPath, _config);
            }
            catch
            {
                // ignore
            }
        }
    }

    private async Task CheckUpdatesInBackgroundAsync()
    {
        var result = await UpdateChecker.CheckLatestAsync().ConfigureAwait(false);
        await Dispatcher.InvokeAsync(() =>
        {
            _window?.ShowUpdateNotice(result.Message ?? "");
            if (result.UpdateAvailable)
            {
                _tray?.ShowBalloon("Karu", result.Message ?? "Mise à jour disponible.");
            }
        });
    }

    private void OnClipSaved(string path, ClipTag tag)
    {
        try
        {
            _library.Register(path, tag);
            var videoPath = path;
            _ = Task.Run(async () =>
            {
                if (_thumbnails is not null)
                {
                    await _thumbnails.GenerateAsync(videoPath);
                }

                _ = Dispatcher.BeginInvoke(() => _window?.RefreshClips());
            });
        }
        catch (Exception ex)
        {
            AppLog.Write("metadata: " + ex.Message);
        }

        PushUnifiedStatus();
        _window?.RefreshClips();
        var title = tag switch
        {
            ClipTag.Triple => "Triple sauvé",
            ClipTag.Quad => "Quad sauvé",
            ClipTag.Ace => "Ace sauvé",
            _ => "Clip sauvé"
        };
        _tray?.ShowBalloon(title, Path.GetFileName(path) + " — ouvre Karu pour revoir");
    }

    private void OnMissingThumbnails(IReadOnlyList<string> paths)
    {
        if (_thumbnails is null || paths.Count == 0)
        {
            return;
        }

        var copy = paths.ToArray();
        _ = Task.Run(async () =>
        {
            await _thumbnails.GenerateMissingAsync(copy);
            if (Interlocked.Exchange(ref _thumbRefreshQueued, 1) == 1)
            {
                return;
            }

            await Task.Delay(400);
            Interlocked.Exchange(ref _thumbRefreshQueued, 0);
            _ = Dispatcher.BeginInvoke(() => _window?.RefreshClips());
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _hotkey?.Dispose();
            _killfeed?.Dispose();
            _capturePolicyTimer?.Dispose();
            _capturePolicyTimer = null;
            _gameFocus?.Dispose();
            _power?.Dispose();
            _buffer?.Dispose();
            _thumbnails?.Dispose();
            _tray?.Dispose();
            _mutex?.Dispose();
            _mutex = null;
        }
        catch (Exception ex)
        {
            AppLog.Write("exit: " + ex.Message);
        }

        base.OnExit(e);
    }

    private void OnSettingsChanged(AppConfig config)
    {
        _config = config;
        try
        {
            ConfigStore.Save(ConfigStore.DefaultPath, _config);
        }
        catch (Exception ex)
        {
            _window?.SetStatus(PathSafety.FriendlyIoMessage(ex));
            AppLog.Write("config save: " + ex.Message);
        }

        if (PathSafety.TryEnsureWritableDirectory(_config.SaveDirectory, out var saveDir, out var err))
        {
            _config.SaveDirectory = saveDir;
        }
        else
        {
            _window?.SetStatus(err ?? "Dossier clips inaccessible.");
        }

        _buffer?.SetBufferSeconds(_config.BufferSeconds);
        _killfeed?.ApplyConfig(_config);
        ApplyCapturePolicy();

        try
        {
            StartupRegistration.SetEnabled(_config.StartWithWindows);
        }
        catch (Exception ex)
        {
            AppLog.Write("startup reg: " + ex.Message);
        }

        if (_hotkey is not null && HotkeyParser.TryParse(_config.Hotkey, out var chord))
        {
            try
            {
                _hotkey.SetChord(chord);
            }
            catch (Exception ex)
            {
                _window?.SetStatus(ex.Message);
            }
        }
    }

    private async Task SaveClipAsync(ClipTag tag)
    {
        if (_buffer is null)
        {
            return;
        }

        if (!_buffer.IsRunning)
        {
            const string msg = "Buffer inactif — lance Valorant pour démarrer le buffer.";
            _window?.SetStatus(msg);
            _tray?.ShowBalloon("Karu", msg);
            return;
        }

        var hint = ClipFileNamer.MakeHint(tag, _config.PlayerName);
        await _buffer.SaveClipAsync(_config.SaveDirectory, tag, hint);
    }

    private async Task SaveHighlightAsync(HighlightClipRequest request)
    {
        if (_buffer is null || !_buffer.IsRunning)
        {
            return;
        }

        AppLog.Write(
            $"highlight {request.Tag} window={request.WindowStartUtc:HH:mm:ss}..{request.WindowEndUtc:HH:mm:ss}Z");
        var hint = ClipFileNamer.MakeHint(request.Tag, _config.PlayerName);
        await _buffer.SaveClipAsync(_config.SaveDirectory, request, hint);
    }

    private void ShowSettings()
    {
        if (_window is null)
        {
            return;
        }

        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
        _window.RefreshClips();
    }

    private void ExitApp()
    {
        _window?.AllowClose();
        Shutdown();
    }
}
