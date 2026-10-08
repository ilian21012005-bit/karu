using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClipBuffer.Core;

public sealed class ClipMetadata
{
    public string VideoPath { get; set; } = "";
    public ClipTag Tag { get; set; } = ClipTag.Manual;
    public bool Favorite { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string? ThumbnailPath { get; set; }

    public string FileName => Path.GetFileName(VideoPath);
    public string DisplayName => Path.GetFileNameWithoutExtension(VideoPath);

    public static string SidecarPathFor(string videoPath) =>
        Path.ChangeExtension(videoPath, ".clip.json");
}

public static class ClipMetadataStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static ClipMetadata LoadOrCreate(string videoPath)
    {
        var sidecar = ClipMetadata.SidecarPathFor(videoPath);
        if (File.Exists(sidecar))
        {
            try
            {
                var json = File.ReadAllText(sidecar);
                var loaded = JsonSerializer.Deserialize<ClipMetadata>(json, JsonOptions);
                if (loaded is not null)
                {
                    loaded.VideoPath = videoPath;
                    return loaded;
                }
            }
            catch
            {
                // fall through to default
            }
        }

        var info = new FileInfo(videoPath);
        return new ClipMetadata
        {
            VideoPath = videoPath,
            Tag = ClipTag.Manual,
            CreatedAt = info.Exists ? info.CreationTime : DateTime.Now
        };
    }

    public static void Save(ClipMetadata metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata.VideoPath))
        {
            throw new ArgumentException("VideoPath requis.", nameof(metadata));
        }

        var sidecar = ClipMetadata.SidecarPathFor(metadata.VideoPath);
        var directory = Path.GetDirectoryName(sidecar);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(sidecar, JsonSerializer.Serialize(metadata, JsonOptions));
    }

    public static void DeleteSidecar(string videoPath)
    {
        var sidecar = ClipMetadata.SidecarPathFor(videoPath);
        if (File.Exists(sidecar))
        {
            File.Delete(sidecar);
        }
    }
}
