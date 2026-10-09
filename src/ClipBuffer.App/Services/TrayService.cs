using System.Drawing;
using System.IO;
using System.Windows;
using Forms = System.Windows.Forms;

namespace ClipBuffer.App.Services;

public sealed class TrayService : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private bool _disposed;

    public TrayService()
    {
        _icon = new Forms.NotifyIcon
        {
            Text = "Karu",
            Icon = LoadAppIcon(),
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };
        _icon.DoubleClick += (_, _) => ShowSettingsRequested?.Invoke();
    }

    public event Action? ShowSettingsRequested;
    public event Action? ExitRequested;

    public void ShowBalloon(string title, string message)
    {
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = message;
        _icon.ShowBalloonTip(3000);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _icon.Visible = false;
        _icon.Dispose();
    }

    private static Icon LoadAppIcon()
    {
        try
        {
            var icoPath = Path.Combine(AppContext.BaseDirectory, "Assets", "karu.ico");
            if (File.Exists(icoPath))
            {
                return new Icon(icoPath);
            }

            var exe = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(exe) && File.Exists(exe))
            {
                return Icon.ExtractAssociatedIcon(exe) ?? SystemIcons.Application;
            }
        }
        catch
        {
            // fall through
        }

        return SystemIcons.Application;
    }

    private Forms.ContextMenuStrip BuildMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        var settings = new Forms.ToolStripMenuItem("Réglages");
        settings.Click += (_, _) => ShowSettingsRequested?.Invoke();
        var exit = new Forms.ToolStripMenuItem("Quitter");
        exit.Click += (_, _) => ExitRequested?.Invoke();
        menu.Items.Add(settings);
        menu.Items.Add(exit);
        return menu;
    }
}
