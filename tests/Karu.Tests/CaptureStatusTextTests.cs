namespace Karu.Tests;

public class CaptureStatusTextTests
{
    [Fact]
    public void Waiting_when_valorant_not_running()
    {
        Assert.Equal(
            CaptureStatusText.WaitingForValorant,
            CaptureStatusText.Build(false, false, false, true, true));
    }

    [Fact]
    public void Detection_on_when_focused_with_player()
    {
        Assert.Equal(
            CaptureStatusText.BufferDetectionOn,
            CaptureStatusText.Build(false, true, true, true, true));
    }

    [Fact]
    public void Need_focus_when_running_unfocused()
    {
        Assert.Equal(
            CaptureStatusText.BufferNeedFocus,
            CaptureStatusText.Build(false, true, false, true, true));
    }

    [Fact]
    public void Missing_player_when_highlights_on()
    {
        Assert.Equal(
            CaptureStatusText.BufferMissingPlayer,
            CaptureStatusText.Build(false, true, true, true, false));
    }

    [Fact]
    public void Buffer_only_when_highlights_off()
    {
        Assert.Equal(
            CaptureStatusText.BufferActive,
            CaptureStatusText.Build(false, true, true, false, false));
    }

    [Fact]
    public void Battery_pause()
    {
        Assert.Equal(
            CaptureStatusText.BufferPausedBattery,
            CaptureStatusText.Build(true, true, true, true, true));
    }
}
