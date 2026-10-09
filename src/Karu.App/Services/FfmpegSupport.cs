using System.Diagnostics;
using System.IO;
using Karu.Core;

namespace Karu.App.Services;

public static class FfmpegLocator
{
    public static string? Find()
    {
        var candidates = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, "ffmpeg", "ffmpeg.exe"),
            Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Karu",
                "ffmpeg",
                "ffmpeg.exe")
        };

        var pathDirs = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator);
        candidates.AddRange(pathDirs.Select(dir => Path.Combine(dir, "ffmpeg.exe")));

        return candidates.FirstOrDefault(File.Exists);
    }
}

public sealed record NvencProbeResult(bool Ok, string Message);

public static class NvencProbe
{
    public static NvencProbeResult Run(string ffmpegPath)
    {
        var start = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = FfmpegArgumentBuilder.BuildNvencProbeArgs(),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(start);
        if (process is null)
        {
            return new NvencProbeResult(false, "Impossible de lancer FFmpeg.");
        }

        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        if (!process.WaitForExit(10_000))
        {
            try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
            return new NvencProbeResult(false, "FFmpeg ne répond plus pendant la détection NVENC.");
        }

        var ok = output.Contains("h264_nvenc", StringComparison.OrdinalIgnoreCase);
        return ok
            ? new NvencProbeResult(true, "NVENC prêt")
            : new NvencProbeResult(false, "Encodeur h264_nvenc introuvable. Installe des drivers NVIDIA à jour.");
    }
}
