using Microsoft.Win32;

namespace ClipBuffer.App.Services;

/// <summary>Démarrage avec Windows via HKCU (aucun droit admin).</summary>
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Karu";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return key?.GetValue(ValueName) is string;
        }
        catch
        {
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                        ?? Registry.CurrentUser.CreateSubKey(RunKey);
        if (key is null)
        {
            throw new InvalidOperationException("Impossible d'accéder au registre de démarrage.");
        }

        if (enabled)
        {
            var exe = Environment.ProcessPath
                      ?? Path.Combine(AppContext.BaseDirectory, "Karu.exe");
            key.SetValue(ValueName, $"\"{exe}\"");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
