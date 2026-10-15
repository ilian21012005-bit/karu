using System.Windows;
using System.Windows.Media;

namespace Karu.App.Services;

public static class ThemeService
{
    public const string Riot = "riot";
    public static readonly (string Id, string Label)[] All = [(Riot, "Riot")];

    public static string Normalize(string? theme) => Riot;
    public static int IndexOf(string? theme) => 0;
    public static string IdAt(int index) => Riot;

    public static void Apply(string? theme)
    {
        var resources = Application.Current.Resources;
        Set(resources, "BgBrush", "#0A0A0B");
        Set(resources, "PanelBrush", "#141416");
        Set(resources, "PanelAltBrush", "#1C1C1F");
        Set(resources, "TextBrush", "#F5F5F5");
        Set(resources, "MutedBrush", "#9A9694");
        Set(resources, "AccentBrush", "#FF4655");
        Set(resources, "AccentDimBrush", "#8A1E28");
        Set(resources, "ButtonBgBrush", "#222021");
        Set(resources, "ButtonHoverBrush", "#49383B");
        Set(resources, "BorderBrush", "#2E2A2B");
        Set(resources, "DangerBrush", "#FF4655");
        Set(resources, "TrackBrush", "#2E2A2B");
        Set(resources, "BadgeBrush", "#2A1216");
        Set(resources, "StarBrush", "#FFB84A");
        Set(resources, "VideoBgBrush", "#000000");
    }

    private static void Set(ResourceDictionary resources, string key, string color) =>
        resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
}
