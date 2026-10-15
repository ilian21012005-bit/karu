using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Karu.Core;

namespace Karu.App;

/// <summary>Premier démarrage en trois étapes courtes.</summary>
public sealed class OnboardingWindow : Window
{
    private readonly TextBox _playerBox;
    private readonly TextBox _hotkeyBox;
    private readonly CheckBox _autoClipsCheck;
    private readonly TextBlock _stepTitle;
    private readonly TextBlock _stepNumber;
    private readonly StackPanel[] _steps;
    private readonly Button _backButton;
    private readonly Button _nextButton;
    private int _step;

    public string PlayerName { get; private set; } = "";
    public string Hotkey { get; private set; } = AppConfig.DefaultHotkey;
    public bool AutoClipsEnabled { get; private set; } = true;
    public bool CalibrateRequested { get; private set; }
    public bool Completed { get; private set; }

    public OnboardingWindow(AppConfig seed)
    {
        Title = "Bienvenue sur Karu";
        Width = 560;
        Height = 500;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        Background = (Brush)Application.Current.Resources["BgBrush"];
        Foreground = (Brush)Application.Current.Resources["TextBrush"];
        FontFamily = new FontFamily("Bahnschrift, Segoe UI");

        var muted = (Brush)Application.Current.Resources["MutedBrush"];
        var accent = (Brush)Application.Current.Resources["AccentBrush"];
        var root = new Grid { Margin = new Thickness(48, 34, 48, 30) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };
        header.Children.Add(new TextBlock { Text = "KARU", FontSize = 16, FontWeight = FontWeights.Bold, Foreground = Foreground });
        _stepNumber = new TextBlock { Text = "01 / 03", FontSize = 11, Foreground = muted, Margin = new Thickness(0, 18, 0, 4) };
        header.Children.Add(_stepNumber);
        _stepTitle = new TextBlock { FontSize = 30, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap };
        header.Children.Add(_stepTitle);
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        _playerBox = new TextBox { Text = seed.PlayerName ?? "", Padding = new Thickness(10, 8, 10, 8), FontSize = 15 };
        _hotkeyBox = new TextBox { Text = string.IsNullOrWhiteSpace(seed.Hotkey) ? AppConfig.DefaultHotkey : seed.Hotkey, Padding = new Thickness(10, 8, 10, 8), FontSize = 15, IsReadOnly = true };
        _hotkeyBox.PreviewKeyDown += (_, e) =>
        {
            e.Handled = true;
            var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
            _hotkeyBox.Text = key.ToString();
        };
        _autoClipsCheck = new CheckBox
        {
            Content = "Créer automatiquement les clips 3K, 4K et Ace",
            IsChecked = seed.HighlightsEnabled && (seed.AutoTriple || seed.AutoQuad || seed.AutoAce),
            FontSize = 15
        };

        _steps =
        [
            Step("Pseudo Valorant exact", "Entre le pseudo affiché en jeu pour reconnaître les éliminations.", _playerBox),
            Step("Clip manuel", "Choisis le raccourci pour sauvegarder les dernières minutes.", _hotkeyBox),
            Step("Clips automatiques", "La détection killfeed fonctionne quand Valorant est au premier plan.", _autoClipsCheck)
        ];
        var body = new Grid();
        foreach (var panel in _steps) body.Children.Add(panel);
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        _backButton = new Button { Content = "Retour", MinWidth = 90, Margin = new Thickness(0, 0, 8, 0) };
        _backButton.Click += (_, _) => ShowStep(_step - 1);
        _nextButton = new Button { Content = "Continuer", MinWidth = 120, Height = 40, Background = accent, Foreground = Brushes.White, BorderThickness = new Thickness(0), FontWeight = FontWeights.SemiBold };
        _nextButton.Click += (_, _) =>
        {
            if (_step < 2) ShowStep(_step + 1);
            else Finish(calibrate: false);
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0) };
        buttons.Children.Add(_backButton);
        buttons.Children.Add(_nextButton);
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);
        Content = root;
        ShowStep(0);
    }

    private static StackPanel Step(string label, string help, UIElement control)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = label, FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 7) });
        panel.Children.Add(control);
        panel.Children.Add(new TextBlock
        {
            Text = help,
            Foreground = (Brush)Application.Current.Resources["MutedBrush"],
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0)
        });
        return panel;
    }

    private void ShowStep(int step)
    {
        _step = Math.Clamp(step, 0, 2);
        for (var i = 0; i < _steps.Length; i++) _steps[i].Visibility = i == _step ? Visibility.Visible : Visibility.Collapsed;
        _stepNumber.Text = $"{_step + 1:00} / 03";
        _stepTitle.Text = _step switch { 0 => "Pour commencer", 1 => "Choisis ta touche", _ => "Tes clips, automatiquement" };
        _backButton.Visibility = _step == 0 ? Visibility.Collapsed : Visibility.Visible;
        _nextButton.Content = _step == 2 ? "Terminer" : "Continuer";
    }

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
