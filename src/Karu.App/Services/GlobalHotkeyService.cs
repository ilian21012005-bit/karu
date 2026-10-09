using System.Runtime.InteropServices;
using System.Windows.Interop;
using Karu.Core;

namespace Karu.App.Services;

/// <summary>
/// Raccourci global via RegisterHotKey (pas de WH_KEYBOARD_LL / injection).
/// </summary>
public sealed class GlobalHotkeyService : IDisposable
{
    private const int HotkeyId = 0x4B52; // 'KR'
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModNoRepeat = 0x4000;

    private HwndSource? _source;
    private bool _registered;
    private bool _disposed;
    private HotkeyChord _chord = HotkeyParser.Parse(AppConfig.DefaultHotkey);

    public HotkeyChord Chord => _chord;

    /// <summary>Change le raccourci ; en cas d'échec, conserve l'ancien.</summary>
    public void SetChord(HotkeyChord chord)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_source is null)
        {
            if (!HotkeyParser.IsSupportedByRegisterHotKey(chord))
            {
                throw new InvalidOperationException("Raccourci non supporté (F1–F24, A–Z, 0–9).");
            }

            _chord = chord;
            return;
        }

        ApplyChord(chord);
    }

    public event Action? Triggered;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_source is not null)
        {
            return;
        }

        var parameters = new HwndSourceParameters("KaruHotkeySink")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
            ParentWindow = new IntPtr(-3) // HWND_MESSAGE
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
        ApplyChord(_chord);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Unregister();
        if (_source is not null)
        {
            _source.RemoveHook(WndProc);
            _source.Dispose();
            _source = null;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            Triggered?.Invoke();
            handled = true;
        }

        return IntPtr.Zero;
    }

    private void ApplyChord(HotkeyChord chord)
    {
        if (_source is null)
        {
            _chord = chord;
            return;
        }

        if (!TryMap(chord, out var modifiers, out var vk))
        {
            throw new InvalidOperationException("Raccourci non supporté (F1–F24, A–Z, 0–9).");
        }

        var previous = _chord;
        Unregister();

        if (!RegisterHotKey(_source.Handle, HotkeyId, modifiers, vk))
        {
            var err = Marshal.GetLastWin32Error();
            // Restaure l'ancien raccourci.
            if (TryMap(previous, out var prevMod, out var prevVk))
            {
                if (RegisterHotKey(_source.Handle, HotkeyId, prevMod, prevVk))
                {
                    _registered = true;
                    _chord = previous;
                }
            }

            throw new InvalidOperationException(
                err == 1409
                    ? "Ce raccourci est déjà utilisé par une autre application."
                    : "Impossible d'enregistrer le raccourci global.");
        }

        _chord = chord;
        _registered = true;
    }

    private void Unregister()
    {
        if (_registered && _source is not null)
        {
            UnregisterHotKey(_source.Handle, HotkeyId);
            _registered = false;
        }
    }

    internal static bool TryMap(HotkeyChord chord, out uint modifiers, out uint vk)
    {
        modifiers = ModNoRepeat;
        if (chord.Ctrl) modifiers |= ModControl;
        if (chord.Alt) modifiers |= ModAlt;
        if (chord.Shift) modifiers |= ModShift;

        vk = KeyToVk(chord.Key);
        return vk != 0;
    }

    private static uint KeyToVk(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return 0;
        }

        if (key.Length >= 2 &&
            (key[0] == 'F' || key[0] == 'f') &&
            int.TryParse(key[1..], out var fn) &&
            fn is >= 1 and <= 24)
        {
            return (uint)(0x70 + fn - 1);
        }

        if (key.Length == 1)
        {
            var c = char.ToUpperInvariant(key[0]);
            if (c is >= '0' and <= '9') return c;
            if (c is >= 'A' and <= 'Z') return c;
        }

        return 0;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
