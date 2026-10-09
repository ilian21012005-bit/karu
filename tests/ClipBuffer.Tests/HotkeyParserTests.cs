namespace ClipBuffer.Tests;

public class HotkeyParserTests
{
    [Fact]
    public void Parses_plain_function_key()
    {
        var chord = HotkeyParser.Parse("F9");
        Assert.False(chord.Ctrl);
        Assert.False(chord.Alt);
        Assert.False(chord.Shift);
        Assert.Equal("F9", chord.Key);
    }

    [Fact]
    public void Parses_modifiers_case_insensitively()
    {
        var chord = HotkeyParser.Parse("ctrl+shift+f8");
        Assert.True(chord.Ctrl);
        Assert.True(chord.Shift);
        Assert.False(chord.Alt);
        Assert.Equal("F8", chord.Key);
    }

    [Fact]
    public void ToDisplay_is_stable_and_roundtrips()
    {
        var chord = HotkeyParser.Parse("Alt+F10");
        Assert.Equal("Alt+F10", HotkeyParser.ToDisplay(chord));
        Assert.Equal(chord, HotkeyParser.Parse(HotkeyParser.ToDisplay(chord)));
    }

    [Fact]
    public void Matches_requires_the_same_modifiers_and_key()
    {
        var chord = HotkeyParser.Parse("Ctrl+F9");
        Assert.True(HotkeyParser.Matches(chord, ctrl: true, alt: false, shift: false, key: "F9"));
        Assert.False(HotkeyParser.Matches(chord, ctrl: false, alt: false, shift: false, key: "F9"));
        Assert.False(HotkeyParser.Matches(chord, ctrl: true, alt: false, shift: false, key: "F8"));
    }

    [Fact]
    public void TryParse_rejects_empty_and_unknown_tokens()
    {
        Assert.False(HotkeyParser.TryParse("", out _));
        Assert.False(HotkeyParser.TryParse("Ctrl+", out _));
        Assert.False(HotkeyParser.TryParse("Windows+F9", out _));
        Assert.False(HotkeyParser.TryParse("Space", out _));
        Assert.False(HotkeyParser.TryParse("OemPlus", out _));
    }

    [Fact]
    public void IsSupported_accepts_function_and_alnum_keys()
    {
        Assert.True(HotkeyParser.IsSupportedByRegisterHotKey(HotkeyParser.Parse("F9")));
        Assert.True(HotkeyParser.IsSupportedByRegisterHotKey(HotkeyParser.Parse("Ctrl+A")));
        Assert.False(HotkeyParser.IsSupportedByRegisterHotKey(new HotkeyChord(false, false, false, "Space")));
    }
}
