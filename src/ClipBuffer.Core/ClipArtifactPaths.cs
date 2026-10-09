namespace ClipBuffer.Core;

/// <summary>
/// Sidecars + miniatures dans un dossier caché <c>.karu</c> à côté des mp4,
/// pour ne pas polluer l'Explorateur Windows.
/// </summary>
public static class ClipArtifactPaths
{
    public const string MetaFolderName = ".karu";

    public static string MetaDirectoryFor(string videoPath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(videoPath))
                  ?? throw new ArgumentException("Chemin video invalide.", nameof(videoPath));
        return Path.Combine(dir, MetaFolderName);
    }

    public static string SidecarPathFor(string videoPath)
    {
        var stem = Path.GetFileNameWithoutExtension(videoPath);
        return Path.Combine(MetaDirectoryFor(videoPath), stem + ".clip.json");
    }

    public static string ThumbnailPathFor(string videoPath)
    {
        var stem = Path.GetFileNameWithoutExtension(videoPath);
        return Path.Combine(MetaDirectoryFor(videoPath), stem + ".jpg");
    }

    public static string LegacySidecarPathFor(string videoPath) =>
        Path.ChangeExtension(videoPath, ".clip.json");

    public static string LegacyThumbnailPathFor(string videoPath) =>
        Path.ChangeExtension(videoPath, ".jpg");

    public static void EnsureMetaDirectory(string videoPath)
    {
        var meta = MetaDirectoryFor(videoPath);
        Directory.CreateDirectory(meta);
        TryHide(meta);
    }

    /// <summary>Déplace les anciens .clip.json / .jpg à côté du mp4 vers .karu/.</summary>
    public static void MigrateLegacyIfNeeded(string videoPath)
    {
        if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath))
        {
            return;
        }

        MoveLegacy(LegacySidecarPathFor(videoPath), SidecarPathFor(videoPath));
        MoveLegacy(LegacyThumbnailPathFor(videoPath), ThumbnailPathFor(videoPath));
    }

    public static string? ResolveExistingThumbnail(string videoPath)
    {
        var modern = ThumbnailPathFor(videoPath);
        if (File.Exists(modern))
        {
            return modern;
        }

        var legacy = LegacyThumbnailPathFor(videoPath);
        return File.Exists(legacy) ? legacy : null;
    }

    public static string? ResolveExistingSidecar(string videoPath)
    {
        var modern = SidecarPathFor(videoPath);
        if (File.Exists(modern))
        {
            return modern;
        }

        var legacy = LegacySidecarPathFor(videoPath);
        return File.Exists(legacy) ? legacy : null;
    }

    private static void MoveLegacy(string from, string to)
    {
        if (!File.Exists(from) || string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            var dir = Path.GetDirectoryName(to);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
                TryHide(dir);
            }

            if (File.Exists(to))
            {
                File.Delete(from);
            }
            else
            {
                File.Move(from, to);
            }
        }
        catch
        {
            // ignore migration errors
        }
    }

    private static void TryHide(string path)
    {
        try
        {
            var attrs = File.GetAttributes(path);
            if ((attrs & FileAttributes.Hidden) == 0)
            {
                File.SetAttributes(path, attrs | FileAttributes.Hidden);
            }
        }
        catch
        {
            // ignore
        }
    }
}
