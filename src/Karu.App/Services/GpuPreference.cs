using Microsoft.Win32;

namespace Karu.App.Services;

/// <summary>
/// Demande le GPU haute perf (NVIDIA dédié) via les préférences Windows DirectX.
/// Pas de drivers embarqués — Optimus / Windows Graphics Settings.
/// </summary>
public static class GpuPreference
{
    private const string KeyPath = @"Software\Microsoft\DirectX\UserGpuPreferences";

    public static void PreferHighPerformanceGpu()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
            {
                return;
            }

            using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
            key?.SetValue(exe, "GpuPreference=2;", RegistryValueKind.String);
        }
        catch (Exception ex)
        {
            AppLog.Write("gpu preference: " + ex.Message);
        }
    }
}
