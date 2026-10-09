using System.Windows;
using System.Windows.Media;

namespace Karu.App.Services;

public static class ThemeService
{
    public const string Carbon = "carbon";
    public const string Aventurine = "aventurine";

    public static string Normalize(string? theme) =>
        string.Equals(theme, Aventurine, StringComparison.OrdinalIgnoreCase) ? Aventurine : Carbon;

    public static void Apply(string? theme)
    {
        var id = Normalize(theme);
        var p = id == Aventurine ? AventurinePalette : CarbonPalette;
        var res = Application.Current.Resources;

        Set(res, "BgBrush", p.Bg);
        Set(res, "PanelBrush", p.Panel);
        Set(res, "PanelAltBrush", p.PanelAlt);
        Set(res, "TextBrush", p.Text);
        Set(res, "MutedBrush", p.Muted);
        Set(res, "AccentBrush", p.Accent);
        Set(res, "AccentDimBrush", p.AccentDim);
        Set(res, "ButtonBgBrush", p.ButtonBg);
        Set(res, "ButtonHoverBrush", p.ButtonHover);
        Set(res, "BorderBrush", p.Border);
        Set(res, "DangerBrush", p.Danger);
        Set(res, "TrackBrush", p.Track);
        Set(res, "BadgeBrush", p.Badge);
        Set(res, "StarBrush", p.Star);
        Set(res, "VideoBgBrush", p.VideoBg);
    }

    private static void Set(ResourceDictionary res, string key, Color color) =>
        res[key] = new SolidColorBrush(color);

    private readonly record struct Palette(
        Color Bg,
        Color Panel,
        Color PanelAlt,
        Color Text,
        Color Muted,
        Color Accent,
        Color AccentDim,
        Color ButtonBg,
        Color ButtonHover,
        Color Border,
        Color Danger,
        Color Track,
        Color Badge,
        Color Star,
        Color VideoBg);

    // Thème actuel (bleu / carbone)
    private static readonly Palette CarbonPalette = new(
        Bg: Rgb(0x0E, 0x10, 0x14),
        Panel: Rgb(0x17, 0x1A, 0x21),
        PanelAlt: Rgb(0x21, 0x26, 0x32),
        Text: Rgb(0xF1, 0xF3, 0xF5),
        Muted: Rgb(0x8B, 0x93, 0xA7),
        Accent: Rgb(0x5B, 0x9D, 0xFF),
        AccentDim: Rgb(0x2A, 0x4A, 0x7A),
        ButtonBg: Rgb(0x2A, 0x30, 0x40),
        ButtonHover: Rgb(0x36, 0x3E, 0x52),
        Border: Rgb(0x2C, 0x33, 0x44),
        Danger: Rgb(0xE3, 0x5D, 0x6A),
        Track: Rgb(0x2C, 0x33, 0x44),
        Badge: Rgb(0x1A, 0x27, 0x40),
        Star: Rgb(0xF0, 0xC3, 0x5A),
        VideoBg: Rgb(0x07, 0x08, 0x0C));

    // BMW Individual Aventurinrot — noirs quasi purs, rouge inchangé
    private static readonly Palette AventurinePalette = new(
        Bg: Rgb(0x00, 0x00, 0x00),
        Panel: Rgb(0x06, 0x06, 0x07),
        PanelAlt: Rgb(0x0C, 0x0C, 0x0E),
        Text: Rgb(0xFF, 0xFF, 0xFF),
        Muted: Rgb(0x7A, 0x7A, 0x84),
        Accent: Rgb(0x9E, 0x1B, 0x2A),
        AccentDim: Rgb(0x5E, 0x12, 0x1E),
        ButtonBg: Rgb(0x0E, 0x0E, 0x10),
        ButtonHover: Rgb(0x5E, 0x12, 0x1E),
        Border: Rgb(0x16, 0x16, 0x1A),
        Danger: Rgb(0xC9, 0x3A, 0x4A),
        Track: Rgb(0x16, 0x16, 0x1A),
        Badge: Rgb(0x1A, 0x08, 0x0C),
        Star: Rgb(0xE8, 0x5A, 0x6A),
        VideoBg: Rgb(0x00, 0x00, 0x00));

    private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);
}
