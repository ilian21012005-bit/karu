namespace Karu.Tests;

public class PathSafetyTests
{
    [Fact]
    public void IsUnderRoot_accepts_child_and_rejects_sibling()
    {
        var root = Path.Combine(Path.GetTempPath(), "karu-path-root", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var child = Path.Combine(root, "Aces");
            Directory.CreateDirectory(child);
            var sibling = Path.Combine(Path.GetTempPath(), "karu-path-sibling-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(sibling);

            Assert.True(PathSafety.IsUnderRoot(root, child));
            Assert.True(PathSafety.IsUnderRoot(root, Path.Combine(child, "x.mp4")));
            Assert.False(PathSafety.IsUnderRoot(root, sibling));
            Assert.False(PathSafety.IsUnderRoot(root, Path.Combine(sibling, "x.mp4")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void TryEnsureWritableDirectory_succeeds_for_temp()
    {
        var dir = Path.Combine(Path.GetTempPath(), "karu-writable-" + Guid.NewGuid().ToString("N"));
        Assert.True(PathSafety.TryEnsureWritableDirectory(dir, out var full, out var error));
        Assert.True(Directory.Exists(full));
        Assert.Null(error);
        Directory.Delete(full, true);
    }
}
