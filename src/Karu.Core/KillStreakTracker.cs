namespace Karu.Core;

public enum KillfeedEventKind
{
    None,
    LocalKill,
    LocalDeath
}

public readonly record struct KillfeedEvent(KillfeedEventKind Kind, string RawLine);

public static class KillfeedLineParser
{
    /// <summary>
    /// Parse a killfeed line. Valorant shows "Killer  Weapon  Victim".
    /// If playerName appears as killer (left side) → LocalKill.
    /// If playerName appears as victim (right side) → LocalDeath.
    /// </summary>
    public static KillfeedEvent Parse(string line, string playerName)
    {
        if (string.IsNullOrWhiteSpace(line) || string.IsNullOrWhiteSpace(playerName))
        {
            return new KillfeedEvent(KillfeedEventKind.None, line ?? "");
        }

        var cleaned = CollapseSpaces(line.Trim());
        var player = playerName.Trim();
        if (cleaned.Length == 0)
        {
            return new KillfeedEvent(KillfeedEventKind.None, cleaned);
        }

        // Prefer exact case-insensitive containment with word boundaries when possible
        var idx = cleaned.IndexOf(player, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            return new KillfeedEvent(KillfeedEventKind.None, cleaned);
        }

        var after = idx + player.Length;
        var beforeOk = idx == 0 || !char.IsLetterOrDigit(cleaned[idx - 1]);
        var afterOk = after >= cleaned.Length || !char.IsLetterOrDigit(cleaned[after]);
        if (!beforeOk || !afterOk)
        {
            return new KillfeedEvent(KillfeedEventKind.None, cleaned);
        }

        // Name near the start → killer; near the end → victim
        var relative = idx / (double)Math.Max(1, cleaned.Length);
        if (relative <= 0.45)
        {
            return new KillfeedEvent(KillfeedEventKind.LocalKill, cleaned);
        }

        if (relative >= 0.40)
        {
            return new KillfeedEvent(KillfeedEventKind.LocalDeath, cleaned);
        }

        return new KillfeedEvent(KillfeedEventKind.None, cleaned);
    }

    private static string CollapseSpaces(string text)
    {
        var chars = new char[text.Length];
        var n = 0;
        var space = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!space && n > 0)
                {
                    chars[n++] = ' ';
                    space = true;
                }
            }
            else
            {
                chars[n++] = c;
                space = false;
            }
        }

        return new string(chars, 0, n).Trim();
    }
}

public sealed class KillStreakTracker
{
    private readonly HashSet<string> _seenLines = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _lastKillUtc = DateTime.MinValue;

    public int Streak { get; private set; }
    public TimeSpan IdleReset { get; set; } = TimeSpan.FromSeconds(45);

    public event Action<int>? ThresholdReached;

    public void Reset()
    {
        Streak = 0;
        _seenLines.Clear();
        _lastKillUtc = DateTime.MinValue;
    }

    public void ObserveLine(string line, string playerName, DateTime utcNow)
    {
        if (_lastKillUtc != DateTime.MinValue && utcNow - _lastKillUtc > IdleReset && Streak > 0)
        {
            Streak = 0;
            _seenLines.Clear();
        }

        var evt = KillfeedLineParser.Parse(line, playerName);
        if (evt.Kind == KillfeedEventKind.None)
        {
            return;
        }

        var key = evt.RawLine;
        if (!_seenLines.Add(key))
        {
            return;
        }

        if (_seenLines.Count > 40)
        {
            _seenLines.Clear();
            _seenLines.Add(key);
        }

        if (evt.Kind == KillfeedEventKind.LocalDeath)
        {
            Streak = 0;
            return;
        }

        Streak++;
        _lastKillUtc = utcNow;
        if (Streak is 3 or 4 or 5)
        {
            ThresholdReached?.Invoke(Streak);
        }
    }

    public void ObserveOcrText(string fullText, string playerName, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(fullText))
        {
            return;
        }

        foreach (var line in fullText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            ObserveLine(line, playerName, utcNow);
        }
    }
}
