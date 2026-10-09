namespace Karu.Tests;

public class FfmpegArgumentBuilderTests
{
    [Fact]
    public void Capture_uses_ddagrab_nvenc_and_mpegts_segments()
    {
        var args = FfmpegArgumentBuilder.BuildCaptureArgs(@"C:\buf", outputIndex: 0, fps: 60);

        Assert.Contains("ddagrab=", args);
        Assert.Contains("output_idx=0", args);
        Assert.Contains("framerate=60", args);
        Assert.Contains("h264_nvenc", args);
        Assert.Contains("preset p1", args);
        Assert.Contains("-f s16le", args);
        Assert.Contains("-i pipe:0", args);
        Assert.Contains("-f segment", args);
        Assert.Contains("-segment_time 5", args);
        Assert.Contains("mpegts", args);
        Assert.Contains(@"C:\buf\seg_%05d.ts", args);
    }

    [Fact]
    public void Concat_copies_without_reencoding()
    {
        var args = FfmpegArgumentBuilder.BuildConcatArgs(@"C:\tmp\list.txt", @"D:\Clips\out.mp4");
        Assert.Contains("-f concat", args);
        Assert.Contains("-safe 0", args);
        Assert.Contains(@"C:\tmp\list.txt", args);
        Assert.Contains("-c copy", args);
        Assert.Contains("aac_adtstoasc", args);
        Assert.Contains("faststart", args);
        Assert.Contains(@"D:\Clips\out.mp4", args);
    }

    [Fact]
    public void Probe_lists_encoders()
    {
        var args = FfmpegArgumentBuilder.BuildNvencProbeArgs();
        Assert.Contains("-encoders", args);
    }
}
