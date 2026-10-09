namespace Karu.Core;

public static class FfmpegArgumentBuilder
{
    public static string BuildCaptureArgs(string segmentDirectory, int outputIndex = 0, int fps = 60)
    {
        var pattern = Path.Combine(segmentDirectory, "seg_%05d.ts");
        return string.Join(' ',
            "-hide_banner",
            "-loglevel warning",
            "-fflags +genpts",
            "-f lavfi",
            $"-i \"ddagrab=output_idx={outputIndex}:framerate={fps}\"",
            "-f s16le -ar 48000 -ac 2 -i pipe:0",
            "-map 0:v:0 -map 1:a:0",
            "-c:v h264_nvenc -preset p1 -tune ll -rc constqp -qp 28 -bf 0 -g 30",
            "-c:a aac -b:a 160k -ar 48000 -ac 2",
            "-f segment",
            $"-segment_time {SegmentRetention.SegmentSeconds}",
            "-reset_timestamps 1",
            "-segment_format mpegts",
            $"\"{pattern}\"");
    }

    public static string BuildConcatArgs(string concatListPath, string outputPath) =>
        string.Join(' ',
            "-hide_banner",
            "-loglevel warning",
            "-y",
            "-f concat",
            "-safe 0",
            $"-i \"{concatListPath}\"",
            "-map 0:v:0 -map 0:a:0?",
            "-c copy",
            // AAC en MPEG-TS → MP4 : requis pour MediaElement / Media Foundation
            "-bsf:a aac_adtstoasc",
            "-movflags +faststart",
            $"\"{outputPath}\"");

    public static string BuildNvencProbeArgs() => "-hide_banner -encoders";

    public static string BuildConcatList(IEnumerable<string> segmentPaths) =>
        string.Join(
            "\n",
            segmentPaths.Select(path =>
            {
                var slashPath = path.Replace('\\', '/');
                return $"file '{slashPath.Replace("'", @"'\''")}'";
            }));
}
