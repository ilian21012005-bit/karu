namespace ClipBuffer.Core;

/// <summary>Validation de chemins pour éviter traversal / écriture hors racine.</summary>
public static class PathSafety
{
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public static string NormalizeDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Chemin de dossier vide.", nameof(path));
        }

        var full = Path.GetFullPath(path.Trim());
        if (full.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            throw new ArgumentException("Chemin de dossier invalide.", nameof(path));
        }

        return full;
    }

    public static bool IsUnderRoot(string rootDirectory, string candidatePath)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory) || string.IsNullOrWhiteSpace(candidatePath))
        {
            return false;
        }

        string root;
        string candidate;
        try
        {
            root = Path.GetFullPath(rootDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            candidate = Path.GetFullPath(candidatePath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return false;
        }

        if (string.Equals(root, candidate, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var prefix = root + Path.DirectorySeparatorChar;
        return candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    public static void EnsureUnderRoot(string rootDirectory, string candidatePath, string paramName = "path")
    {
        if (!IsUnderRoot(rootDirectory, candidatePath))
        {
            throw new UnauthorizedAccessException(
                $"Chemin hors du dossier autorisé ({paramName}).");
        }
    }

    public static string SanitizeFolderName(string name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            trimmed = trimmed.Replace(c, '_');
        }

        trimmed = trimmed.Replace("..", "_", StringComparison.Ordinal);
        trimmed = trimmed.Trim('.', ' ', '\t');
        if (trimmed.Length > 64)
        {
            trimmed = trimmed[..64].TrimEnd('.', ' ');
        }

        if (ReservedNames.Contains(trimmed))
        {
            trimmed = "_" + trimmed;
        }

        return trimmed;
    }

    public static bool TryEnsureWritableDirectory(string path, out string fullPath, out string? error)
    {
        fullPath = "";
        error = null;
        try
        {
            fullPath = NormalizeDirectory(path);
            Directory.CreateDirectory(fullPath);

            var probe = Path.Combine(fullPath, $".karu_write_{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex)
        {
            error = FriendlyIoMessage(ex);
            return false;
        }
    }

    public static string FriendlyIoMessage(Exception ex) => ex switch
    {
        UnauthorizedAccessException => "Accès refusé au dossier. Choisis un autre emplacement.",
        DirectoryNotFoundException => "Dossier introuvable. Il a peut-être été supprimé.",
        DriveNotFoundException => "Disque introuvable.",
        PathTooLongException => "Chemin trop long.",
        IOException io when IsDiskFull(io) => "Disque plein. Libère de l'espace puis réessaie.",
        IOException => "Erreur d'écriture disque. Vérifie le dossier et l'espace disponible.",
        ArgumentException => "Chemin invalide.",
        _ => ex.Message
    };

    private static bool IsDiskFull(IOException ex)
    {
        const int errorHandleDiskFull = unchecked((int)0x80070027);
        const int errorDiskFull = unchecked((int)0x80070070);
        return ex.HResult is errorHandleDiskFull or errorDiskFull
               || ex.Message.Contains("disk", StringComparison.OrdinalIgnoreCase)
               || ex.Message.Contains("plein", StringComparison.OrdinalIgnoreCase);
    }
}
