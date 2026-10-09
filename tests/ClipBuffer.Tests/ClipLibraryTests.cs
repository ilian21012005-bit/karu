namespace ClipBuffer.Tests;

public class ClipLibraryTests
{
    [Fact]
    public void Register_writes_sidecar_and_lists_clip()
    {
        var dir = CreateTempDir();
        try
        {
            var video = Path.Combine(dir, "Valorant_2026-10-08_12-00-00.mp4");
            File.WriteAllBytes(video, new byte[] { 0 });

            var library = new ClipLibrary();
            var meta = library.Register(video, ClipTag.Triple);

            Assert.Equal(ClipTag.Triple, meta.Tag);
            Assert.True(File.Exists(ClipMetadata.SidecarPathFor(video)));

            var listed = library.List(dir, ClipFilter.Triple);
            Assert.Single(listed);
            Assert.Equal(video, listed[0].VideoPath);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Filter_favorites_only_returns_starred()
    {
        var dir = CreateTempDir();
        try
        {
            var a = Path.Combine(dir, "a.mp4");
            var b = Path.Combine(dir, "b.mp4");
            File.WriteAllBytes(a, new byte[] { 1 });
            File.WriteAllBytes(b, new byte[] { 1 });
            var library = new ClipLibrary();
            library.Register(a, ClipTag.Manual);
            library.Register(b, ClipTag.Ace);
            library.SetFavorite(b, true);

            var favs = library.List(dir, ClipFilter.Favorites);
            Assert.Single(favs);
            Assert.Equal(b, favs[0].VideoPath);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Delete_removes_video_and_sidecar()
    {
        var dir = CreateTempDir();
        try
        {
            var video = Path.Combine(dir, "clip.mp4");
            File.WriteAllBytes(video, new byte[] { 1 });
            var library = new ClipLibrary();
            library.Register(video, ClipTag.Manual);
            library.Delete(dir, video);

            Assert.False(File.Exists(video));
            Assert.False(File.Exists(ClipMetadata.SidecarPathFor(video)));
            Assert.Empty(library.List(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void LoadOrCreate_defaults_missing_sidecar_to_manual()
    {
        var dir = CreateTempDir();
        try
        {
            var video = Path.Combine(dir, "orphan.mp4");
            File.WriteAllBytes(video, new byte[] { 1 });
            var meta = ClipMetadataStore.LoadOrCreate(video);
            Assert.Equal(ClipTag.Manual, meta.Tag);
            Assert.Equal(video, meta.VideoPath);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "clipbuffer-lib-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
