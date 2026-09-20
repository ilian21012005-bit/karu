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
            Text = "Clip Buffer",
            Icon = System.Drawing.SystemIcons.Application,
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
