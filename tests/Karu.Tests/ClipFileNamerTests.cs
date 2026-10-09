namespace Karu.Tests;

public class ClipFileNamerTests
{
    [Fact]
    public void Uses_game_hint_and_local_timestamp()
    {
        var when = new DateTime(2026, 9, 20, 16, 22, 5);
        Assert.Equal("Valorant_2026-09-20_16-22-05.mp4", ClipFileNamer.MakeFileName(when));
    }

    [Fact]
    public void Falls_back_to_Clip_when_game_hint_is_blank()
    {
        var when = new DateTime(2026, 1, 2, 3, 4, 5);
        Assert.Equal("Clip_2026-01-02_03-04-05.mp4", ClipFileNamer.MakeFileName(when, gameHint: "  "));
    }

    [Fact]
    public void MakeFullPath_avoids_existing_file_names()
    {
        var dir = Path.Combine(Path.GetTempPath(), "karu-namer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var when = new DateTime(2026, 3, 4, 5, 6, 7);
            var first = ClipFileNamer.MakeFullPath(dir, when);
            File.WriteAllText(first, "x");
            var second = ClipFileNamer.MakeFullPath(dir, when);
            Assert.NotEqual(first, second);
            Assert.EndsWith("_2.mp4", second, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }
}
