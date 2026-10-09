using System.Windows;
using System.Windows.Media;

namespace Karu.App.Services;

public static class ThemeService
{
    public const string Carbon = "carbon";
    public const string Aventurine = "aventurine";
    public const string Plum = "plum";
    public const string Ember = "ember";
    public const string Forest = "forest";
    public const string Ocean = "ocean";
    public const string Holst = "holst";
    public const string Crimson = "crimson";

    public static readonly (string Id, string Label)[] All =
    [
        (Carbon, "Carbon (bleu sombre)"),
        (Aventurine, "Aventurine (bordeaux)"),
        (Plum, "Plum (violet)"),
        (Ember, "Ember (terre)"),
        (Forest, "Forest (vert)"),
        (Ocean, "Ocean (bleu)"),
        (Holst, "Holst (rose)"),
        (Crimson, "Crimson (rouge)")
    ];

    public static string Normalize(string? theme)
    {
        var id = (theme ?? Carbon).Trim().ToLowerInvariant();
        return All.Any(t => t.Id == id) ? id : Carbon;
    }

    public static int IndexOf(string? theme)
    {
        var id = Normalize(theme);
        for (var i = 0; i < All.Length; i++)
        {
            if (All[i].Id == id)
            {
                return i;
            }
        }

        return 0;
    }

    public static string IdAt(int index) =>
        index >= 0 && index < All.Length ? All[index].Id : Carbon;

    public static void Apply(string? theme)
    {
        var id = Normalize(theme);
        var p = id switch
        {
            Aventurine => AventurinePalette,
            Plum => PlumPalette,
            Ember => EmberPalette,
            Forest => ForestPalette,
            Ocean => OceanPalette,
            Holst => HolstPalette,
            Crimson => CrimsonPalette,
            _ => CarbonPalette
        };

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

    // #190019 #2B124C #522B5B #854F6C #DFB6B2 #FBE4D8
    private static readonly Palette PlumPalette = new(
        Bg: Rgb(0x19, 0x00, 0x19),
        Panel: Rgb(0x2B, 0x12, 0x4C),
        PanelAlt: Rgb(0x52, 0x2B, 0x5B),
        Text: Rgb(0xFB, 0xE4, 0xD8),
        Muted: Rgb(0xDF, 0xB6, 0xB2),
        Accent: Rgb(0x85, 0x4F, 0x6C),
        AccentDim: Rgb(0x52, 0x2B, 0x5B),
        ButtonBg: Rgb(0x2B, 0x12, 0x4C),
        ButtonHover: Rgb(0x52, 0x2B, 0x5B),
        Border: Rgb(0x52, 0x2B, 0x5B),
        Danger: Rgb(0xC9, 0x3A, 0x4A),
        Track: Rgb(0x2B, 0x12, 0x4C),
        Badge: Rgb(0x52, 0x2B, 0x5B),
        Star: Rgb(0xDF, 0xB6, 0xB2),
        VideoBg: Rgb(0x12, 0x00, 0x14));

    // #1F1D20 #4E2427 #803E2F #A79986 #463E3A #3E3D38
    private static readonly Palette EmberPalette = new(
        Bg: Rgb(0x1F, 0x1D, 0x20),
        Panel: Rgb(0x3E, 0x3D, 0x38),
        PanelAlt: Rgb(0x46, 0x3E, 0x3A),
        Text: Rgb(0xA7, 0x99, 0x86),
        Muted: Rgb(0x80, 0x72, 0x62),
        Accent: Rgb(0x80, 0x3E, 0x2F),
        AccentDim: Rgb(0x4E, 0x24, 0x27),
        ButtonBg: Rgb(0x46, 0x3E, 0x3A),
        ButtonHover: Rgb(0x4E, 0x24, 0x27),
        Border: Rgb(0x46, 0x3E, 0x3A),
        Danger: Rgb(0xAD, 0x28, 0x31),
        Track: Rgb(0x3E, 0x3D, 0x38),
        Badge: Rgb(0x4E, 0x24, 0x27),
        Star: Rgb(0xA7, 0x99, 0x86),
        VideoBg: Rgb(0x14, 0x12, 0x15));

    // #051F20 #0B2B26 #163832 #235347 #8EB69B #DAF1DE
    private static readonly Palette ForestPalette = new(
        Bg: Rgb(0x05, 0x1F, 0x20),
        Panel: Rgb(0x0B, 0x2B, 0x26),
        PanelAlt: Rgb(0x16, 0x38, 0x32),
        Text: Rgb(0xDA, 0xF1, 0xDE),
        Muted: Rgb(0x8E, 0xB6, 0x9B),
        Accent: Rgb(0x23, 0x53, 0x47),
        AccentDim: Rgb(0x16, 0x38, 0x32),
        ButtonBg: Rgb(0x0B, 0x2B, 0x26),
        ButtonHover: Rgb(0x16, 0x38, 0x32),
        Border: Rgb(0x16, 0x38, 0x32),
        Danger: Rgb(0xC9, 0x3A, 0x4A),
        Track: Rgb(0x0B, 0x2B, 0x26),
        Badge: Rgb(0x16, 0x38, 0x32),
        Star: Rgb(0x8E, 0xB6, 0x9B),
        VideoBg: Rgb(0x03, 0x14, 0x15));

    // #021024 #052659 #5483B3 #7DA0CA #C1E8FF
    private static readonly Palette OceanPalette = new(
        Bg: Rgb(0x02, 0x10, 0x24),
        Panel: Rgb(0x05, 0x26, 0x59),
        PanelAlt: Rgb(0x1A, 0x3A, 0x6E),
        Text: Rgb(0xC1, 0xE8, 0xFF),
        Muted: Rgb(0x7D, 0xA0, 0xCA),
        Accent: Rgb(0x54, 0x83, 0xB3),
        AccentDim: Rgb(0x05, 0x26, 0x59),
        ButtonBg: Rgb(0x05, 0x26, 0x59),
        ButtonHover: Rgb(0x1A, 0x3A, 0x6E),
        Border: Rgb(0x1A, 0x3A, 0x6E),
        Danger: Rgb(0xE3, 0x5D, 0x6A),
        Track: Rgb(0x05, 0x26, 0x59),
        Badge: Rgb(0x05, 0x26, 0x59),
        Star: Rgb(0x7D, 0xA0, 0xCA),
        VideoBg: Rgb(0x01, 0x0A, 0x18));

    // #4B0F1E #6D1D32 #8E2B44 #B23C59 #CC5671 #E07A94 #F7D6DC
    private static readonly Palette HolstPalette = new(
        Bg: Rgb(0x4B, 0x0F, 0x1E),
        Panel: Rgb(0x6D, 0x1D, 0x32),
        PanelAlt: Rgb(0x8E, 0x2B, 0x44),
        Text: Rgb(0xF7, 0xD6, 0xDC),
        Muted: Rgb(0xE0, 0x7A, 0x94),
        Accent: Rgb(0xCC, 0x56, 0x71),
        AccentDim: Rgb(0x8E, 0x2B, 0x44),
        ButtonBg: Rgb(0x6D, 0x1D, 0x32),
        ButtonHover: Rgb(0x8E, 0x2B, 0x44),
        Border: Rgb(0x8E, 0x2B, 0x44),
        Danger: Rgb(0xE0, 0x7A, 0x94),
        Track: Rgb(0x6D, 0x1D, 0x32),
        Badge: Rgb(0x8E, 0x2B, 0x44),
        Star: Rgb(0xF7, 0xD6, 0xDC),
        VideoBg: Rgb(0x3A, 0x0A, 0x16));

    // #AD2831 #800E13 #640B0F #38040E #250902 #250902-ish
    private static readonly Palette CrimsonPalette = new(
        Bg: Rgb(0x24, 0x00, 0x00),
        Panel: Rgb(0x2E, 0x00, 0x04),
        PanelAlt: Rgb(0x38, 0x04, 0x0E),
        Text: Rgb(0xF1, 0xF3, 0xF5),
        Muted: Rgb(0xAD, 0x28, 0x31),
        Accent: Rgb(0xAD, 0x28, 0x31),
        AccentDim: Rgb(0x80, 0x0E, 0x13),
        ButtonBg: Rgb(0x38, 0x04, 0x0E),
        ButtonHover: Rgb(0x62, 0x00, 0x00),
        Border: Rgb(0x38, 0x04, 0x0E),
        Danger: Rgb(0xAD, 0x28, 0x31),
        Track: Rgb(0x2E, 0x00, 0x04),
        Badge: Rgb(0x62, 0x00, 0x00),
        Star: Rgb(0xAD, 0x28, 0x31),
        VideoBg: Rgb(0x18, 0x00, 0x00));

    private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);
}
