namespace Karu.Core;

public static class SegmentRetention
{
    public const int SegmentSeconds = 5;

    public static int MaxKeptSegments(int bufferSeconds) =>
        Math.Max(1, (int)Math.Ceiling(bufferSeconds / (double)SegmentSeconds));

    public static IReadOnlyList<string> FilesToSave(
        IReadOnlyList<string> existingFiles,
        int bufferSeconds,
        string? currentlyWriting = null)
    {
        var complete = CompleteFilesSorted(existingFiles, currentlyWriting);
        var keep = MaxKeptSegments(bufferSeconds);
        if (complete.Count <= keep)
        {
            return complete;
        }

        return complete.Skip(complete.Count - keep).ToArray();
    }

    public static IReadOnlyList<string> FilesToDelete(
        IReadOnlyList<string> existingFiles,
        int bufferSeconds,
        string? currentlyWriting = null)
    {
        var complete = CompleteFilesSorted(existingFiles, currentlyWriting);
        var keep = MaxKeptSegments(bufferSeconds);
        if (complete.Count <= keep)
        {
            return Array.Empty<string>();
        }

        return complete.Take(complete.Count - keep).ToArray();
    }

    private static List<string> CompleteFilesSorted(IReadOnlyList<string> existingFiles, string? currentlyWriting)
    {
        var writingName = currentlyWriting is null ? null : Path.GetFileName(currentlyWriting);
        return existingFiles
            .Where(path => writingName is null || !string.Equals(Path.GetFileName(path), writingName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
