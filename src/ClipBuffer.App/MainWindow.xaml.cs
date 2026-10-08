using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using ClipBuffer.Core;

namespace ClipBuffer.App;

public sealed class ClipListItem : INotifyPropertyChanged
{
    private BitmapImage? _thumbnailImage;

    public required ClipMetadata Metadata { get; init; }
    public string DisplayName => Metadata.DisplayName;
    public string TagLabel => Metadata.Favorite
        ? $"{TagToLabel(Metadata.Tag)} · favori"
        : TagToLabel(Metadata.Tag);

    public BitmapImage? ThumbnailImage
    {
        get => _thumbnailImage;
        set
        {
            _thumbnailImage = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ThumbnailImage)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public static string TagToLabel(ClipTag tag) => tag switch
    {
        ClipTag.Triple => "3 kills",
        ClipTag.Quad => "4 kills",
        ClipTag.Ace => "Ace",
        _ => "Manuel"
    };
}

public partial class MainWindow : Window
{
    private readonly ClipLibrary _library = new();
    private readonly ObservableCollection<ClipListItem> _items = new();
    private bool _allowClose;
    private bool _suppressEvents;
    private string _directory = AppConfig.DefaultSaveDirectory();
    private double _killX = 0.72;
    private double _killY = 0.02;
    private double _killW = 0.27;
    private double _killH = 0.22;

    public MainWindow(AppConfig config)
    {
        InitializeComponent();
        ClipList.ItemsSource = _items;
        ApplyConfig(config);
        RefreshClips();
    }

    public event Action<AppConfig>? SettingsChanged;

    public void SetStatus(string status) => StatusText.Text = status;

    public void AllowClose() => _allowClose = true;

    public void RefreshClips()
    {
        var filter = SelectedFilter();
        var selectedPath = (ClipList.SelectedItem as ClipListItem)?.Metadata.VideoPath;
        _items.Clear();
        foreach (var meta in _library.List(_directory, filter))
        {
            var item = new ClipListItem { Metadata = meta };
            if (!string.IsNullOrWhiteSpace(meta.ThumbnailPath) && File.Exists(meta.ThumbnailPath))
            {
                try
                {
                    var img = new BitmapImage();
                    img.BeginInit();
                    img.CacheOption = BitmapCacheOption.OnLoad;
                    img.UriSource = new Uri(meta.ThumbnailPath);
                    img.EndInit();
                    img.Freeze();
                    item.ThumbnailImage = img;
                }
                catch
                {
                    // ignore bad thumbs
                }
            }

            _items.Add(item);
        }

        if (selectedPath is not null)
        {
            ClipList.SelectedItem = _items.FirstOrDefault(i => i.Metadata.VideoPath == selectedPath);
        }
    }

    public void ApplyConfig(AppConfig config)
    {
        _suppressEvents = true;
        _directory = config.SaveDirectory;
        _killX = config.KillfeedX;
        _killY = config.KillfeedY;
        _killW = config.KillfeedW;
        _killH = config.KillfeedH;
        DurationSlider.Value = config.BufferSeconds;
        DurationValueText.Text = FormatDuration(config.BufferSeconds);
        HotkeyBox.Text = config.Hotkey;
        FolderBox.Text = config.SaveDirectory;
        HighlightsEnabledCheck.IsChecked = config.HighlightsEnabled;
        AutoTripleCheck.IsChecked = config.AutoTriple;
        AutoQuadCheck.IsChecked = config.AutoQuad;
        AutoAceCheck.IsChecked = config.AutoAce;
        PlayerNameBox.Text = config.PlayerName;
        CooldownSlider.Value = config.HighlightCooldownSeconds;
        CooldownValueText.Text = $"{config.HighlightCooldownSeconds} s";
        CalibrationInfo.Text =
            $"Zone killfeed : {_killX:0%} , {_killY:0%} · {_killW:0%} × {_killH:0%}";
        _suppressEvents = false;
    }

    private void DurationSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded || _suppressEvents) return;
        DurationValueText.Text = FormatDuration((int)DurationSlider.Value);
        RaiseSettingsChanged();
    }

    private void CooldownSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded || _suppressEvents) return;
        CooldownValueText.Text = $"{(int)CooldownSlider.Value} s";
        RaiseSettingsChanged();
    }

    private void SettingsCheck_OnChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _suppressEvents) return;
        RaiseSettingsChanged();
    }

    private void PlayerNameBox_OnLostFocus(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _suppressEvents) return;
        RaiseSettingsChanged();
    }

    private void HotkeyBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
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
        _directory = dialog.FolderName;
        RaiseSettingsChanged();
        RefreshClips();
    }

    private void Calibrate_OnClick(object sender, RoutedEventArgs e)
    {
        var win = new CalibrationWindow();
        win.ShowDialog();
        if (!win.Confirmed)
        {
            return;
        }

        _killX = win.NormX;
        _killY = win.NormY;
        _killW = win.NormW;
        _killH = win.NormH;
        CalibrationInfo.Text =
            $"Zone killfeed : {_killX:0%} , {_killY:0%} · {_killW:0%} × {_killH:0%}";
        RaiseSettingsChanged();
    }

    private void FilterBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        RefreshClips();
    }

    private void ClipList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ClipList.SelectedItem is not ClipListItem item)
        {
            return;
        }

        try
        {
            _suppressEvents = true;
            Player.Stop();
            Player.Source = new Uri(item.Metadata.VideoPath);
            Player.Play();
            TagBox.SelectedIndex = item.Metadata.Tag switch
            {
                ClipTag.Triple => 1,
                ClipTag.Quad => 2,
                ClipTag.Ace => 3,
                _ => 0
            };
            _suppressEvents = false;
        }
        catch
        {
            _suppressEvents = false;
            SetStatus("Impossible de lire ce clip.");
        }
    }

    private void Favorite_OnClick(object sender, RoutedEventArgs e)
    {
        if (ClipList.SelectedItem is not ClipListItem item) return;
        _library.SetFavorite(item.Metadata.VideoPath, !item.Metadata.Favorite);
        RefreshClips();
    }

    private void Delete_OnClick(object sender, RoutedEventArgs e)
    {
        if (ClipList.SelectedItem is not ClipListItem item) return;
        var ask = MessageBox.Show(
            $"Supprimer {item.DisplayName} ?",
            "Clip Buffer",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (ask != MessageBoxResult.Yes) return;

        Player.Stop();
        Player.Source = null;
        _library.Delete(item.Metadata.VideoPath);
        RefreshClips();
    }

    private void TagBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _suppressEvents) return;
        if (ClipList.SelectedItem is not ClipListItem item) return;
        var tag = TagBox.SelectedIndex switch
        {
            1 => ClipTag.Triple,
            2 => ClipTag.Quad,
            3 => ClipTag.Ace,
            _ => ClipTag.Manual
        };
        if (item.Metadata.Tag == tag) return;
        _library.SetTag(item.Metadata.VideoPath, tag);
        RefreshClips();
    }

    private void OpenFolder_OnClick(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_directory);
        Process.Start(new ProcessStartInfo
        {
            FileName = _directory,
            UseShellExecute = true
        });
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        try { Player.Stop(); } catch { /* ignore */ }
        Hide();
    }

    private void RaiseSettingsChanged()
    {
        var config = BuildConfigFromUi();
        config.Clamp();
        _directory = config.SaveDirectory;
        SettingsChanged?.Invoke(config);
    }

    private AppConfig BuildConfigFromUi() => new()
    {
        BufferSeconds = (int)DurationSlider.Value,
        Hotkey = HotkeyBox.Text,
        SaveDirectory = FolderBox.Text,
        HighlightsEnabled = HighlightsEnabledCheck.IsChecked == true,
        AutoTriple = AutoTripleCheck.IsChecked == true,
        AutoQuad = AutoQuadCheck.IsChecked == true,
        AutoAce = AutoAceCheck.IsChecked == true,
        PlayerName = PlayerNameBox.Text,
        HighlightCooldownSeconds = (int)CooldownSlider.Value,
        KillfeedX = _killX,
        KillfeedY = _killY,
        KillfeedW = _killW,
        KillfeedH = _killH
    };

    private ClipFilter SelectedFilter() => FilterBox.SelectedIndex switch
    {
        1 => ClipFilter.Manual,
        2 => ClipFilter.Triple,
        3 => ClipFilter.Quad,
        4 => ClipFilter.Ace,
        5 => ClipFilter.Favorites,
        _ => ClipFilter.All
    };

    private static string FormatDuration(int seconds)
    {
        if (seconds < 60) return $"{seconds} s";
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
