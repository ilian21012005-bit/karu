namespace Karu.Core;

public static class ClipFileNamer
{
    public static string MakeHint(ClipTag tag, string? playerName, string gameFallback = "Valorant")
    {
        var tagPart = tag switch
        {
            ClipTag.Triple => "Triple",
            ClipTag.Quad => "Quad",
            ClipTag.Ace => "Ace",
            _ => string.IsNullOrWhiteSpace(gameFallback) ? "Clip" : gameFallback.Trim()
        };

        var player = SanitizeFileToken(playerName);
        return string.IsNullOrEmpty(player) ? SanitizeFileToken(tagPart) : $"{SanitizeFileToken(tagPart)}_{player}";
    }

    public static string MakeFileName(DateTime when, string gameHint = "Valorant")
    {
        var hint = SanitizeFileToken(string.IsNullOrWhiteSpace(gameHint) ? "Clip" : gameHint.Trim());
        return $"{hint}_{when:yyyy-MM-dd_HH-mm-ss}.mp4";
    }

    public static string MakeFileName(DateTime when, ClipTag tag, string? playerName, string gameFallback = "Valorant") =>
        MakeFileName(when, MakeHint(tag, playerName, gameFallback));

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

    public static string MakeFullPath(
        string directory,
        DateTime when,
        ClipTag tag,
        string? playerName,
        string gameFallback = "Valorant") =>
        MakeFullPath(directory, when, MakeHint(tag, playerName, gameFallback));

    private static string SanitizeFileToken(string? value)
    {
        var hint = (value ?? "").Trim();
        if (hint.Length == 0)
        {
            return "";
        }

        foreach (var c in Path.GetInvalidFileNameChars())
        {
            hint = hint.Replace(c, '_');
        }

        return hint.Replace(' ', '_');
    }
}
