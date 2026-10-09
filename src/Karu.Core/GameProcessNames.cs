using System.Diagnostics;

namespace Karu.Core;

/// <summary>Noms de process Valorant acceptés pour activer le buffer.</summary>
public static class GameProcessNames
{
    private static readonly string[] ValorantProcessNames =
    [
        "VALORANT-Win64-Shipping",
        "VALORANT"
    ];

    private static readonly HashSet<string> ValorantNames = new(ValorantProcessNames, StringComparer.OrdinalIgnoreCase);

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

    /// <summary>Vrai si le client jeu Valorant tourne (pas le launcher seul).</summary>
    public static bool IsValorantRunning()
    {
        foreach (var name in ValorantProcessNames)
        {
            Process[] list;
            try
            {
                list = Process.GetProcessesByName(name);
            }
            catch
            {
                continue;
            }

            try
            {
                if (list.Length > 0)
                {
                    return true;
                }
            }
            finally
            {
                foreach (var p in list)
                {
                    p.Dispose();
                }
            }
        }

        return false;
    }
}
