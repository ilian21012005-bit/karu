namespace Karu.Core;

public static class ClipFileNamer
{
    public static string MakeFileName(DateTime when, string gameHint = "Valorant")
    {
        var hint = string.IsNullOrWhiteSpace(gameHint) ? "Clip" : gameHint.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            hint = hint.Replace(c, '_');
        }

        return $"{hint}_{when:yyyy-MM-dd_HH-mm-ss}.mp4";
    }

    public static string MakeFullPath(string directory, DateTime when, string gameHint = "Valorant")
    {
        var path = Path.Combine(directory, MakeFileName(when, gameHint));
        if (!File.Exists(path))
        {
            return path;
        }

        var dir = Path.GetDirectoryName(path)!;
        var stem = Path.GetFileNameWithoutExtension(path);
        for (var i = 2; i < 1000; i++)
        {
            var candidate = Path.Combine(dir, $"{stem}_{i}.mp4");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(dir, $"{stem}_{Guid.NewGuid():N}.mp4");
    }
}
