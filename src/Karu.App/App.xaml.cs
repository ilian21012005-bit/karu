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
    private readonly ClipLibrary _library = new();
    private int _thumbRefreshQueued;
    private int _powerBusy;
    private int _powerPending;

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

        var ffmpeg = FfmpegLocator.Find();
        AppLog.Write("ffmpeg=" + (ffmpeg ?? "(null)"));
        if (ffmpeg is null)
        {
            _window.SetStatus("FFmpeg introuvable. Place ffmpeg.exe dans tools/ffmpeg.");
            _window.Show();
            return;
        }

        _window.SetStatus("Détection NVENC…");
        _window.Show();

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
            _buffer.StatusChanged += () => Dispatcher.BeginInvoke(() => _window?.SetStatus(_buffer!.Status));
            _buffer.ClipSaved += (path, tag) => Dispatcher.BeginInvoke(() => OnClipSaved(path, tag));
            _buffer.SaveFailed += message => Dispatcher.BeginInvoke(() =>
            {
                _window?.SetStatus(message);
                _tray?.ShowBalloon("Karu", message);
            });

            _killfeed = new KillfeedMonitor();
            _killfeed.ApplyConfig(_config);
            _killfeed.HighlightTriggered += tag => _ = SaveClipAsync(tag);
            _killfeed.Start();

            _power = new PowerMonitorService();
            _power.PowerSourceChanged += _ =>
                Dispatcher.BeginInvoke(() => ApplyPowerPolicy());
            _power.Start();

            ApplyPowerPolicy(startIfNeeded: true);
            _window?.SetStatus(_buffer.Status);
            AppLog.Write("buffer started status=" + _buffer.Status);

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

    private void ApplyPowerPolicy(bool startIfNeeded = false)
    {
        if (_buffer is null)
        {
            return;
        }

        if (Interlocked.Exchange(ref _powerBusy, 1) == 1)
        {
            Interlocked.Exchange(ref _powerPending, 1);
            return;
        }

        var pause = _config.PauseBufferOnBattery && (_power?.IsOnBattery ?? false);
        var forceStart = startIfNeeded;
        _ = Task.Run(() =>
        {
            try
            {
                if (pause)
                {
                    if (_buffer!.IsRunning || !_buffer.IsPausedForPower)
                    {
                        _buffer.PauseForPower();
                    }

                    _killfeed?.Stop();
                    Dispatcher.BeginInvoke(() => _window?.SetStatus(_buffer.Status));
                    return;
                }

                if (_buffer!.IsPausedForPower || (forceStart && !_buffer.IsRunning))
                {
                    try
                    {
                        _buffer.ResumeFromPower();
                        _killfeed?.Start();
                        Dispatcher.BeginInvoke(() => _window?.SetStatus(_buffer.Status));
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.BeginInvoke(() =>
                            _window?.SetStatus("Erreur NVENC : " + ex.Message));
                        AppLog.Write("resume: " + ex);
                    }
                }
            }
            finally
            {
                Interlocked.Exchange(ref _powerBusy, 0);
                if (Interlocked.Exchange(ref _powerPending, 0) == 1)
                {
                    Dispatcher.BeginInvoke(() => ApplyPowerPolicy());
                }
            }
        });
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

        _window?.SetStatus("Buffer actif");
        _window?.RefreshClips();
        _tray?.ShowBalloon("Karu", Path.GetFileName(path));
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
        ApplyPowerPolicy();

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

        await _buffer.SaveClipAsync(_config.SaveDirectory, tag);
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
