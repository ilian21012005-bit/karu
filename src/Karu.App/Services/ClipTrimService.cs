using System.Diagnostics;
using Karu.Core;

namespace Karu.App.Services;

/// <summary>Trim post-clip via ffmpeg (export only — 0 coût en jeu).</summary>
public static class ClipTrimService
{
    public static async Task<(bool Ok, string? OutputPath, string? Error)> TrimAsync(
        string ffmpegPath,
        string inputPath,
        double startSeconds,
        double endSeconds,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ffmpegPath) || !File.Exists(ffmpegPath))
        {
            return (false, null, "FFmpeg introuvable.");
        }

        if (!File.Exists(inputPath))
        {
            return (false, null, "Clip introuvable.");
        }

        if (endSeconds - startSeconds < 0.2)
        {
            return (false, null, "Trim trop court (min 0,2 s).");
        }

        var dir = Path.GetDirectoryName(inputPath) ?? AppConfig.DefaultSaveDirectory();
        var stem = Path.GetFileNameWithoutExtension(inputPath);
        var output = ClipFileNamer.MakeFullPath(dir, DateTime.Now, $"{stem}_trim");

        var copyArgs = FfmpegArgumentBuilder.BuildTrimCopyArgs(inputPath, output, startSeconds, endSeconds);
        var copyOk = await RunFfmpegAsync(ffmpegPath, copyArgs, cancellationToken).ConfigureAwait(false);
        if (copyOk && File.Exists(output) && new FileInfo(output).Length > 1024)
        {
            return (true, output, null);
        }

        try
        {
            if (File.Exists(output))
            {
                File.Delete(output);
            }
        }
        catch
        {
            // ignore
        }

        var reArgs = FfmpegArgumentBuilder.BuildTrimReencodeArgs(inputPath, output, startSeconds, endSeconds);
        var reOk = await RunFfmpegAsync(ffmpegPath, reArgs, cancellationToken).ConfigureAwait(false);
        if (reOk && File.Exists(output) && new FileInfo(output).Length > 1024)
        {
            return (true, output, null);
        }

        return (false, null, "Échec du trim ffmpeg.");
    }

    private static async Task<bool> RunFfmpegAsync(string ffmpegPath, string args, CancellationToken token)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                return false;
            }

            ChildProcessJob.Assign(process);
            using var killOnCancel = token.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                    // ignore
                }
            });

            var errTask = process.StandardError.ReadToEndAsync(token);
            var outTask = process.StandardOutput.ReadToEndAsync(token);
            await process.WaitForExitAsync(token).ConfigureAwait(false);
            _ = await errTask.ConfigureAwait(false);
            _ = await outTask.ConfigureAwait(false);
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            AppLog.Write("trim ffmpeg: " + ex.Message);
            return false;
        }
    }
}
