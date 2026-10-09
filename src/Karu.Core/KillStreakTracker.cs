using System.Text.RegularExpressions;

namespace Karu.Core;

public enum KillfeedEventKind
{
    None,
    LocalKill,
    LocalDeath
}

public readonly record struct KillfeedEvent(KillfeedEventKind Kind, string RawLine, string StableKey);

public static class KillfeedLineParser
{
    private static readonly HashSet<string> NoiseTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "SPECTATORS", "SPECTATOR", "SPECT", "SPE", "SPEC", "TATORS", "CTATORS", "SPECIATORS",
        "EXCLUSIVE", "EXCLU", "XVE",
        // Armes / UI OCR — pas des pseudos victimes
        "VANDAL", "PHANTOM", "SPECTRE", "OPERATOR", "MARSHAL", "SHERIFF", "GHOST", "CLASSIC",
        "FRENZY", "SHORTY", "BUCKY", "JUDGE", "ARES", "ODIN", "STINGER", "OUTLAW", "BULLDOG",
        "HEADSHOT", "HS",
        // Écran mort / combat report Valorant
        "KILLED", "KILLEO", "KILL", "BY", "OUTGOING", "INCOMING", "COMBAT", "REPORT",
        "UNSTOPPABLE", "UNSTOPPABL", "ASSIST", "DAMAGE"
    };

    /// <summary>
    /// Parse une ligne killfeed déjà isolée : "Killer … Victim".
    /// Killer = pseudo en début de ligne ; mort = pseudo en fin.
    /// </summary>
    public static KillfeedEvent Parse(string line, string playerName)
    {
        if (string.IsNullOrWhiteSpace(line) || string.IsNullOrWhiteSpace(playerName))
        {
            return new KillfeedEvent(KillfeedEventKind.None, line ?? "", "");
        }

        var cleaned = CollapseSpaces(line.Trim());
        var player = CollapseSpaces(playerName.Trim());
        if (cleaned.Length == 0 || player.Length == 0)
        {
            return new KillfeedEvent(KillfeedEventKind.None, cleaned, "");
        }

        if (!TryFindPlayer(cleaned, player, out var idx))
        {
            return new KillfeedEvent(KillfeedEventKind.None, cleaned, "");
        }

        var before = idx == 0 ? "" : cleaned[..idx].Trim();
        var after = cleaned[(idx + player.Length)..].Trim();

        // Pseudo au début (+ victime) → kill local
        if (before.Length == 0 && after.Length > 0)
        {
            var victim = ExtractVictimKey(after, preferLast: false);
            if (string.IsNullOrEmpty(victim))
            {
                return new KillfeedEvent(KillfeedEventKind.None, cleaned, "");
            }

            return new KillfeedEvent(KillfeedEventKind.LocalKill, cleaned, "k:" + victim);
        }

        // Pseudo à la fin (+ killer devant) → mort (préfixe ≥2 tokens pour éviter OCR inversé)
        if (after.Length == 0 && before.Length > 0
            && before.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 2)
        {
            return new KillfeedEvent(KillfeedEventKind.LocalDeath, cleaned, "d:" + NormalizeToken(before));
        }

        // Nom seul ou coincé au milieu d'un blob OCR → ignorer
        return new KillfeedEvent(KillfeedEventKind.None, cleaned, "");
    }

    /// <summary>
    /// Découpe un blob OCR (souvent sans \n) en événements kill/mort fiables.
    /// Pseudo au début d'un segment → kill ; à la fin → mort ; coincé au milieu → ignoré.
    /// </summary>
    public static IReadOnlyList<KillfeedEvent> ExtractEvents(string fullText, string playerName)
    {
        var results = new List<KillfeedEvent>();
        if (string.IsNullOrWhiteSpace(fullText) || string.IsNullOrWhiteSpace(playerName))
        {
            return results;
        }

        var player = CollapseSpaces(playerName.Trim());

        foreach (var chunk in SplitRawChunks(fullText))
        {
            var cleaned = CollapseSpaces(chunk);
            if (cleaned.Length == 0)
            {
                continue;
            }

            // Écran mort / combat report — pas du killfeed
            if (Regex.IsMatch(cleaned, @"\b(?:KILLED|KILLEO)\s+BY\b|\bCOMBAT\s+REPORT\b|\bOUTGOING\b|\bINCOMING\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                continue;
            }

            var beforeCount = results.Count;
            var positions = FindPlayerPositions(cleaned, player);
            if (positions.Count == 0)
            {
                continue;
            }

            for (var i = 0; i < positions.Count; i++)
            {
                var start = positions[i];
                var end = start + player.Length;
                var nextStart = i + 1 < positions.Count ? positions[i + 1] : cleaned.Length;

                // 1er match : préfixe du blob. Matches suivants = nouvelles lignes killfeed collées.
                var before = i == 0 ? cleaned[..start].Trim() : "";
                var after = StripSpectatorTokens(cleaned[end..nextStart].Trim());
                before = TrimSpectatorPrefix(before);

                // Kill si pseudo en tête, ou collé après un court préfixe (autre ligne killfeed / agent).
                var glued = before.Length > 0 && IsGluedKillfeedPrefix(before);
                var killLike = after.Length > 0 && (before.Length == 0 || glued);
                if (killLike)
                {
                    // Ligne propre → 1er token ; glue "Clove Rick … Reyna" → dernier token (notre victime)
                    var victim = ExtractVictimKey(after, preferLast: glued);
                    if (string.IsNullOrEmpty(victim))
                    {
                        continue;
                    }

                    var raw = CollapseSpaces(player + " " + after);
                    results.Add(new KillfeedEvent(KillfeedEventKind.LocalKill, raw, "k:" + victim));
                    continue;
                }

                // Mort : besoin d'un vrai préfixe (pseudo ± arme). 1 seul token = souvent OCR inversé.
                if (after.Length == 0 && before.Length > 0 && !IsPlayerNameFragment(before, player)
                    && before.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 2)
                {
                    var raw = CollapseSpaces(before + " " + player);
                    results.Add(new KillfeedEvent(KillfeedEventKind.LocalDeath, raw, "d:" + NormalizeToken(before)));
                }

                // Long contenu avant ET après → pseudo vraiment coincé au milieu → ignorer
            }

            if (results.Count == beforeCount)
            {
                var single = Parse(cleaned, player);
                if (single.Kind != KillfeedEventKind.None)
                {
                    results.Add(single);
                }
            }
        }

        return results;
    }

    private static List<int> FindPlayerPositions(string text, string player)
    {
        var list = new List<int>();
        var start = 0;
        while (start <= text.Length - player.Length)
        {
            var idx = text.IndexOf(player, start, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                break;
            }

            var after = idx + player.Length;
            var beforeOk = idx == 0 || !char.IsLetterOrDigit(text[idx - 1]);
            var afterOk = after >= text.Length || !char.IsLetterOrDigit(text[after]);
            if (beforeOk && afterOk)
            {
                list.Add(idx);
            }

            start = idx + 1;
        }

        return list;
    }

    /// <summary>Enlève les tokens SPECTATORS sans jeter les victimes OCR collées derrière.</summary>
    private static string StripSpectatorTokens(string text)
    {
        var cleaned = CollapseSpaces(text);
        if (cleaned.Length == 0)
        {
            return "";
        }

        var kept = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(tok =>
            {
                var t = NormalizeToken(tok);
                return t.Length >= 2
                       && !NoiseTokens.Contains(t)
                       && !Regex.IsMatch(t, @"^(spectators?|speciators|spect|spe|spec|tators|ctators)$",
                           RegexOptions.IgnoreCase);
            });

        return CollapseSpaces(string.Join(' ', kept));
    }

    private static bool IsPlayerNameFragment(string before, string player)
    {
        var b = NormalizeToken(before);
        if (b.Length < 4)
        {
            return true;
        }

        var p = NormalizeToken(player).Replace(" ", "");
        return p.Contains(b, StringComparison.OrdinalIgnoreCase)
               || b.Contains(p, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> SplitRawChunks(string fullText)
    {
        foreach (var part in fullText.Split(new[] { '\r', '\n', '|', '•' }, StringSplitOptions.RemoveEmptyEntries))
        {
            yield return part;
        }
    }

    private static bool TryFindPlayer(string text, string player, out int index)
    {
        index = 0;
        var start = 0;
        while (start <= text.Length - player.Length)
        {
            var idx = text.IndexOf(player, start, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                return false;
            }

            var after = idx + player.Length;
            var beforeOk = idx == 0 || !char.IsLetterOrDigit(text[idx - 1]);
            var afterOk = after >= text.Length || !char.IsLetterOrDigit(text[after]);
            if (beforeOk && afterOk)
            {
                index = idx;
                return true;
            }

            start = idx + 1;
        }

        return false;
    }

    /// <summary>
    /// Victime utile après le killer. preferLast = glue multi-lignes (notre victime en fin).
    /// </summary>
    private static string ExtractVictimKey(string afterPlayer, bool preferLast)
    {
        var tokens = afterPlayer.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string? first = null;
        string? last = null;
        foreach (var token in tokens)
        {
            var t = NormalizeToken(token);
            if (t.Length < 2)
            {
                continue;
            }

            if (NoiseTokens.Contains(t))
            {
                continue;
            }

            if (t.All(char.IsDigit))
            {
                continue;
            }

            first ??= t;
            last = t;
        }

        if (preferLast)
        {
            return last ?? "";
        }

        return first ?? "";
    }

    /// <summary>Préfixe court d'une autre ligne killfeed collée devant notre kill.</summary>
    private static bool IsGluedKillfeedPrefix(string before)
    {
        var tokens = before.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeToken)
            .Where(t => t.Length >= 2 && !NoiseTokens.Contains(t) && !t.All(char.IsDigit))
            .ToArray();

        // Un seul token : nom d'agent / killer de la ligne au-dessus ("Clove", "Reyna")
        return tokens.Length == 1;
    }

    private static string TrimSpectatorPrefix(string text)
    {
        var cleaned = CollapseSpaces(text);
        if (cleaned.Length == 0)
        {
            return "";
        }

        return Regex.Replace(
            cleaned,
            @"^(?:SPECTATORS?|SPECT|SPE|TATORS|CTATORS)\b[\s\d]*",
            "",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Trim();
    }

    private static string NormalizeToken(string token)
    {
        var chars = token.Where(c => char.IsLetterOrDigit(c) || c is '_' or '-').ToArray();
        return new string(chars).ToLowerInvariant();
    }

    internal static string CollapseSpaces(string text)
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
    private readonly HashSet<string> _seenKeys = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _lastKillUtc = DateTime.MinValue;
    private DateTime _firstKillUtc = DateTime.MinValue;

    public int Streak { get; private set; }
    public DateTime FirstKillUtc => _firstKillUtc;
    public DateTime LastKillUtc => _lastKillUtc;
    public TimeSpan IdleReset { get; set; } = TimeSpan.FromSeconds(45);

    public event Action<int>? ThresholdReached;

    public void Reset()
    {
        Streak = 0;
        _seenKeys.Clear();
        _lastKillUtc = DateTime.MinValue;
        _firstKillUtc = DateTime.MinValue;
    }

    public void ObserveLine(string line, string playerName, DateTime utcNow)
    {
        // Une ligne isolée : Parse classique
        ApplyEvent(KillfeedLineParser.Parse(line, playerName), utcNow);
    }

    public void ObserveOcrText(string fullText, string playerName, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(fullText))
        {
            return;
        }

        foreach (var evt in KillfeedLineParser.ExtractEvents(fullText, playerName))
        {
            ApplyEvent(evt, utcNow);
        }
    }

    private void ApplyEvent(KillfeedEvent evt, DateTime utcNow)
    {
        if (evt.Kind == KillfeedEventKind.None || string.IsNullOrEmpty(evt.StableKey))
        {
            return;
        }

        if (_lastKillUtc != DateTime.MinValue && utcNow - _lastKillUtc > IdleReset && Streak > 0)
        {
            Streak = 0;
            _seenKeys.Clear();
            _firstKillUtc = DateTime.MinValue;
        }

        if (!_seenKeys.Add(evt.StableKey))
        {
            return;
        }

        if (_seenKeys.Count > 40)
        {
            _seenKeys.Clear();
            _seenKeys.Add(evt.StableKey);
        }

        if (evt.Kind == KillfeedEventKind.LocalDeath)
        {
            Streak = 0;
            _seenKeys.Clear();
            _firstKillUtc = DateTime.MinValue;
            return;
        }

        Streak++;
        if (Streak == 1 || _firstKillUtc == DateTime.MinValue)
        {
            _firstKillUtc = utcNow;
        }

        _lastKillUtc = utcNow;
        if (Streak is 3 or 4 or 5)
        {
            ThresholdReached?.Invoke(Streak);
        }
    }
}
