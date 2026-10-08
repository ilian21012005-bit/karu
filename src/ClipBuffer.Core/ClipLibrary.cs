namespace ClipBuffer.Core;

public sealed class ClipLibrary
{
    public IReadOnlyList<ClipMetadata> List(string directory, ClipFilter filter = ClipFilter.All)
    {
        if (!Directory.Exists(directory))
        {
            return Array.Empty<ClipMetadata>();
        }

        var clips = Directory.EnumerateFiles(directory, "*.mp4")
            .Select(ClipMetadataStore.LoadOrCreate)
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
        var meta = ClipMetadataStore.LoadOrCreate(videoPath);
        meta.ThumbnailPath = thumbnailPath;
        ClipMetadataStore.Save(meta);
    }

    public void Delete(string videoPath)
    {
        ClipMetadataStore.DeleteSidecar(videoPath);
        var thumbCandidate = Path.ChangeExtension(videoPath, ".jpg");
        if (File.Exists(thumbCandidate))
        {
            try { File.Delete(thumbCandidate); } catch { /* ignore */ }
        }

        if (File.Exists(videoPath))
        {
            File.Delete(videoPath);
        }
    }
}
