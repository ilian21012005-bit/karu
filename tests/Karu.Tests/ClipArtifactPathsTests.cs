namespace Karu.Tests;

public class ClipArtifactPathsTests
{
    [Fact]
    public void Sidecar_and_thumb_live_under_hidden_karu_folder()
    {
        var root = Path.Combine(Path.GetTempPath(), "karu-meta-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var video = Path.Combine(root, "clip.mp4");
            File.WriteAllBytes(video, new byte[] { 1 });

            var sidecar = ClipArtifactPaths.SidecarPathFor(video);
            var thumb = ClipArtifactPaths.ThumbnailPathFor(video);

            Assert.Contains(Path.DirectorySeparatorChar + ".karu" + Path.DirectorySeparatorChar, sidecar);
            Assert.Contains(Path.DirectorySeparatorChar + ".karu" + Path.DirectorySeparatorChar, thumb);
            Assert.EndsWith(".clip.json", sidecar);
            Assert.EndsWith(".jpg", thumb);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void MigrateLegacyIfNeeded_moves_sidecar_and_jpg_into_karu()
    {
        var root = Path.Combine(Path.GetTempPath(), "karu-mig-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var video = Path.Combine(root, "clip.mp4");
            File.WriteAllBytes(video, new byte[] { 1 });
            var legacyJson = Path.ChangeExtension(video, ".clip.json");
            var legacyJpg = Path.ChangeExtension(video, ".jpg");
            File.WriteAllText(legacyJson, "{}");
            File.WriteAllBytes(legacyJpg, new byte[] { 2 });

            ClipArtifactPaths.MigrateLegacyIfNeeded(video);

            Assert.False(File.Exists(legacyJson));
            Assert.False(File.Exists(legacyJpg));
            Assert.True(File.Exists(ClipArtifactPaths.SidecarPathFor(video)));
            Assert.True(File.Exists(ClipArtifactPaths.ThumbnailPathFor(video)));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
