using System.Diagnostics;
using System.Runtime.InteropServices;
using Karu.Core;

namespace Karu.App.Services;

/// <summary>
/// Détecte si la fenêtre au premier plan appartient à Valorant.
/// Hook EVENT_SYSTEM_FOREGROUND — coût quasi nul hors alt-tab.
/// </summary>
public sealed class GameForegroundMonitor : IDisposable
{
    private const uint EventSystemForeground = 0x0003;
    private const uint WineventOutofcontext = 0;
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(400);

    private readonly object _gate = new();
    private WinEventDelegate? _callback;
    private IntPtr _hook;
    private bool _disposed;
    private bool _started;
    private bool _isGameFocused;
    private int _cachedPid = -1;
    private bool _cachedIsGame;
    private CancellationTokenSource? _debounceCts;

    public bool IsGameFocused
    {
        get
        {
            lock (_gate)
            {
                return _isGameFocused;
            }
        }
    }

    public event Action<bool>? Changed;

    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started)
            {
                return;
            }

            _callback = OnWinEvent;
            _hook = SetWinEventHook(
                EventSystemForeground,
                EventSystemForeground,
                IntPtr.Zero,
                _callback,
                0,
                0,
                WineventOutofcontext);

            if (_hook == IntPtr.Zero)
            {
                AppLog.Write("GameForegroundMonitor: SetWinEventHook failed");
            }

            _started = true;
        }

        // État initial (pas seulement au prochain alt-tab)
        ApplyForeground(immediate: true);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _started = false;
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = null;

            if (_hook != IntPtr.Zero)
            {
                UnhookWinEvent(_hook);
                _hook = IntPtr.Zero;
            }

            _callback = null;
        }
    }

    private void OnWinEvent(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime)
    {
        if (eventType != EventSystemForeground)
        {
            return;
        }

        ApplyForeground(immediate: false);
    }

    private void ApplyForeground(bool immediate)
    {
        CancellationToken token;
        lock (_gate)
        {
            if (_disposed || !_started)
            {
                return;
            }

            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = new CancellationTokenSource();
            token = _debounceCts.Token;
        }

        if (immediate)
        {
            Publish(EvaluateIsGameFocused());
            return;
        }

        _ = DebounceThenPublishAsync(token);
    }

    private async Task DebounceThenPublishAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(Debounce, token).ConfigureAwait(false);
            if (token.IsCancellationRequested)
            {
                return;
            }

            Publish(EvaluateIsGameFocused());
        }
        catch (OperationCanceledException)
        {
            // nouveau focus avant fin du debounce
        }
    }

    private void Publish(bool focused)
    {
        bool changed;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            changed = focused != _isGameFocused;
            _isGameFocused = focused;
        }

        if (changed)
        {
            AppLog.Write("game focus=" + focused);
            Changed?.Invoke(focused);
        }
    }

    private bool EvaluateIsGameFocused()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        _ = GetWindowThreadProcessId(hwnd, out var pidU);
        if (pidU == 0 || pidU > int.MaxValue)
        {
            return false;
        }

        var pid = (int)pidU;
        lock (_gate)
        {
            if (pid == _cachedPid)
            {
                return _cachedIsGame;
            }
        }

        var isGame = false;
        try
        {
            using var process = Process.GetProcessById(pid);
            isGame = GameProcessNames.IsValorantProcess(process.ProcessName);
        }
        catch
        {
            isGame = false;
        }

        lock (_gate)
        {
            _cachedPid = pid;
            _cachedIsGame = isGame;
        }

        return isGame;
    }

    private delegate void WinEventDelegate(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(
        uint eventMin,
        uint eventMax,
        IntPtr hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc,
        uint idProcess,
        uint idThread,
        uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);
}
