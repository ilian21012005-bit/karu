namespace Karu.Tests;

public class FfmpegTrimArgsTests
{
    [Fact]
    public void Trim_copy_args_include_ss_and_duration()
    {
        var args = FfmpegArgumentBuilder.BuildTrimCopyArgs(@"C:\a.mp4", @"C:\b.mp4", 1.5, 4.5);
        Assert.Contains("-ss", args);
        Assert.Contains("1.5", args);
        Assert.Contains("-t", args);
        Assert.Contains("3", args);
        Assert.Contains("-c copy", args);
    }
}
