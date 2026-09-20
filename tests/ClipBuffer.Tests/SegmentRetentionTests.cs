namespace ClipBuffer.Tests;

public class SegmentRetentionTests
{
    [Theory]
    [InlineData(15, 3)]
    [InlineData(16, 4)]
    [InlineData(300, 60)]
    [InlineData(5, 1)]
    public void MaxKeptSegments_covers_the_requested_duration(int bufferSeconds, int expected)
    {
        Assert.Equal(expected, SegmentRetention.MaxKeptSegments(bufferSeconds));
    }

    [Fact]
    public void FilesToSave_takes_the_oldest_complete_segments_covering_the_buffer()
    {
        var files = Enumerable.Range(0, 10)
            .Select(i => $@"C:\buf\seg_{i:D5}.ts")
            .ToArray();

        var selected = SegmentRetention.FilesToSave(files, bufferSeconds: 15, currentlyWriting: @"C:\buf\seg_00009.ts");

        Assert.Equal(
            new[]
            {
                @"C:\buf\seg_00006.ts",
                @"C:\buf\seg_00007.ts",
                @"C:\buf\seg_00008.ts"
            },
            selected);
    }

    [Fact]
    public void FilesToSave_uses_all_complete_files_when_buffer_is_not_full_yet()
    {
        var files = new[]
        {
            @"C:\buf\seg_00000.ts",
            @"C:\buf\seg_00001.ts",
            @"C:\buf\seg_00002.ts"
        };

        var selected = SegmentRetention.FilesToSave(files, bufferSeconds: 300, currentlyWriting: @"C:\buf\seg_00002.ts");

        Assert.Equal(
            new[] { @"C:\buf\seg_00000.ts", @"C:\buf\seg_00001.ts" },
            selected);
    }

    [Fact]
    public void FilesToDelete_drops_complete_files_beyond_the_retention_window()
    {
        var files = Enumerable.Range(0, 8)
            .Select(i => $@"C:\buf\seg_{i:D5}.ts")
            .ToArray();

        var doomed = SegmentRetention.FilesToDelete(files, bufferSeconds: 15, currentlyWriting: @"C:\buf\seg_00007.ts");

        Assert.Equal(
            new[]
            {
                @"C:\buf\seg_00000.ts",
                @"C:\buf\seg_00001.ts",
                @"C:\buf\seg_00002.ts",
                @"C:\buf\seg_00003.ts"
            },
            doomed);
    }

    [Fact]
    public void FilesToDelete_never_removes_the_file_currently_being_written()
    {
        var files = new[] { @"C:\buf\seg_00000.ts" };
        var doomed = SegmentRetention.FilesToDelete(files, bufferSeconds: 15, currentlyWriting: @"C:\buf\seg_00000.ts");
        Assert.Empty(doomed);
    }
}
