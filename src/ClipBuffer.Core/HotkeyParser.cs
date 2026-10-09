namespace ClipBuffer.Core;

public readonly record struct HotkeyChord(bool Ctrl, bool Alt, bool Shift, string Key);

public static class HotkeyParser
{
    public static HotkeyChord Parse(string text)
    {
        if (!TryParse(text, out var chord))
        {
            throw new FormatException($"Raccourci invalide : '{text}'.");
        }

        return chord;
    }

    public static bool TryParse(string text, out HotkeyChord chord)
    {
        chord = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        var ctrl = false;
        var alt = false;
        var shift = false;
        string? key = null;

        foreach (var raw in parts)
        {
            var token = raw.Trim();
            if (token.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("Control", StringComparison.OrdinalIgnoreCase))
            {
                ctrl = true;
                continue;
            }

            if (token.Equals("Alt", StringComparison.OrdinalIgnoreCase))
            {
                alt = true;
                continue;
            }

            if (token.Equals("Shift", StringComparison.OrdinalIgnoreCase))
            {
                shift = true;
                continue;
            }

            if (token.Equals("Win", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("Windows", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (key is not null)
            {
                return false;
            }

            key = NormalizeKey(token);
        }

        if (string.IsNullOrEmpty(key))
        {
            return false;
        }

        chord = new HotkeyChord(ctrl, alt, shift, key);
        return IsSupportedByRegisterHotKey(chord);
    }

    public static string ToDisplay(HotkeyChord chord)
    {
        var parts = new List<string>(4);
        if (chord.Ctrl) parts.Add("Ctrl");
        if (chord.Alt) parts.Add("Alt");
        if (chord.Shift) parts.Add("Shift");
        parts.Add(chord.Key);
        return string.Join("+", parts);
    }

    public static bool Matches(HotkeyChord chord, bool ctrl, bool alt, bool shift, string key) =>
        chord.Ctrl == ctrl &&
        chord.Alt == alt &&
        chord.Shift == shift &&
        string.Equals(chord.Key, NormalizeKey(key), StringComparison.OrdinalIgnoreCase);

    /// <summary>Touches supportées par RegisterHotKey (F1–F24, A–Z, 0–9).</summary>
    public static bool IsSupportedByRegisterHotKey(HotkeyChord chord)
    {
        var key = chord.Key;
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        if (key.Length >= 2 &&
            (key[0] == 'F' || key[0] == 'f') &&
            int.TryParse(key[1..], out var fn) &&
            fn is >= 1 and <= 24)
        {
            return true;
        }

        if (key.Length == 1)
        {
            var c = char.ToUpperInvariant(key[0]);
            return c is (>= '0' and <= '9') or (>= 'A' and <= 'Z');
        }

        return false;
    }

    private static string NormalizeKey(string token)
    {
        var trimmed = token.Trim();
        if (trimmed.Length == 1)
        {
            return trimmed.ToUpperInvariant();
        }

        if (trimmed.Length >= 2 &&
            (trimmed[0] == 'F' || trimmed[0] == 'f') &&
            int.TryParse(trimmed[1..], out var fn) &&
            fn is >= 1 and <= 24)
        {
            return "F" + fn;
        }

        // Refuse les tokens WPF type "Space" / "Oem*" pour RegisterHotKey.
        if (trimmed.Length > 1)
        {
            return trimmed; // sera rejeté par IsSupportedByRegisterHotKey
        }

        return char.ToUpperInvariant(trimmed[0]) + trimmed[1..].ToLowerInvariant();
    }
}
