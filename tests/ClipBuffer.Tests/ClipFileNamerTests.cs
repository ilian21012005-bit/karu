namespace ClipBuffer.Tests;

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
}
