namespace ClipBuffer.Tests;

public class ClipFolderTests
{
    [Fact]
    public void CreateFolder_and_ListFolders_includes_root_and_child()
    {
        var root = CreateTempDir();
        try
        {
            var library = new ClipLibrary();
            library.CreateFolder(root, "Aces");
            var folders = library.ListFolders(root);
            Assert.Equal(2, folders.Count);
            Assert.True(folders[0].IsRoot);
            Assert.Equal("Aces", folders[1].Name);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void MoveClip_moves_video_sidecar_and_thumbnail()
    {
        var root = CreateTempDir();
        try
        {
            var library = new ClipLibrary();
            var folder = library.CreateFolder(root, "Ranked");
            var video = Path.Combine(root, "clip.mp4");
            File.WriteAllBytes(video, new byte[] { 1 });
            library.Register(video, ClipTag.Ace);
            ClipArtifactPaths.EnsureMetaDirectory(video);
            var thumb = ClipArtifactPaths.ThumbnailPathFor(video);
            File.WriteAllBytes(thumb, new byte[] { 2 });
            library.SetThumbnail(video, thumb);

            var moved = library.MoveClip(root, video, folder.FullPath);

            Assert.False(File.Exists(video));
            Assert.True(File.Exists(moved));
            Assert.True(File.Exists(ClipMetadata.SidecarPathFor(moved)));
            Assert.True(File.Exists(ClipArtifactPaths.ThumbnailPathFor(moved)));
            Assert.Equal(folder.FullPath, Path.GetDirectoryName(moved));
            Assert.False(File.Exists(Path.ChangeExtension(moved, ".jpg")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void List_non_recursive_hides_clips_in_subfolders()
    {
        var root = CreateTempDir();
        try
        {
            var library = new ClipLibrary();
            var folder = library.CreateFolder(root, "Misc");
            File.WriteAllBytes(Path.Combine(root, "root.mp4"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(folder.FullPath, "nested.mp4"), new byte[] { 1 });

            Assert.Single(library.List(root, recursive: false));
            Assert.Equal(2, library.List(root, recursive: true).Count);
            Assert.Single(library.List(folder.FullPath, recursive: false));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void SanitizeFolderName_strips_invalid_chars()
    {
        Assert.Equal("Ace_Clips", ClipLibrary.SanitizeFolderName("Ace/Clips"));
    }

    [Fact]
    public void MoveClip_rejects_path_outside_root()
    {
        var root = CreateTempDir();
        var outside = CreateTempDir();
        try
        {
            var library = new ClipLibrary();
            var video = Path.Combine(root, "clip.mp4");
            File.WriteAllBytes(video, new byte[] { 1 });
            Assert.Throws<UnauthorizedAccessException>(() => library.MoveClip(root, video, outside));
        }
        finally
        {
            Directory.Delete(root, true);
            Directory.Delete(outside, true);
        }
    }

    [Fact]
    public void SanitizeFolderName_blocks_traversal_and_reserved()
    {
        Assert.DoesNotContain("..", PathSafety.SanitizeFolderName("..\\evil"));
        Assert.Equal("_CON", PathSafety.SanitizeFolderName("CON"));
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "karu-folder-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
