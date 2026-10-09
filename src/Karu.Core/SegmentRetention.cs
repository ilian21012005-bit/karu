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

    /// <summary>
    /// Segments qui chevauchent [windowStart, windowEnd].
    /// Fin de segment ≈ LastWriteTimeUtc (ou getSegmentEndUtc en test) ; durée = SegmentSeconds.
    /// </summary>
    public static IReadOnlyList<string> FilesToSaveInWindow(
        IReadOnlyList<string> existingFiles,
        DateTime windowStartUtc,
        DateTime windowEndUtc,
        string? currentlyWriting = null,
        Func<string, DateTime>? getSegmentEndUtc = null)
    {
        if (windowEndUtc < windowStartUtc)
        {
            (windowStartUtc, windowEndUtc) = (windowEndUtc, windowStartUtc);
        }

        getSegmentEndUtc ??= static path => File.GetLastWriteTimeUtc(path);
        var complete = CompleteFilesSorted(existingFiles, currentlyWriting);
        var selected = new List<string>(complete.Count);
        var duration = TimeSpan.FromSeconds(SegmentSeconds);

        foreach (var path in complete)
        {
            DateTime endUtc;
            try
            {
                endUtc = getSegmentEndUtc(path);
            }
            catch
            {
                continue;
            }

            var startUtc = endUtc - duration;
            if (startUtc <= windowEndUtc && endUtc >= windowStartUtc)
            {
                selected.Add(path);
            }
        }

        return selected;
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
