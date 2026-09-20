using System.Runtime.InteropServices;
using ClipBuffer.Core;

namespace ClipBuffer.App.Services;

public sealed class GlobalHotkeyService : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeydown = 0x0100;
    private const int WmSyskeydown = 0x0104;
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int KeyPressed = 0x8000;

    private readonly LowLevelKeyboardProc _proc;
    private IntPtr _hook;
    private bool _disposed;

    public GlobalHotkeyService()
    {
        _proc = HookCallback;
    }

    public HotkeyChord Chord { get; set; } = HotkeyParser.Parse(AppConfig.DefaultHotkey);

    public event Action? Triggered;

    public void Start()
    {
        if (_hook != IntPtr.Zero)
        {
            return;
        }

        _hook = SetWindowsHookEx(WhKeyboardLl, _proc, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
        {
            throw new InvalidOperationException("Impossible d'installer le raccourci global.");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == WmKeydown || wParam == (IntPtr)WmSyskeydown))
        {
            var vk = Marshal.ReadInt32(lParam);
            var keyName = VkToKeyName(vk);
            var ctrl = (GetKeyState(VkControl) & KeyPressed) != 0;
            var alt = (GetKeyState(VkMenu) & KeyPressed) != 0;
            var shift = (GetKeyState(VkShift) & KeyPressed) != 0;

            if (keyName is not null && HotkeyParser.Matches(Chord, ctrl, alt, shift, keyName))
            {
                Triggered?.Invoke();
                return (IntPtr)1;
            }
        }

        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private static string? VkToKeyName(int vk)
    {
        if (vk is >= 0x70 and <= 0x87)
        {
            return "F" + (vk - 0x70 + 1);
        }

        if (vk is >= 0x30 and <= 0x39)
        {
            return ((char)vk).ToString();
        }

        if (vk is >= 0x41 and <= 0x5A)
        {
            return ((char)vk).ToString();
        }

        return null;
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);
}
