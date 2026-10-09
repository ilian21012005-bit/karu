namespace Karu.Tests;

public class ConcatListTests
{
    [Fact]
    public void Emits_ffmpeg_concat_lines_with_forward_slashes_and_no_bom_chars()
    {
        var text = FfmpegArgumentBuilder.BuildConcatList(new[]
        {
            @"C:\buf\seg_00000.ts",
            @"C:\buf\seg_00001.ts"
        });

        Assert.Equal("file 'C:/buf/seg_00000.ts'\nfile 'C:/buf/seg_00001.ts'", text);
        Assert.DoesNotContain('\uFEFF', text);
    }
}
