using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ClipBuffer.App.Services;
using ClipBuffer.Core;

namespace ClipBuffer.App;

public sealed class ClipListItem : INotifyPropertyChanged
{
    private BitmapImage? _thumbnailImage;

    public required ClipMetadata Metadata { get; init; }
    public string DisplayName => Metadata.DisplayName;
    public string TagBadge => TagToLabel(Metadata.Tag);
    public string Subtitle => Metadata.CreatedAt.ToString("dd MMM · HH:mm");
    public string FavoriteGlyph => Metadata.Favorite ? "\u2605" : "\u2606";
    public string FavoriteToolTip => Metadata.Favorite ? "Retirer des favoris" : "Ajouter aux favoris";

    public Brush FavoriteForeground => Metadata.Favorite
        ? (Brush)Application.Current.Resources["StarBrush"]
        : (Brush)Application.Current.Resources["MutedBrush"];

    public Brush TagBadgeForeground => Metadata.Tag switch
    {
        ClipTag.Ace => (Brush)Application.Current.Resources["StarBrush"],
        ClipTag.Triple or ClipTag.Quad => (Brush)Application.Current.Resources["AccentBrush"],
        _ => (Brush)Application.Current.Resources["MutedBrush"]
    };

    public bool HasThumbnail => _thumbnailImage is not null;

    public BitmapImage? ThumbnailImage
    {
        get => _thumbnailImage;
        set
        {
            _thumbnailImage = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ThumbnailImage)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasThumbnail)));
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

public sealed class FolderListItem
{
    public required ClipFolder Folder { get; init; }
    public string Name => Folder.Name;
    public FontWeight NameWeight => Folder.IsRoot ? FontWeights.SemiBold : FontWeights.Normal;
}

public partial class MainWindow : Window
{
    private readonly ClipLibrary _library = new();
    private readonly ObservableCollection<ClipListItem> _items = new();
    private readonly ObservableCollection<FolderListItem> _folders = new();
    private readonly DispatcherTimer _playTimer;
    private bool _allowClose;
    private bool _suppressEvents;
    private bool _isDraggingClip;
    private bool _isSeeking;
    private bool _isPlaying;
    private FolderListItem? _dropHighlight;
    private long _lastDragOverTicks;
    private const int MaxThumbCacheEntries = 64;
    private static readonly Dictionary<string, BitmapImage> ThumbCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly LinkedList<string> ThumbCacheOrder = new();
    private bool _resumeAfterSeek;
    private double _userVolume = 0.8;
    private string _directory = AppConfig.DefaultSaveDirectory();
    private string _currentFolderPath = AppConfig.DefaultSaveDirectory();
    private Point _dragStart;
    private double _killX = 0.72;
    private double _killY = 0.02;
    private double _killW = 0.27;
    private double _killH = 0.22;

    public MainWindow(AppConfig config)
    {
        InitializeComponent();
        ClipList.ItemsSource = _items;
        FolderList.ItemsSource = _folders;
        Player.ScrubbingEnabled = true;
        Player.LoadedBehavior = MediaState.Manual;
        Player.UnloadedBehavior = MediaState.Stop;
        _playTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _playTimer.Tick += (_, _) => UpdatePlaybackUi();
        ApplyConfig(config);
        RefreshFolders();
        RefreshClips();
        UpdatePlayPauseLabel();
    }

    public event Action<AppConfig>? SettingsChanged;
    public event Action<IReadOnlyList<string>>? MissingThumbnailsRequested;

    public void SetStatus(string status) => StatusText.Text = status;

    public void AllowClose() => _allowClose = true;

    public void RefreshFolders()
    {
        var selected = (FolderList.SelectedItem as FolderListItem)?.Folder.FullPath;
        _folders.Clear();
        foreach (var folder in _library.ListFolders(_directory))
        {
            _folders.Add(new FolderListItem { Folder = folder });
        }

        var match = _folders.FirstOrDefault(f =>
            string.Equals(f.Folder.FullPath, selected, StringComparison.OrdinalIgnoreCase));
        FolderList.SelectedItem = match ?? _folders.FirstOrDefault();
        _currentFolderPath = (FolderList.SelectedItem as FolderListItem)?.Folder.FullPath ?? _directory;
    }

    public void RefreshClips()
    {
        var filter = SelectedFilter();
        var selectedPath = (ClipList.SelectedItem as ClipListItem)?.Metadata.VideoPath;
        var folder = (FolderList.SelectedItem as FolderListItem)?.Folder;
        var listPath = folder?.FullPath ?? _directory;
        var recursive = folder?.IsRoot == true;
        _currentFolderPath = listPath;

        _items.Clear();
        var missingThumbs = new List<string>();
        foreach (var meta in _library.List(listPath, filter, recursive))
        {
            var item = new ClipListItem { Metadata = meta };
            var thumbPath = ClipArtifactPaths.ResolveExistingThumbnail(meta.VideoPath)
                            ?? meta.ThumbnailPath;

            if (!string.IsNullOrWhiteSpace(thumbPath) && File.Exists(thumbPath))
            {
                item.ThumbnailImage = GetCachedThumb(thumbPath);
            }
            else
            {
                missingThumbs.Add(meta.VideoPath);
            }

            _items.Add(item);
        }

        if (selectedPath is not null)
        {
            ClipList.SelectedItem = _items.FirstOrDefault(i => i.Metadata.VideoPath == selectedPath);
        }

        FolderHintText.Text = recursive
            ? "Tous les clips · glisse vers un dossier"
            : $"« {folder?.Name} » · glisse pour déplacer";

        if (missingThumbs.Count > 0)
        {
            MissingThumbnailsRequested?.Invoke(missingThumbs);
        }
    }

    public void ApplyConfig(AppConfig config)
    {
        _suppressEvents = true;
        _directory = config.SaveDirectory;
        _currentFolderPath = config.SaveDirectory;
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
        ThemeBox.SelectedIndex = ThemeService.Normalize(config.Theme) == ThemeService.Aventurine ? 1 : 0;
        StartWithWindowsCheck.IsChecked = config.StartWithWindows;
        PauseOnBatteryCheck.IsChecked = config.PauseBufferOnBattery;
        CheckUpdatesCheck.IsChecked = config.CheckUpdatesOnStartup;
        ThemeService.Apply(config.Theme);
        Background = (Brush)Application.Current.Resources["BgBrush"];
        Foreground = (Brush)Application.Current.Resources["TextBrush"];
        _suppressEvents = false;
    }

    private void ThemeBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _suppressEvents) return;
        var theme = ThemeBox.SelectedIndex == 1 ? ThemeService.Aventurine : ThemeService.Carbon;
        ThemeService.Apply(theme);
        Background = (Brush)Application.Current.Resources["BgBrush"];
        Foreground = (Brush)Application.Current.Resources["TextBrush"];
        RefreshClips();
        RaiseSettingsChanged();
    }

    private async void CheckUpdates_OnClick(object sender, RoutedEventArgs e)
    {
        UpdateInfoText.Text = "Vérification…";
        var result = await UpdateChecker.CheckLatestAsync();
        UpdateInfoText.Text = result.Message ?? "";
        if (result.UpdateAvailable && !string.IsNullOrWhiteSpace(result.ReleaseUrl))
        {
            var ask = MessageBox.Show(
                result.Message + "\n\nOuvrir la page de téléchargement ?",
                "Karu",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);
            if (ask == MessageBoxResult.Yes)
            {
                Process.Start(new ProcessStartInfo(result.ReleaseUrl) { UseShellExecute = true });
            }
        }
    }

    public void ShowUpdateNotice(string message) => UpdateInfoText.Text = message;

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
        if (keyName is null)
        {
            return;
        }

        var chord = new HotkeyChord(
            (Keyboard.Modifiers & ModifierKeys.Control) != 0,
            (Keyboard.Modifiers & ModifierKeys.Alt) != 0,
            (Keyboard.Modifiers & ModifierKeys.Shift) != 0,
            keyName);

        if (!HotkeyParser.IsSupportedByRegisterHotKey(chord))
        {
            return;
        }

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

        if (!PathSafety.TryEnsureWritableDirectory(dialog.FolderName, out var full, out var error))
        {
            MessageBox.Show(error ?? "Dossier inaccessible.", "Karu", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        FolderBox.Text = full;
        _directory = full;
        RaiseSettingsChanged();
        RefreshFolders();
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

    private void FolderList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Pendant un drag, ne pas recharger toute la liste (cause du freeze).
        if (!IsLoaded || _suppressEvents || _isDraggingClip) return;
        RefreshClips();
    }

    private void NavClips_OnClick(object sender, RoutedEventArgs e) => ShowClipsView();

    private void NavSettings_OnClick(object sender, RoutedEventArgs e) => ShowSettingsView();

    private void ShowClipsView()
    {
        ClipsView.Visibility = Visibility.Visible;
        SettingsView.Visibility = Visibility.Collapsed;
        NavClipsButton.Style = (Style)FindResource("NavButtonActive");
        NavSettingsButton.Style = (Style)FindResource("NavButton");
    }

    private void ShowSettingsView()
    {
        ClipsView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Visible;
        NavClipsButton.Style = (Style)FindResource("NavButton");
        NavSettingsButton.Style = (Style)FindResource("NavButtonActive");
    }

    private void NewFolder_OnClick(object sender, RoutedEventArgs e)
    {
        var name = PromptText("Nouveau dossier", "Nom du dossier :", "Aces");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        try
        {
            _library.CreateFolder(_directory, name);
            RefreshFolders();
            SetStatus($"Dossier « {ClipLibrary.SanitizeFolderName(name)} » créé.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Karu", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void DeleteFolder_OnClick(object sender, RoutedEventArgs e)
    {
        if (FolderList.SelectedItem is not FolderListItem item || item.Folder.IsRoot)
        {
            MessageBox.Show("Choisis un dossier (pas « Tous les clips »).", "Karu", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var ask = MessageBox.Show(
            $"Supprimer le dossier « {item.Folder.Name} » ?\nLes clips seront remis à la racine.",
            "Karu",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (ask != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            _library.DeleteFolder(_directory, item.Folder.FullPath, deleteClips: false);
            RefreshFolders();
            RefreshClips();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Karu", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void FavoriteStar_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    private void FavoriteStar_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ClipListItem item })
        {
            return;
        }

        _library.SetFavorite(item.Metadata.VideoPath, !item.Metadata.Favorite);
        RefreshClips();
    }

    private void ClipList_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
    }

    private void ClipList_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || ClipList.SelectedItem is not ClipListItem item)
        {
            return;
        }

        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        // Ne pas lancer un drag depuis l'etoile favori.
        if (e.OriginalSource is DependencyObject src &&
            FindAncestor<Button>(src) is not null)
        {
            return;
        }

        var path = item.Metadata.VideoPath;
        var data = new DataObject(DataFormats.FileDrop, new[] { path });
        _isDraggingClip = true;
        try
        {
            DragDrop.DoDragDrop(ClipList, data, DragDropEffects.Move);
        }
        finally
        {
            _isDraggingClip = false;
            ClearDropHighlight();
        }
    }

    private void FolderList_OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;

        // Throttle ~30 fps pour le hit-test.
        var now = Environment.TickCount64;
        if (now - _lastDragOverTicks < 32)
        {
            return;
        }

        _lastDragOverTicks = now;

        var item = GetFolderItemAt(e.GetPosition(FolderList));
        if (item is null || ReferenceEquals(item, _dropHighlight))
        {
            return;
        }

        ClearDropHighlight();
        _dropHighlight = item;
        if (FolderList.ItemContainerGenerator.ContainerFromItem(item) is ListBoxItem container)
        {
            container.Opacity = 0.85;
            container.BorderBrush = (Brush)FindResource("AccentBrush");
            container.BorderThickness = new Thickness(1);
        }
    }

    private void FolderList_OnDragLeave(object sender, DragEventArgs e)
    {
        // Ne clear que si on quitte vraiment la liste (pas un enfant).
        var pos = e.GetPosition(FolderList);
        if (pos.X < 0 || pos.Y < 0 || pos.X > FolderList.ActualWidth || pos.Y > FolderList.ActualHeight)
        {
            ClearDropHighlight();
        }
    }

    private async void FolderList_OnDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        var target = GetFolderItemAt(e.GetPosition(FolderList))
                     ?? _dropHighlight
                     ?? FolderList.SelectedItem as FolderListItem;
        ClearDropHighlight();
        if (target is null)
        {
            return;
        }

        var files = ((string[])e.Data.GetData(DataFormats.FileDrop)!)
            .Where(f =>
                f.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) &&
                PathSafety.IsUnderRoot(_directory, f))
            .ToArray();
        var dest = target.Folder.IsRoot ? _directory : target.Folder.FullPath;
        if (!PathSafety.IsUnderRoot(_directory, dest) || files.Length == 0)
        {
            return;
        }

        var root = _directory;
        var folderName = target.Folder.Name;
        SetStatus("Deplacement…");

        // Toujours liberer le MediaElement : un mp4 verrouille rend File.Move tres lent.
        StopPlaybackHard();
        Player.Source = null;

        try
        {
            var moved = await Task.Run(() =>
            {
                var results = new List<string>(files.Length);
                foreach (var file in files)
                {
                    results.Add(_library.MoveClip(root, file, dest));
                }

                return results;
            }).ConfigureAwait(true);

            // Mise a jour UI locale : pas de RefreshClips complet (miniatures = freeze).
            ApplyMoveToList(files, moved, target);
            SetStatus($"Clip déplacé vers « {folderName} ».");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Karu", MessageBoxButton.OK, MessageBoxImage.Warning);
            SetStatus("Deplacement echoue.");
            RefreshClips();
        }
    }

    private void ApplyMoveToList(IReadOnlyList<string> sources, IReadOnlyList<string> destinations, FolderListItem target)
    {
        var folder = (FolderList.SelectedItem as FolderListItem)?.Folder;
        var viewingRoot = folder?.IsRoot == true;
        var viewingTarget = folder is not null &&
            string.Equals(folder.FullPath, target.Folder.FullPath, StringComparison.OrdinalIgnoreCase);

        for (var i = 0; i < sources.Count; i++)
        {
            var src = sources[i];
            var item = _items.FirstOrDefault(x =>
                string.Equals(x.Metadata.VideoPath, src, StringComparison.OrdinalIgnoreCase));
            if (item is null)
            {
                continue;
            }

            // Vue dossier source : le clip part → on l'enleve.
            // Vue racine : il reste visible.
            // Vue dossier cible : rien a enlever (il n'y etait pas).
            if (!viewingRoot)
            {
                _items.Remove(item);
            }
            else
            {
                // Met a jour le chemin en place (meme objet UI / meme miniature).
                item.Metadata.VideoPath = destinations[i];
                item.Metadata.ThumbnailPath = ClipArtifactPaths.ResolveExistingThumbnail(destinations[i]);
            }
        }

        if (viewingTarget && !viewingRoot)
        {
            // Optionnel : pas de reload lourd ; l'utilisateur verra le clip en ouvrant le dossier.
        }

        _suppressEvents = true;
        FolderList.SelectedItem = target;
        _suppressEvents = false;
    }

    private static BitmapImage? GetCachedThumb(string thumbPath)
    {
        lock (ThumbCache)
        {
            if (ThumbCache.TryGetValue(thumbPath, out var cached))
            {
                ThumbCacheOrder.Remove(thumbPath);
                ThumbCacheOrder.AddLast(thumbPath);
                return cached;
            }
        }

        try
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.UriSource = new Uri(thumbPath);
            img.DecodePixelWidth = 240;
            img.EndInit();
            img.Freeze();
            lock (ThumbCache)
            {
                ThumbCache[thumbPath] = img;
                ThumbCacheOrder.Remove(thumbPath);
                ThumbCacheOrder.AddLast(thumbPath);
                while (ThumbCacheOrder.Count > MaxThumbCacheEntries)
                {
                    var oldest = ThumbCacheOrder.First!.Value;
                    ThumbCacheOrder.RemoveFirst();
                    ThumbCache.Remove(oldest);
                }
            }

            return img;
        }
        catch
        {
            return null;
        }
    }

    private void ClearDropHighlight()
    {
        if (_dropHighlight is null)
        {
            return;
        }

        if (FolderList.ItemContainerGenerator.ContainerFromItem(_dropHighlight) is ListBoxItem container)
        {
            container.ClearValue(UIElement.OpacityProperty);
            container.ClearValue(Control.BorderBrushProperty);
            container.ClearValue(Control.BorderThicknessProperty);
        }

        _dropHighlight = null;
    }

    private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private FolderListItem? GetFolderItemAt(Point position)
    {
        var element = FolderList.InputHitTest(position) as DependencyObject;
        while (element is not null)
        {
            if (element is ListBoxItem listItem)
            {
                return listItem.DataContext as FolderListItem;
            }

            element = System.Windows.Media.VisualTreeHelper.GetParent(element);
        }

        return null;
    }

    private static string? PromptText(string title, string message, string defaultValue)
    {
        // Couleurs en dur : le style TextBox themé cassait le contraste (texte illisible).
        var bg = new SolidColorBrush(Color.FromRgb(0x08, 0x08, 0x0A));
        var fieldBg = new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x32));
        var border = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x60));
        var white = Brushes.White;
        var select = new SolidColorBrush(Color.FromRgb(0x9E, 0x1B, 0x2A));

        var win = new Window
        {
            Title = title,
            Width = 380,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = bg,
            Foreground = white,
            Owner = Application.Current.MainWindow,
            ShowInTaskbar = false
        };

        var box = new TextBox
        {
            // Ignore le ControlTemplate app (source du texte invisible).
            Style = null,
            Text = defaultValue,
            Height = 34,
            Margin = new Thickness(0, 8, 0, 16),
            Padding = new Thickness(10, 6, 10, 6),
            VerticalContentAlignment = VerticalAlignment.Center,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = white,
            Background = fieldBg,
            BorderBrush = border,
            BorderThickness = new Thickness(1),
            CaretBrush = white,
            SelectionBrush = select,
            SelectionOpacity = 0.55
        };
        var ok = new Button
        {
            Content = "OK",
            Width = 88,
            Height = 32,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true
        };
        var cancel = new Button
        {
            Content = "Annuler",
            Width = 88,
            Height = 32,
            IsCancel = true
        };
        string? result = null;
        ok.Click += (_, _) => { result = box.Text; win.DialogResult = true; };
        cancel.Click += (_, _) => win.DialogResult = false;

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(new TextBlock
        {
            Text = message,
            Margin = new Thickness(0, 0, 0, 4),
            Foreground = white,
            FontSize = 13
        });
        root.Children.Add(box);
        root.Children.Add(buttons);
        win.Content = root;

        win.Loaded += (_, _) =>
        {
            box.Focus();
            box.CaretIndex = box.Text?.Length ?? 0;
            // Pas de SelectAll : la selection rouge sombre rendait le texte invisible.
        };
        return win.ShowDialog() == true ? result : null;
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
            StopPlaybackHard();
            Player.Source = new Uri(item.Metadata.VideoPath);
            StartPlayback();
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

    private void PlayPause_OnClick(object sender, RoutedEventArgs e)
    {
        if (Player.Source is null)
        {
            return;
        }

        if (_isPlaying)
        {
            PausePlayback();
        }
        else
        {
            StartPlayback();
        }
    }

    private void SeekBack_OnClick(object sender, RoutedEventArgs e) => SeekBy(TimeSpan.FromSeconds(-5));

    private void SeekForward_OnClick(object sender, RoutedEventArgs e) => SeekBy(TimeSpan.FromSeconds(5));

    private void SeekBy(TimeSpan delta)
    {
        if (!Player.NaturalDuration.HasTimeSpan)
        {
            return;
        }

        var next = Player.Position + delta;
        if (next < TimeSpan.Zero) next = TimeSpan.Zero;
        if (next > Player.NaturalDuration.TimeSpan) next = Player.NaturalDuration.TimeSpan;
        SeekTo(next, resumeIfWasPlaying: true);
    }

    private void SeekTo(TimeSpan position, bool resumeIfWasPlaying)
    {
        if (!Player.NaturalDuration.HasTimeSpan || Player.Source is null)
        {
            return;
        }

        var wasPlaying = _isPlaying;
        // Pause first: setting Position while playing makes WPF MediaElement desync (pause/audio bugs).
        PausePlayback();
        Player.Position = position;
        UpdatePlaybackUi();

        if (resumeIfWasPlaying && wasPlaying)
        {
            StartPlayback();
        }
    }

    private void StartPlayback()
    {
        if (Player.Source is null)
        {
            return;
        }

        ApplyUserVolume();
        Player.Play();
        _isPlaying = true;
        _playTimer.Start();
        UpdatePlayPauseLabel();
    }

    private void PausePlayback()
    {
        try
        {
            Player.Pause();
        }
        catch
        {
            // ignore
        }

        _isPlaying = false;
        _playTimer.Stop();
        UpdatePlayPauseLabel();
        UpdatePlaybackUi();
    }

    private void StopPlaybackHard()
    {
        _playTimer.Stop();
        _isPlaying = false;
        _isSeeking = false;
        try
        {
            Player.Stop();
        }
        catch
        {
            // ignore
        }

        SeekSlider.Value = 0;
        TimeText.Text = "0:00 / 0:00";
        UpdatePlayPauseLabel();
    }

    private void ApplyUserVolume()
    {
        _userVolume = Math.Clamp(VolumeSlider.Value, 0, 1);
        Player.Volume = _userVolume;
        VolumeText.Text = $"{(int)Math.Round(_userVolume * 100)}%";
    }

    private void VolumeSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded) return;
        ApplyUserVolume();
    }

    private void SeekSlider_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isSeeking = true;
        _resumeAfterSeek = _isPlaying;
        PausePlayback();
    }

    private void SeekSlider_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (Player.NaturalDuration.HasTimeSpan)
        {
            Player.Position = TimeSpan.FromSeconds(SeekSlider.Value);
        }

        _isSeeking = false;
        UpdatePlaybackUi();

        if (_resumeAfterSeek)
        {
            StartPlayback();
        }

        _resumeAfterSeek = false;
    }

    private void SeekSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Pendant le drag : maj du temps seulement, PAS de Position (évite le bug audio).
        if (!IsLoaded || !_isSeeking || !Player.NaturalDuration.HasTimeSpan)
        {
            return;
        }

        var preview = TimeSpan.FromSeconds(SeekSlider.Value);
        TimeText.Text = $"{FormatTime(preview)} / {FormatTime(Player.NaturalDuration.TimeSpan)}";
    }

    private void Player_OnMediaOpened(object sender, RoutedEventArgs e)
    {
        if (Player.NaturalDuration.HasTimeSpan)
        {
            SeekSlider.Maximum = Math.Max(0.1, Player.NaturalDuration.TimeSpan.TotalSeconds);
            SeekSlider.Value = 0;
        }

        UpdatePlaybackUi();
    }

    private void Player_OnMediaEnded(object sender, RoutedEventArgs e)
    {
        PausePlayback();
        if (Player.NaturalDuration.HasTimeSpan)
        {
            Player.Position = Player.NaturalDuration.TimeSpan;
            UpdatePlaybackUi();
        }
    }

    private void UpdatePlaybackUi()
    {
        if (!Player.NaturalDuration.HasTimeSpan)
        {
            TimeText.Text = "0:00 / 0:00";
            return;
        }

        var total = Player.NaturalDuration.TimeSpan;
        var pos = Player.Position;
        if (!_isSeeking)
        {
            SeekSlider.Maximum = Math.Max(0.1, total.TotalSeconds);
            SeekSlider.Value = Math.Clamp(pos.TotalSeconds, 0, SeekSlider.Maximum);
        }

        TimeText.Text = $"{FormatTime(pos)} / {FormatTime(total)}";
    }

    private void UpdatePlayPauseLabel()
    {
        PlayPauseButton.Content = _isPlaying ? "\u23F8" : "\u25B6";
        PlayPauseButton.ToolTip = _isPlaying ? "Pause" : "Lecture";
    }

    private static string FormatTime(TimeSpan time)
    {
        if (time.TotalHours >= 1)
        {
            return time.ToString(@"h\:mm\:ss");
        }

        return time.ToString(@"m\:ss");
    }

    private void Delete_OnClick(object sender, RoutedEventArgs e)
    {
        if (ClipList.SelectedItem is not ClipListItem item) return;
        var ask = MessageBox.Show(
            $"Supprimer {item.DisplayName} ?",
            "Karu",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (ask != MessageBoxResult.Yes) return;

        StopPlaybackHard();
        Player.Source = null;
        _library.Delete(_directory, item.Metadata.VideoPath);
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
        var path = (FolderList.SelectedItem as FolderListItem)?.Folder.FullPath ?? _directory;
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
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
        StopPlaybackHard();
        Hide();
    }

    private void RaiseSettingsChanged()
    {
        var config = BuildConfigFromUi();
        config.Clamp();
        var rootChanged = !string.Equals(_directory, config.SaveDirectory, StringComparison.OrdinalIgnoreCase);
        _directory = config.SaveDirectory;
        SettingsChanged?.Invoke(config);
        if (rootChanged)
        {
            RefreshFolders();
            RefreshClips();
        }
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
        Theme = ThemeBox.SelectedIndex == 1 ? ThemeService.Aventurine : ThemeService.Carbon,
        StartWithWindows = StartWithWindowsCheck.IsChecked == true,
        PauseBufferOnBattery = PauseOnBatteryCheck.IsChecked == true,
        CheckUpdatesOnStartup = CheckUpdatesCheck.IsChecked == true,
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
