namespace ClipBuffer.Core;

public static class ClipFileNamer
{
    public static string MakeFileName(DateTime when, string gameHint = "Valorant")
    {
        var hint = string.IsNullOrWhiteSpace(gameHint) ? "Clip" : gameHint.Trim();
        return $"{hint}_{when:yyyy-MM-dd_HH-mm-ss}.mp4";
    }

    public static string MakeFullPath(string directory, DateTime when, string gameHint = "Valorant") =>
        Path.Combine(directory, MakeFileName(when, gameHint));
}
