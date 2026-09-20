using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using ClipBuffer.Core;

namespace ClipBuffer.App;

public partial class MainWindow : Window
{
    private bool _allowClose;
    private bool _suppressEvents;

    public MainWindow(AppConfig config)
    {
        InitializeComponent();
        ApplyConfig(config);
    }

    public event Action<AppConfig>? SettingsChanged;

    public void SetStatus(string status) => StatusText.Text = status;

    public void AllowClose() => _allowClose = true;

    public void ApplyConfig(AppConfig config)
    {
        _suppressEvents = true;
        DurationSlider.Value = config.BufferSeconds;
        DurationValueText.Text = FormatDuration(config.BufferSeconds);
        HotkeyBox.Text = config.Hotkey;
        FolderBox.Text = config.SaveDirectory;
        _suppressEvents = false;
    }

    private void DurationSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded || _suppressEvents)
        {
            return;
        }

        DurationValueText.Text = FormatDuration((int)DurationSlider.Value);
        RaiseSettingsChanged();
    }

    private void HotkeyBox_OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.Escape)
        {
            return;
        }

        var keyName = NormalizeWpfKey(key);
        if (keyName is null || !HotkeyParser.TryParse(keyName, out _))
        {
            return;
        }

        var chord = new HotkeyChord(
            (Keyboard.Modifiers & ModifierKeys.Control) != 0,
            (Keyboard.Modifiers & ModifierKeys.Alt) != 0,
            (Keyboard.Modifiers & ModifierKeys.Shift) != 0,
            keyName);

        HotkeyBox.Text = HotkeyParser.ToDisplay(chord);
        RaiseSettingsChanged();
    }

    private void BrowseButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Dossier des clips",
            InitialDirectory = Directory.Exists(FolderBox.Text)
                ? FolderBox.Text
                : AppConfig.DefaultSaveDirectory()
        };

        if (dialog.ShowDialog(this) != true || string.IsNullOrWhiteSpace(dialog.FolderName))
        {
            return;
        }

        FolderBox.Text = dialog.FolderName;
        RaiseSettingsChanged();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        Hide();
    }

    private void RaiseSettingsChanged()
    {
        var config = new AppConfig
        {
            BufferSeconds = (int)DurationSlider.Value,
            Hotkey = HotkeyBox.Text,
            SaveDirectory = FolderBox.Text
        };
        config.Clamp();
        SettingsChanged?.Invoke(config);
    }

    private static string FormatDuration(int seconds)
    {
        if (seconds < 60)
        {
            return $"{seconds} s";
        }

        var minutes = seconds / 60;
        var rest = seconds % 60;
        return rest == 0 ? $"{minutes} min" : $"{minutes} min {rest} s";
    }

    private static string? NormalizeWpfKey(Key key)
    {
        var name = key.ToString();
        if (name.Length == 2 && name[0] == 'D' && char.IsDigit(name[1]))
        {
            return name[1].ToString();
        }

        return name;
    }
}
