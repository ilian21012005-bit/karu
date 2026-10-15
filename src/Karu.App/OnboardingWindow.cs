using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Karu.Core;

namespace Karu.App;

/// <summary>First-run : pseudo, hotkey, clips auto, calibration optionnelle.</summary>
public sealed class OnboardingWindow : Window
{
    private readonly TextBox _playerBox;
    private readonly TextBox _hotkeyBox;
    private readonly CheckBox _autoClipsCheck;

    public string PlayerName { get; private set; } = "";
    public string Hotkey { get; private set; } = AppConfig.DefaultHotkey;
    public bool AutoClipsEnabled { get; private set; } = true;
    public bool CalibrateRequested { get; private set; }
    public bool Completed { get; private set; }

    public OnboardingWindow(AppConfig seed)
    {
        Title = "Bienvenue sur Karu";
        Width = 480;
        Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        Background = (Brush)Application.Current.Resources["BgBrush"];
        Foreground = (Brush)Application.Current.Resources["TextBrush"];
        FontFamily = new FontFamily("Segoe UI");

        var accent = (Brush)Application.Current.Resources["AccentBrush"];
        var muted = (Brush)Application.Current.Resources["MutedBrush"];
        var panel = (Brush)Application.Current.Resources["PanelBrush"];

        var root = new Border
        {
            Background = panel,
            CornerRadius = new CornerRadius(12),
            Margin = new Thickness(20),
            Padding = new Thickness(24)
        };

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = "Karu",
            FontSize = 28,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        });
        stack.Children.Add(new TextBlock
        {
            Text = "2 minutes pour que ça marche tout seul. Local, sans compte.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = muted,
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 18)
        });

        stack.Children.Add(Label("Pseudo Valorant (exact)"));
        _playerBox = new TextBox
        {
            Text = seed.PlayerName ?? "",
            Margin = new Thickness(0, 0, 0, 14),
            Padding = new Thickness(8, 6, 8, 6)
        };
        stack.Children.Add(_playerBox);

        stack.Children.Add(Label("Raccourci clip manuel"));
        _hotkeyBox = new TextBox
        {
            Text = string.IsNullOrWhiteSpace(seed.Hotkey) ? AppConfig.DefaultHotkey : seed.Hotkey,
            Margin = new Thickness(0, 0, 0, 14),
            Padding = new Thickness(8, 6, 8, 6)
        };
        stack.Children.Add(_hotkeyBox);

        _autoClipsCheck = new CheckBox
        {
            Content = "Clips auto (3K / 4K / Ace)",
            IsChecked = seed.HighlightsEnabled && (seed.AutoTriple || seed.AutoQuad || seed.AutoAce),
            Margin = new Thickness(0, 0, 0, 16)
        };
        stack.Children.Add(_autoClipsCheck);

        stack.Children.Add(new TextBlock
        {
            Text = "Lance Valorant → le statut devient « Buffer actif ». La détection OCR ne tourne que quand Valo est au premier plan (perf).",
            TextWrapping = TextWrapping.Wrap,
            Foreground = muted,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 18)
        });

        var buttons = new StackPanel
        {
            Orientation = Orientation.Vertical
        };

        var startBtn = new Button
        {
            Content = "C’est parti",
            Height = 36,
            Margin = new Thickness(0, 0, 0, 8),
            Background = accent,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            FontWeight = FontWeights.SemiBold
        };
        startBtn.Click += (_, _) => Finish(calibrate: false);

        var calibBtn = new Button
        {
            Content = "Calibrer killfeed (optionnel)",
            Height = 34,
            Margin = new Thickness(0, 0, 0, 8)
        };
        calibBtn.Click += (_, _) => Finish(calibrate: true);

        var skipBtn = new Button
        {
            Content = "Passer",
            Height = 32
        };
        skipBtn.Click += (_, _) => Finish(calibrate: false);

        buttons.Children.Add(startBtn);
        buttons.Children.Add(calibBtn);
        buttons.Children.Add(skipBtn);
        stack.Children.Add(buttons);

        root.Child = stack;
        Content = root;
    }

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 0, 0, 6)
    };

    private void Finish(bool calibrate)
    {
        PlayerName = (_playerBox.Text ?? "").Trim();
        Hotkey = string.IsNullOrWhiteSpace(_hotkeyBox.Text) ? AppConfig.DefaultHotkey : _hotkeyBox.Text.Trim();
        AutoClipsEnabled = _autoClipsCheck.IsChecked == true;
        CalibrateRequested = calibrate;
        Completed = true;
        DialogResult = true;
        Close();
    }
}
