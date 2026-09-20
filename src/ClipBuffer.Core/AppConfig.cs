using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClipBuffer.Core;

public sealed class AppConfig
{
    public const int MinBufferSeconds = 15;
    public const int MaxBufferSeconds = 600;
    public const int DefaultBufferSeconds = 300;
    public const string DefaultHotkey = "F9";

    public int BufferSeconds { get; set; } = DefaultBufferSeconds;
    public string Hotkey { get; set; } = DefaultHotkey;
    public string SaveDirectory { get; set; } = DefaultSaveDirectory();

    public static string DefaultSaveDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "ClipBuffer");

    public static AppConfig CreateDefault() => new();

    public void Clamp()
    {
        BufferSeconds = Math.Clamp(BufferSeconds, MinBufferSeconds, MaxBufferSeconds);
        if (string.IsNullOrWhiteSpace(Hotkey))
        {
            Hotkey = DefaultHotkey;
        }

        if (string.IsNullOrWhiteSpace(SaveDirectory))
        {
            SaveDirectory = DefaultSaveDirectory();
        }
    }
}

public static class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ClipBuffer",
        "config.json");

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

        File.WriteAllText(path, JsonSerializer.Serialize(config, JsonOptions));
    }
}
