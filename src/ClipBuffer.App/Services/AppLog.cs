using System.Diagnostics;
using System.IO;

namespace ClipBuffer.App.Services;

internal static class AppLog
{
    private static readonly string PathName = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ClipBuffer",
        "log.txt");

    [Conditional("DEBUG")]
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
