namespace Karu.Core;

/// <summary>Noms de process Valorant acceptés pour activer le buffer.</summary>
public static class GameProcessNames
{
    private static readonly HashSet<string> ValorantNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "VALORANT-Win64-Shipping",
        "VALORANT"
    };

    public static bool IsValorantProcess(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return false;
        }

        var name = processName.Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        return ValorantNames.Contains(name);
    }
}
