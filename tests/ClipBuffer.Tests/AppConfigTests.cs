namespace ClipBuffer.Tests;

public class AppConfigTests
{
    [Fact]
    public void Defaults_match_the_product_spec()
    {
        var config = AppConfig.CreateDefault();

        Assert.Equal(300, config.BufferSeconds);
        Assert.Equal("F9", config.Hotkey);
        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "ClipBuffer"),
            config.SaveDirectory);
    }

    [Theory]
    [InlineData(10, 15)]
    [InlineData(15, 15)]
    [InlineData(300, 300)]
    [InlineData(600, 600)]
    [InlineData(9999, 600)]
    public void Clamp_keeps_buffer_between_15s_and_10min(int input, int expected)
    {
        var config = AppConfig.CreateDefault();
        config.BufferSeconds = input;
        config.Clamp();
        Assert.Equal(expected, config.BufferSeconds);
    }

    [Fact]
    public void Save_then_load_roundtrips_settings()
    {
        var path = Path.Combine(Path.GetTempPath(), "clipbuffer-tests", Guid.NewGuid().ToString("N"), "config.json");
        var original = new AppConfig
        {
            BufferSeconds = 120,
            Hotkey = "Ctrl+F8",
            SaveDirectory = @"D:\Clips",
            AutoTriple = true,
            AutoAce = true,
            PlayerName = "ilian",
            HighlightCooldownSeconds = 20,
            HighlightsEnabled = true
        };

        ConfigStore.Save(path, original);
        var loaded = ConfigStore.Load(path);

        Assert.Equal(120, loaded.BufferSeconds);
        Assert.Equal("Ctrl+F8", loaded.Hotkey);
        Assert.Equal(@"D:\Clips", loaded.SaveDirectory);
        Assert.True(loaded.AutoTriple);
        Assert.True(loaded.AutoAce);
        Assert.Equal("ilian", loaded.PlayerName);
        Assert.Equal(20, loaded.HighlightCooldownSeconds);
        Assert.True(loaded.HighlightsEnabled);
    }

    [Fact]
    public void Load_missing_file_returns_defaults()
    {
        var path = Path.Combine(Path.GetTempPath(), "clipbuffer-tests", Guid.NewGuid().ToString("N"), "missing.json");
        var loaded = ConfigStore.Load(path);

        Assert.Equal(300, loaded.BufferSeconds);
        Assert.Equal("F9", loaded.Hotkey);
    }

    [Fact]
    public void DefaultPath_is_under_appdata_ClipBuffer()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClipBuffer",
            "config.json");
        Assert.Equal(expected, ConfigStore.DefaultPath);
    }
}
