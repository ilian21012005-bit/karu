namespace ClipBuffer.Tests;

public class PcmMixerTests
{
    [Fact]
    public void Mixes_game_and_mic_sample_by_sample()
    {
        var dest = new short[2];
        var game = new short[] { 1000, 2000 };
        var mic = new short[] { 100, 200 };

        PcmMixer.MixInto(dest, game, mic, micGain: 0.5f);

        Assert.Equal(1050, dest[0]);
        Assert.Equal(2100, dest[1]);
    }

    [Fact]
    public void Clips_instead_of_wrapping()
    {
        var dest = new short[1];
        PcmMixer.MixInto(dest, new short[] { 30000 }, new short[] { 30000 }, micGain: 1f);
        Assert.Equal(short.MaxValue, dest[0]);
    }

    [Fact]
    public void Pads_missing_mic_with_game_only()
    {
        var dest = new short[2];
        PcmMixer.MixInto(dest, new short[] { 10, 20 }, ReadOnlySpan<short>.Empty, micGain: 0.7f);
        Assert.Equal(10, dest[0]);
        Assert.Equal(20, dest[1]);
    }
}
