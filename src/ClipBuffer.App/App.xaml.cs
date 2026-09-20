using System.IO;
using System.Windows;
using ClipBuffer.App.Services;
using ClipBuffer.Core;

namespace ClipBuffer.App;

public partial class App : System.Windows.Application
{
    private Mutex? _mutex;
    private AppConfig _config = AppConfig.CreateDefault();
    private MainWindow? _window;
    private TrayService? _tray;
    private GlobalHotkeyService? _hotkey;
    private ReplayBufferService? _buffer;

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
            MessageBox.Show(ex.Message, "Clip Buffer", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    private void StartCore()
    {
        _mutex = new Mutex(true, @"Local\ClipBuffer.SingleInstance", out var created);
        if (!created)
        {
            MessageBox.Show("Clip Buffer est déjà lancé.", "Clip Buffer", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        try
        {
            System.Diagnostics.Process.GetCurrentProcess().PriorityClass = System.Diagnostics.ProcessPriorityClass.BelowNormal;
        }
        catch
        {
            // ignore if the OS refuses
        }

        _config = ConfigStore.Load(ConfigStore.DefaultPath);
        ConfigStore.Save(ConfigStore.DefaultPath, _config);
        Directory.CreateDirectory(_config.SaveDirectory);

        _window = new MainWindow(_config);
        _window.SettingsChanged += OnSettingsChanged;

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

        var probe = NvencProbe.Run(ffmpeg);
        AppLog.Write("nvenc=" + probe.Ok + " " + probe.Message);
        if (!probe.Ok)
        {
            _window.SetStatus(probe.Message);
            _window.Show();
            return;
        }

        var segmentDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ClipBuffer",
            "buffer");

        _buffer = new ReplayBufferService(ffmpeg, segmentDir);
        _buffer.SetBufferSeconds(_config.BufferSeconds);
        _buffer.StatusChanged += () => Dispatcher.BeginInvoke(() => _window?.SetStatus(_buffer!.Status));
        _buffer.ClipSaved += path => Dispatcher.BeginInvoke(() =>
        {
            _window?.SetStatus("Buffer actif");
            _tray?.ShowBalloon("Clip sauvé", Path.GetFileName(path));
        });
        _buffer.SaveFailed += message => Dispatcher.BeginInvoke(() =>
        {
            _window?.SetStatus(message);
            _tray?.ShowBalloon("Clip Buffer", message);
        });

        try
        {
            _buffer.Start();
            _window.SetStatus(_buffer.Status);
            AppLog.Write("buffer started status=" + _buffer.Status);
        }
        catch (Exception ex)
        {
            AppLog.Write("buffer start failed: " + ex);
            _window.SetStatus("Erreur NVENC : " + ex.Message);
            _window.Show();
            return;
        }

        _hotkey = new GlobalHotkeyService
        {
            Chord = HotkeyParser.TryParse(_config.Hotkey, out var chord)
                ? chord
                : HotkeyParser.Parse(AppConfig.DefaultHotkey)
        };
        _hotkey.Triggered += () => _ = SaveClipAsync();
        _hotkey.Start();
        AppLog.Write("hotkey installed " + HotkeyParser.ToDisplay(_hotkey.Chord));

        _window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkey?.Dispose();
        _buffer?.Dispose();
        _tray?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }

    private void OnSettingsChanged(AppConfig config)
    {
        _config = config;
        ConfigStore.Save(ConfigStore.DefaultPath, _config);
        Directory.CreateDirectory(_config.SaveDirectory);
        _buffer?.SetBufferSeconds(_config.BufferSeconds);
        if (_hotkey is not null && HotkeyParser.TryParse(_config.Hotkey, out var chord))
        {
            _hotkey.Chord = chord;
        }
    }

    private async Task SaveClipAsync()
    {
        if (_buffer is null)
        {
            return;
        }

        await _buffer.SaveClipAsync(_config.SaveDirectory);
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
    }

    private void ExitApp()
    {
        _window?.AllowClose();
        Shutdown();
    }
}

internal static class AppLog
{
    private static readonly string PathName = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ClipBuffer",
        "log.txt");

    public static void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PathName)!);
            File.AppendAllText(PathName, DateTime.Now.ToString("s") + " " + message + Environment.NewLine);
        }
        catch
        {
            // ignore
        }
    }
}
