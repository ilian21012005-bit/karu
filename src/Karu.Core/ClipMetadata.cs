using System.Text.Json;
using System.Text.Json.Serialization;

namespace Karu.Core;

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
        ClipArtifactPaths.SidecarPathFor(videoPath);
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
        if (File.Exists(ClipArtifactPaths.LegacySidecarPathFor(videoPath)) ||
            File.Exists(ClipArtifactPaths.LegacyThumbnailPathFor(videoPath)))
        {
            ClipArtifactPaths.MigrateLegacyIfNeeded(videoPath);
        }

        var sidecar = ClipArtifactPaths.ResolveExistingSidecar(videoPath)
                      ?? ClipArtifactPaths.SidecarPathFor(videoPath);

        if (File.Exists(sidecar))
        {
            try
            {
                var json = File.ReadAllText(sidecar);
                var loaded = JsonSerializer.Deserialize<ClipMetadata>(json, JsonOptions);
                if (loaded is not null)
                {
                    loaded.VideoPath = videoPath;
                    var thumb = ClipArtifactPaths.ResolveExistingThumbnail(videoPath);
                    if (thumb is not null)
                    {
                        loaded.ThumbnailPath = thumb;
                    }

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
            CreatedAt = info.Exists ? info.CreationTime : DateTime.Now,
            ThumbnailPath = ClipArtifactPaths.ResolveExistingThumbnail(videoPath)
        };
    }

    public static void Save(ClipMetadata metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata.VideoPath))
        {
            throw new ArgumentException("VideoPath requis.", nameof(metadata));
        }

        ClipArtifactPaths.EnsureMetaDirectory(metadata.VideoPath);
        var sidecar = ClipArtifactPaths.SidecarPathFor(metadata.VideoPath);

        // Toujours pointer la miniature vers le chemin moderne si le fichier existe.
        var thumb = ClipArtifactPaths.ResolveExistingThumbnail(metadata.VideoPath);
        if (thumb is not null)
        {
            metadata.ThumbnailPath = ClipArtifactPaths.ThumbnailPathFor(metadata.VideoPath);
            if (!string.Equals(thumb, metadata.ThumbnailPath, StringComparison.OrdinalIgnoreCase) &&
                File.Exists(thumb))
            {
                ClipArtifactPaths.MigrateLegacyIfNeeded(metadata.VideoPath);
                metadata.ThumbnailPath = ClipArtifactPaths.ThumbnailPathFor(metadata.VideoPath);
            }
        }

        File.WriteAllText(sidecar, JsonSerializer.Serialize(metadata, JsonOptions));
    }

    public static void DeleteSidecar(string videoPath)
    {
        TryDelete(ClipArtifactPaths.SidecarPathFor(videoPath));
        TryDelete(ClipArtifactPaths.LegacySidecarPathFor(videoPath));
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // ignore
        }
    }
}
