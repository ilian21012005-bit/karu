using System.IO;

namespace Karu.App.Services;

internal static class AppLog
{
    private static readonly string PathName = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Karu",
        "log.txt");

    /// <summary>Toujours écrit (Release inclus) — utile pour diagnostiquer les crashs de lancement.</summary>
    public static void Write(string message) => WriteAlways(message);

    public static void WriteAlways(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
            File.AppendAllText(PathName, DateTime.Now.ToString("s") + " " + message + Environment.NewLine);
        }
        catch
        {
            // ignore
        }
    }
}
