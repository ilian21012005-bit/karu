namespace ClipBuffer.Core;

public static class PcmMixer
{
    public static void MixInto(Span<short> dest, ReadOnlySpan<short> game, ReadOnlySpan<short> mic, float micGain = 0.7f)
    {
        var count = dest.Length;
        for (var i = 0; i < count; i++)
        {
            var gameSample = i < game.Length ? game[i] : (short)0;
            var micSample = i < mic.Length ? mic[i] : (short)0;
            var mixed = gameSample + (micSample * micGain);
            dest[i] = ClampToShort(mixed);
        }
    }

    private static short ClampToShort(float value)
    {
        if (value > short.MaxValue) return short.MaxValue;
        if (value < short.MinValue) return short.MinValue;
        return (short)Math.Round(value);
    }
}
