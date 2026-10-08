using System.Diagnostics;
using ClipBuffer.Core;

namespace ClipBuffer.App.Services;

public sealed class ThumbnailService
{
    private readonly string _ffmpegPath;
    private readonly ClipLibrary _library = new();

    public ThumbnailService(string ffmpegPath)
    {
        _ffmpegPath = ffmpegPath;
    }

    public Task GenerateAsync(string videoPath, CancellationToken cancellationToken = default) =>
        Task.Run(() => Generate(videoPath), cancellationToken);

    private void Generate(string videoPath)
    {
        try
        {
            if (!File.Exists(videoPath) || !File.Exists(_ffmpegPath))
            {
                return;
            }

            Thread.CurrentThread.Priority = ThreadPriority.Lowest;
            var thumb = Path.ChangeExtension(videoPath, ".jpg");
            var start = new ProcessStartInfo
            {
                FileName = _ffmpegPath,
                Arguments = $"-hide_banner -loglevel error -y -ss 1 -i \"{videoPath}\" -frames:v 1 -q:v 5 \"{thumb}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };

            using var process = Process.Start(start);
            if (process is null)
            {
                return;
            }

            process.WaitForExit(20_000);
            if (process.ExitCode == 0 && File.Exists(thumb))
            {
                _library.SetThumbnail(videoPath, thumb);
            }
        }
        catch
        {
            // never break the app for thumbnails
        }
    }
}
