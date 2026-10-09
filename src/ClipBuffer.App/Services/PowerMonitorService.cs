using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ClipBuffer.App.Services;

/// <summary>Détecte secteur / batterie pour suspendre le buffer si demandé.</summary>
public sealed class PowerMonitorService : IDisposable
{
    private bool _disposed;
    private bool _onBattery;

    public bool IsOnBattery => _onBattery;

    public event Action<bool>? PowerSourceChanged;

    public void Start()
    {
        _onBattery = QueryOnBattery();
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode is not (PowerModes.StatusChange or PowerModes.Resume or PowerModes.Suspend))
        {
            return;
        }

        var battery = QueryOnBattery();
        if (battery == _onBattery)
        {
            return;
        }

        _onBattery = battery;
        PowerSourceChanged?.Invoke(battery);
    }

    private static bool QueryOnBattery()
    {
        if (!GetSystemPowerStatus(out var status))
        {
            return false;
        }

        // ACLineStatus: 0 = battery, 1 = AC, 255 = unknown
        return status.ACLineStatus == 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus sps);
}
