namespace Karu.Core;

public sealed class ClipFolder
{
    public required string FullPath { get; init; }
    public required string Name { get; init; }
    public bool IsRoot { get; init; }
}

public sealed class ClipLibrary
{
    public IReadOnlyList<ClipFolder> ListFolders(string rootDirectory)
    {
        if (!Directory.Exists(rootDirectory))
        {
            return Array.Empty<ClipFolder>();
        }

        var folders = new List<ClipFolder>
        {
            new()
            {
                FullPath = Path.GetFullPath(rootDirectory),
                Name = "Tous les clips",
                IsRoot = true
            }
        };

        foreach (var dir in Directory.EnumerateDirectories(rootDirectory)
                     .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(dir);
            if (string.IsNullOrWhiteSpace(name) || name.StartsWith('.'))
            {
                continue;
            }

            folders.Add(new ClipFolder
            {
                FullPath = Path.GetFullPath(dir),
                Name = name,
                IsRoot = false
            });
        }

        return folders;
    }

    public ClipFolder CreateFolder(string rootDirectory, string folderName)
    {
        var root = PathSafety.NormalizeDirectory(rootDirectory);
        var safe = SanitizeFolderName(folderName);
        if (string.IsNullOrWhiteSpace(safe))
        {
            throw new ArgumentException("Nom de dossier invalide.", nameof(folderName));
        }

        Directory.CreateDirectory(root);
        var path = Path.Combine(root, safe);
        PathSafety.EnsureUnderRoot(root, path);
        if (Directory.Exists(path))
        {
            throw new InvalidOperationException("Ce dossier existe déjà.");
        }

        Directory.CreateDirectory(path);
        return new ClipFolder { FullPath = Path.GetFullPath(path), Name = safe, IsRoot = false };
    }

    public void RenameFolder(string rootDirectory, string folderPath, string newName)
    {
        var root = PathSafety.NormalizeDirectory(rootDirectory);
        PathSafety.EnsureUnderRoot(root, folderPath);
        var safe = SanitizeFolderName(newName);
        if (string.IsNullOrWhiteSpace(safe))
        {
            throw new ArgumentException("Nom de dossier invalide.", nameof(newName));
        }

        var parent = Path.GetDirectoryName(folderPath)
                     ?? throw new InvalidOperationException("Dossier invalide.");
        PathSafety.EnsureUnderRoot(root, parent);
        var dest = Path.Combine(parent, safe);
        PathSafety.EnsureUnderRoot(root, dest);
        if (string.Equals(Path.GetFullPath(folderPath), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (Directory.Exists(dest))
        {
            throw new InvalidOperationException("Ce dossier existe déjà.");
        }

        Directory.Move(folderPath, dest);
    }

    public void DeleteFolder(string rootDirectory, string folderPath, bool deleteClips)
    {
        var root = PathSafety.NormalizeDirectory(rootDirectory);
        PathSafety.EnsureUnderRoot(root, folderPath);
        if (string.Equals(Path.GetFullPath(folderPath), root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Impossible de supprimer le dossier racine.");
        }

        if (!Directory.Exists(folderPath))
        {
            return;
        }

        if (!deleteClips)
        {
            var parent = Path.GetDirectoryName(folderPath)
                         ?? throw new InvalidOperationException("Dossier invalide.");
            PathSafety.EnsureUnderRoot(root, parent);
            foreach (var video in Directory.EnumerateFiles(folderPath, "*.mp4"))
            {
                MoveClip(root, video, parent);
            }
        }

        Directory.Delete(folderPath, recursive: true);
    }

    public IReadOnlyList<ClipMetadata> List(string directory, ClipFilter filter = ClipFilter.All, bool recursive = false)
    {
        if (!Directory.Exists(directory))
        {
            return Array.Empty<ClipMetadata>();
        }

        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var clips = Directory.EnumerateFiles(directory, "*.mp4", option)
            .Select(AttachThumbnailIfPresent)
            .OrderByDescending(c => c.CreatedAt)
            .ToList();

        return filter switch
        {
            ClipFilter.Manual => clips.Where(c => c.Tag == ClipTag.Manual).ToList(),
            ClipFilter.Triple => clips.Where(c => c.Tag == ClipTag.Triple).ToList(),
            ClipFilter.Quad => clips.Where(c => c.Tag == ClipTag.Quad).ToList(),
            ClipFilter.Ace => clips.Where(c => c.Tag == ClipTag.Ace).ToList(),
            ClipFilter.Favorites => clips.Where(c => c.Favorite).ToList(),
            _ => clips
        };
    }

    public ClipMetadata Register(string videoPath, ClipTag tag)
    {
        var meta = ClipMetadataStore.LoadOrCreate(videoPath);
        meta.Tag = tag;
        meta.CreatedAt = DateTime.Now;
        ClipMetadataStore.Save(meta);
        return meta;
    }

    public void SetFavorite(string videoPath, bool favorite)
    {
        var meta = ClipMetadataStore.LoadOrCreate(videoPath);
        meta.Favorite = favorite;
        ClipMetadataStore.Save(meta);
    }

    public void SetTag(string videoPath, ClipTag tag)
    {
        var meta = ClipMetadataStore.LoadOrCreate(videoPath);
        meta.Tag = tag;
        ClipMetadataStore.Save(meta);
    }

    public void SetThumbnail(string videoPath, string thumbnailPath)
    {
        ClipArtifactPaths.EnsureMetaDirectory(videoPath);
        var dest = ClipArtifactPaths.ThumbnailPathFor(videoPath);
        if (!string.Equals(Path.GetFullPath(thumbnailPath), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase)
            && File.Exists(thumbnailPath))
        {
            File.Copy(thumbnailPath, dest, overwrite: true);
            try { File.Delete(thumbnailPath); } catch { /* ignore */ }
        }

        var meta = ClipMetadataStore.LoadOrCreate(videoPath);
        meta.ThumbnailPath = dest;
        ClipMetadataStore.Save(meta);
    }

    /// <summary>Move video + sidecar + thumbnail into targetDirectory. Returns new video path.</summary>
    public string MoveClip(string rootDirectory, string videoPath, string targetDirectory)
    {
        var root = PathSafety.NormalizeDirectory(rootDirectory);
        PathSafety.EnsureUnderRoot(root, videoPath);
        PathSafety.EnsureUnderRoot(root, targetDirectory);

        if (!File.Exists(videoPath))
        {
            throw new FileNotFoundException("Clip introuvable.", videoPath);
        }

        // Migration legacy uniquement si anciens fichiers presents (rapide sinon).
        if (File.Exists(ClipArtifactPaths.LegacySidecarPathFor(videoPath)) ||
            File.Exists(ClipArtifactPaths.LegacyThumbnailPathFor(videoPath)))
        {
            ClipArtifactPaths.MigrateLegacyIfNeeded(videoPath);
        }

        Directory.CreateDirectory(targetDirectory);
        var targetDir = Path.GetFullPath(targetDirectory);
        var currentDir = Path.GetFullPath(Path.GetDirectoryName(videoPath)!);
        if (string.Equals(currentDir, targetDir, StringComparison.OrdinalIgnoreCase))
        {
            return videoPath;
        }

        var fileName = Path.GetFileName(videoPath);
        var destVideo = Path.Combine(targetDir, fileName);
        PathSafety.EnsureUnderRoot(root, destVideo);
        destVideo = EnsureUniquePath(destVideo);

        var sidecar = ClipArtifactPaths.SidecarPathFor(videoPath);
        var thumb = ClipArtifactPaths.ThumbnailPathFor(videoPath);

        // Rename disque d'abord (meme volume = instantane), meta ensuite.
        File.Move(videoPath, destVideo);

        ClipArtifactPaths.EnsureMetaDirectory(destVideo);
        var destSidecar = ClipArtifactPaths.SidecarPathFor(destVideo);
        if (File.Exists(sidecar))
        {
            File.Move(sidecar, destSidecar, overwrite: true);
        }

        var destThumb = ClipArtifactPaths.ThumbnailPathFor(destVideo);
        if (File.Exists(thumb))
        {
            File.Move(thumb, destThumb, overwrite: true);
        }

        // Mise a jour legere du sidecar (evite Reload complet lent).
        try
        {
            var meta = File.Exists(destSidecar)
                ? ClipMetadataStore.LoadOrCreate(destVideo)
                : new ClipMetadata { VideoPath = destVideo, CreatedAt = DateTime.Now };
            meta.VideoPath = destVideo;
            meta.ThumbnailPath = File.Exists(destThumb) ? destThumb : null;
            ClipMetadataStore.Save(meta);
        }
        catch
        {
            // le mp4 est deja deplace ; sidecar optionnel
        }

        return destVideo;
    }

    public void Delete(string rootDirectory, string videoPath)
    {
        var root = PathSafety.NormalizeDirectory(rootDirectory);
        PathSafety.EnsureUnderRoot(root, videoPath);
        ClipMetadataStore.DeleteSidecar(videoPath);
        TryDeleteFile(ClipArtifactPaths.ThumbnailPathFor(videoPath));
        TryDeleteFile(ClipArtifactPaths.LegacyThumbnailPathFor(videoPath));

        if (File.Exists(videoPath))
        {
            File.Delete(videoPath);
        }
    }

    public static string SanitizeFolderName(string name) => PathSafety.SanitizeFolderName(name);

    private static ClipMetadata AttachThumbnailIfPresent(string videoPath)
    {
        // Pas de migration ici : List() doit rester rapide a l'affichage.
        var meta = ClipMetadataStore.LoadOrCreate(videoPath);
        meta.ThumbnailPath = ClipArtifactPaths.ResolveExistingThumbnail(videoPath);
        return meta;
    }

    private static void TryDeleteFile(string path)
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

    private static string EnsureUniquePath(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        var dir = Path.GetDirectoryName(path)!;
        var stem = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (var i = 2; i < 1000; i++)
        {
            var candidate = Path.Combine(dir, $"{stem}_{i}{ext}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(dir, $"{stem}_{Guid.NewGuid():N}{ext}");
    }
}
