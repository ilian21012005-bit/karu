using System.Text.Json;
using System.Text.Json.Serialization;

namespace Karu.Core;

public sealed class AppConfig
{
    public const int MinBufferSeconds = 15;
    public const int MaxBufferSeconds = 600;
    public const int DefaultBufferSeconds = 300;
    public const string DefaultHotkey = "F9";

    public int BufferSeconds { get; set; } = DefaultBufferSeconds;
    public string Hotkey { get; set; } = DefaultHotkey;
    public string SaveDirectory { get; set; } = DefaultSaveDirectory();

    public bool AutoTriple { get; set; }
    public bool AutoQuad { get; set; }
    public bool AutoAce { get; set; }
    public int HighlightCooldownSeconds { get; set; } = 15;
    public string PlayerName { get; set; } = "";
    public bool HighlightsEnabled { get; set; }

    /// <summary>"carbon" (défaut) ou "aventurine".</summary>
    public string Theme { get; set; } = "carbon";

    /// <summary>Démarrage avec Windows (HKCU Run, sans admin).</summary>
    public bool StartWithWindows { get; set; }

    /// <summary>Suspend le buffer NVENC sur batterie (laptops).</summary>
    public bool PauseBufferOnBattery { get; set; } = true;

    /// <summary>Vérifier GitHub Releases au démarrage.</summary>
    public bool CheckUpdatesOnStartup { get; set; } = true;

    /// <summary>Normalized killfeed rect (0-1 relative to primary screen).</summary>
    public double KillfeedX { get; set; } = 0.72;
    public double KillfeedY { get; set; } = 0.02;
    public double KillfeedW { get; set; } = 0.27;
    public double KillfeedH { get; set; } = 0.22;

    public static string DefaultSaveDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Karu");

    public static AppConfig CreateDefault() => new();

    public void Clamp()
    {
        BufferSeconds = Math.Clamp(BufferSeconds, MinBufferSeconds, MaxBufferSeconds);
        HighlightCooldownSeconds = Math.Clamp(HighlightCooldownSeconds, 5, 120);
        if (string.IsNullOrWhiteSpace(Hotkey))
        {
            Hotkey = DefaultHotkey;
        }

        if (string.IsNullOrWhiteSpace(SaveDirectory))
        {
            SaveDirectory = DefaultSaveDirectory();
        }
        else
        {
            try
            {
                SaveDirectory = PathSafety.NormalizeDirectory(SaveDirectory);
            }
            catch
            {
                SaveDirectory = DefaultSaveDirectory();
            }
        }

        PlayerName = (PlayerName ?? "").Trim();
        Theme = NormalizeTheme(Theme);
        KillfeedX = Math.Clamp(KillfeedX, 0, 0.95);
        KillfeedY = Math.Clamp(KillfeedY, 0, 0.95);
        KillfeedW = Math.Clamp(KillfeedW, 0.05, 1 - KillfeedX);
        KillfeedH = Math.Clamp(KillfeedH, 0.05, 1 - KillfeedY);
    }

    private static readonly HashSet<string> KnownThemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "carbon", "aventurine", "plum", "ember", "forest", "ocean", "holst", "crimson"
    };

    private static string NormalizeTheme(string? theme)
    {
        var id = (theme ?? "carbon").Trim().ToLowerInvariant();
        return KnownThemes.Contains(id) ? id : "carbon";
    }

    public ScreenRect GetKillfeedPixelRect(int screenWidth, int screenHeight)
    {
        var x = (int)(KillfeedX * screenWidth);
        var y = (int)(KillfeedY * screenHeight);
        var w = Math.Max(32, (int)(KillfeedW * screenWidth));
        var h = Math.Max(32, (int)(KillfeedH * screenHeight));
        if (x + w > screenWidth) w = screenWidth - x;
        if (y + h > screenHeight) h = screenHeight - y;
        return new ScreenRect(x, y, Math.Max(1, w), Math.Max(1, h));
    }
}

public readonly record struct ScreenRect(int X, int Y, int Width, int Height);

public static class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string DefaultPath
    {
        get
        {
            var karu = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Karu",
                "config.json");
            MigrateLegacyConfigIfNeeded(karu);
            return karu;
        }
    }

    /// <summary>Ancien dossier produit « ClipBuffer » → « Karu ».</summary>
    private static void MigrateLegacyConfigIfNeeded(string karuPath)
    {
        try
        {
            if (File.Exists(karuPath))
            {
                return;
            }

            var legacy = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ClipBuffer",
                "config.json");
            if (!File.Exists(legacy))
            {
                return;
            }

            var dir = Path.GetDirectoryName(karuPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.Copy(legacy, karuPath, overwrite: false);
        }
        catch
        {
            // ignore — démarrage avec defaults si migration impossible
        }
    }

    public static AppConfig Load(string path)
    {
        if (!File.Exists(path))
        {
            var missing = AppConfig.CreateDefault();
            missing.Clamp();
            return missing;
        }

        var json = File.ReadAllText(path);
        var loaded = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? AppConfig.CreateDefault();
        loaded.Clamp();
        return loaded;
    }

    public static void Save(string path, AppConfig config)
    {
        config.Clamp();
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(config, JsonOptions);
        var temp = path + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, path, overwrite: true);
    }
}
