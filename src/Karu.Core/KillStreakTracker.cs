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
        "UNSTOPPABLE", "UNSTOPPABL", "ASSIST", "DAMAGE",
        // HUD / party OCR collé au killfeed
        "READY", "SPIKE", "HAVE", "YOU", "THE", "VE", "INITIATING", "INITIATIN", "INITIATINC",
        "INITIATIN6", "OVERHEALED", "PHASE", "JLT"
    };

    /// <summary>
    /// Agents souvent killers enemy collés en tête OCR.
    /// Exclut clove/tejo/jett/raze : fréquents comme vrais pseudos victimes.
    /// </summary>
    private static readonly HashSet<string> LeadingEnemyAgentNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "reyna", "sage", "sova", "phoenix", "brimstone", "viper", "omen", "cypher",
        "killjoy", "breach", "skye", "yoru", "astra", "kayo", "chamber", "neon",
        "fade", "harbor", "gecko", "deadlock", "iso", "vyse", "waylay"
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
        var after = StripSpectatorTokens(cleaned[(idx + player.Length)..].Trim());
        before = TrimSpectatorPrefix(before);

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
            var cleaned = SanitizeOcrBlob(CollapseSpaces(chunk));
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

            // Party / agent-select UI ("Dead Yet JLT READY") — pas un kill
            if (Regex.IsMatch(cleaned, @"\bREADY\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                continue;
            }

            var beforeCount = results.Count;
            var positions = FindPlayerSpans(cleaned, player);
            if (positions.Count == 0)
            {
                continue;
            }

            var i = 0;
            while (i < positions.Count)
            {
                // Série de "Rick Grimes Rick Grimes …" collés (espace seul entre les matchs)
                var runStart = i;
                while (i + 1 < positions.Count)
                {
                    var gap = cleaned[(positions[i].Index + positions[i].Length)..positions[i + 1].Index].Trim();
                    if (gap.Length > 0)
                    {
                        break;
                    }

                    i++;
                }

                var runEnd = i;
                var runLen = runEnd - runStart + 1;
                var start = positions[runStart].Index;
                var afterBegin = positions[runEnd].Index + positions[runEnd].Length;
                var afterEnd = runEnd + 1 < positions.Count ? positions[runEnd + 1].Index : cleaned.Length;

                var before = runStart == 0 ? cleaned[..start].Trim() : "";
                before = TrimSpectatorPrefix(before);
                before = TrimHudNoisePrefix(before);
                var after = StripSpectatorTokens(cleaned[afterBegin..afterEnd].Trim());

                var glued = before.Length > 0 && IsGluedKillfeedPrefix(before);
                var hudPrefix = before.Length > 0 && IsHudNoisePrefix(before);
                var killLike = after.Length > 0 && (before.Length == 0 || glued || hudPrefix);
                if (killLike)
                {
                    var tokens = UsefulVictimTokens(after)
                        .Where(v => !IsPlayerEchoVictim(v, player))
                        .ToList();
                    if (runLen >= 2 && tokens.Count >= 2)
                    {
                        // "Rick Rick Reyna natsuki Tejo" → skip agent enemy, puis N victimes
                        while (tokens.Count > runLen && LeadingEnemyAgentNames.Contains(tokens[0]))
                        {
                            tokens.RemoveAt(0);
                        }

                        var take = Math.Min(runLen, tokens.Count);
                        foreach (var victim in tokens.Take(take))
                        {
                            results.Add(new KillfeedEvent(
                                KillfeedEventKind.LocalKill,
                                CollapseSpaces(player + " " + victim),
                                "k:" + victim));
                        }
                    }
                    else
                    {
                        var victim = ExtractVictimKey(string.Join(' ', tokens), preferLast: glued);
                        if (!string.IsNullOrEmpty(victim) && !IsPlayerEchoVictim(victim, player))
                        {
                            results.Add(new KillfeedEvent(
                                KillfeedEventKind.LocalKill,
                                CollapseSpaces(player + " " + after),
                                "k:" + victim));
                        }
                    }

                    i = runEnd + 1;
                    continue;
                }

                if (after.Length == 0 && before.Length > 0 && !IsPlayerNameFragment(before, player)
                    && !IsHudNoisePrefix(before)
                    && !IsSpectatorishText(before)
                    && UsefulVictimTokens(before).Count >= 1
                    && before.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 2)
                {
                    results.Add(new KillfeedEvent(
                        KillfeedEventKind.LocalDeath,
                        CollapseSpaces(before + " " + player),
                        "d:" + NormalizeToken(before)));
                }

                i = runEnd + 1;
            }

            // Parse seul si aucun span joueur — évite morts fantômes HUD (INITIATING…)
            if (results.Count == beforeCount && positions.Count == 0)
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

    private readonly record struct PlayerSpan(int Index, int Length);

    /// <summary>
    /// Positions du pseudo (exact) + préfixes OCR tronqués + typos OCR (distance ≤2) pour longs pseudos.
    /// </summary>
    private static List<PlayerSpan> FindPlayerSpans(string text, string player)
    {
        var spans = new List<PlayerSpan>();
        var covered = new bool[text.Length];

        void TryAdd(int idx, int length)
        {
            if (idx < 0 || length <= 0 || idx + length > text.Length)
            {
                return;
            }

            var after = idx + length;
            var beforeOk = idx == 0 || !char.IsLetterOrDigit(text[idx - 1]);
            var afterOk = after >= text.Length || !char.IsLetterOrDigit(text[after]);
            if (!beforeOk || !afterOk)
            {
                return;
            }

            for (var k = idx; k < after; k++)
            {
                if (covered[k])
                {
                    return;
                }
            }

            spans.Add(new PlayerSpan(idx, length));
            for (var k = idx; k < after; k++)
            {
                covered[k] = true;
            }
        }

        foreach (var alias in PlayerAliases(player))
        {
            var start = 0;
            while (start <= text.Length - alias.Length)
            {
                var idx = text.IndexOf(alias, start, StringComparison.OrdinalIgnoreCase);
                if (idx < 0)
                {
                    break;
                }

                TryAdd(idx, alias.Length);
                start = idx + 1;
            }
        }

        // Typos OCR sur longs pseudos : ibtamehyperga ≈ iblamehyperga
        var compact = Regex.Replace(player, @"\s+", "");
        if (compact.Length >= 12)
        {
            var (letters, map) = LettersWithMap(text);
            foreach (var alias in PlayerAliases(player))
            {
                if (alias.Length < 10)
                {
                    continue;
                }

                var target = alias.ToLowerInvariant();
                if (target.Length > letters.Length)
                {
                    continue;
                }

                for (var i = 0; i <= letters.Length - target.Length; i++)
                {
                    if (EditDistanceAtMost(letters.AsSpan(i, target.Length), target.AsSpan(), 2))
                    {
                        var origStart = map[i];
                        var origEnd = map[i + target.Length - 1];
                        TryAdd(origStart, origEnd - origStart + 1);
                    }
                }
            }
        }

        spans.Sort((a, b) => a.Index.CompareTo(b.Index));
        return spans;
    }

    private static IEnumerable<string> PlayerAliases(string player)
    {
        yield return player;
        var compact = Regex.Replace(player, @"\s+", "");
        if (!compact.Equals(player, StringComparison.OrdinalIgnoreCase))
        {
            yield return compact;
        }

        // OCR tronque souvent les longs pseudos : iblamehyperga / iblamehyperg
        if (compact.Length >= 12)
        {
            for (var len = compact.Length - 1; len >= 10; len--)
            {
                yield return compact[..len];
            }

            // OCR drop le premier caractère : blamehypergamy
            if (compact.Length >= 13)
            {
                yield return compact[1..];
            }
        }
    }

    private static (string Letters, int[] Map) LettersWithMap(string text)
    {
        var letters = new char[text.Length];
        var map = new int[text.Length];
        var n = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsLetterOrDigit(text[i]))
            {
                letters[n] = char.ToLowerInvariant(text[i]);
                map[n] = i;
                n++;
            }
        }

        return (new string(letters, 0, n), map.AsSpan(0, n).ToArray());
    }

    internal static bool EditDistanceAtMost(ReadOnlySpan<char> a, ReadOnlySpan<char> b, int max)
    {
        if (Math.Abs(a.Length - b.Length) > max)
        {
            return false;
        }

        if (a.Length == 0 || b.Length == 0)
        {
            return Math.Max(a.Length, b.Length) <= max;
        }

        // Banded DP — suffisant pour pseudos ~15 car.
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            prev[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            var rowMin = cur[0];
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(prev[j] + 1, cur[j - 1] + 1), prev[j - 1] + cost);
                if (cur[j] < rowMin)
                {
                    rowMin = cur[j];
                }
            }

            if (rowMin > max)
            {
                return false;
            }

            (prev, cur) = (cur, prev);
        }

        return prev[b.Length] <= max;
    }

    /// <summary>Enlève points OCR au milieu des mots (iblam.ehypergamy) et timers collés.</summary>
    private static string SanitizeOcrBlob(string text)
    {
        if (text.Length == 0)
        {
            return text;
        }

        var noDots = Regex.Replace(text, @"(?<=\w)[.\u00B7•'’](?=\w)", "", RegexOptions.CultureInvariant);
        // "i blamehypergamy" / "ib lamehypergamy" → recolle fragments du long pseudo
        noDots = Regex.Replace(noDots, @"\bi\s+(?=blame)", "i", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        noDots = Regex.Replace(noDots, @"\bib\s+(?=lame)", "ib", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        // Timer HUD "0:35" / "0:06" en tête
        noDots = Regex.Replace(noDots, @"^\d+:\d+\s*", "", RegexOptions.CultureInvariant);
        return CollapseSpaces(noDots);
    }

    private static string TrimHudNoisePrefix(string before)
    {
        var cleaned = CollapseSpaces(before);
        if (cleaned.Length == 0)
        {
            return "";
        }

        cleaned = Regex.Replace(
            cleaned,
            @"^(?:(?:YOU\s+)?(?:HAVE\s+)?(?:THE\s+)?SPIKE|VE\s+THE\s+SPIKE|E?\s*INITIAT\w*|OUTGOING|INCOMING)\b[\s\d:]*",
            "",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Trim();
        cleaned = Regex.Replace(cleaned, @"^\d+:\d+\s*", "", RegexOptions.CultureInvariant);
        return CollapseSpaces(cleaned);
    }

    private static bool IsHudNoisePrefix(string before)
    {
        var cleaned = CollapseSpaces(before);
        if (cleaned.Length == 0)
        {
            return false;
        }

        return Regex.IsMatch(
            cleaned,
            @"\b(?:SPIKE|INITIAT\w*|OUTGOING|INCOMING|COMBAT|REPORT)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
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
                       && !IsSpectatorishToken(t);
            });

        return CollapseSpaces(string.Join(' ', kept));
    }

    private static bool IsSpectatorishToken(string token)
    {
        var t = NormalizeToken(token);
        if (t.Length < 3)
        {
            return false;
        }

        // SPECTATORS / SPECTATO / SPCTATORS / SPECIATORS / TATORS…
        if (t.Contains("spect", StringComparison.OrdinalIgnoreCase)
            || t.Contains("tator", StringComparison.OrdinalIgnoreCase)
            || t.Contains("pectat", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return Regex.IsMatch(t, @"^sp[eéc]{0,3}t", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool IsSpectatorishText(string text)
    {
        var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            return false;
        }

        var useful = tokens.Select(NormalizeToken)
            .Where(t => t.Length >= 2 && !t.All(char.IsDigit) && !IsSpectatorishToken(t) && !NoiseTokens.Contains(t))
            .ToArray();
        return useful.Length == 0;
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

    /// <summary>Victime OCR qui n'est qu'un écho / typo du pseudo local.</summary>
    private static bool IsPlayerEchoVictim(string victim, string player)
    {
        var b = NormalizeToken(victim);
        var p = NormalizeToken(player).Replace(" ", "");
        if (b.Length == 0 || p.Length == 0)
        {
            return true;
        }

        if (p.StartsWith(b, StringComparison.OrdinalIgnoreCase) && b.Length <= p.Length)
        {
            return true;
        }

        if (p.Contains(b, StringComparison.OrdinalIgnoreCase) && b.Length >= 6)
        {
            return true;
        }

        if (b.Length >= 10 && p.Length >= 12 && EditDistanceAtMost(b.AsSpan(), p.AsSpan(), 3))
        {
            return true;
        }

        foreach (var alias in PlayerAliases(player))
        {
            if (alias.Length >= 10 && EditDistanceAtMost(b.AsSpan(), alias.ToLowerInvariant().AsSpan(), 2))
            {
                return true;
            }
        }

        return false;
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
        var tokens = UsefulVictimTokens(afterPlayer);
        if (tokens.Count == 0)
        {
            return "";
        }

        if (preferLast)
        {
            return tokens[^1];
        }

        // Un agent enemy en tête + autre token → collé ("Reyna natsuki/Tejo")
        if (tokens.Count >= 2 && LeadingEnemyAgentNames.Contains(tokens[0]))
        {
            return tokens[1];
        }

        return tokens[0];
    }

    private static List<string> UsefulVictimTokens(string afterPlayer)
    {
        var raw = afterPlayer.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeToken)
            .Where(t => t.Length >= 2 && !NoiseTokens.Contains(t) && !t.All(char.IsDigit) && !IsSpectatorishToken(t))
            .ToList();

        // OCR coupe "Mrsweaty" → "Mr sweaty" (joindre avant filtre consonnes)
        var joined = new List<string>();
        for (var i = 0; i < raw.Count; i++)
        {
            if (i + 1 < raw.Count
                && raw[i] is "mr" or "mrs"
                && raw[i + 1].Length >= 3)
            {
                joined.Add(raw[i] + raw[i + 1]);
                i++;
                continue;
            }

            joined.Add(raw[i]);
        }

        var list = new List<string>();
        foreach (var t in joined)
        {
            // Bruit OCR HUD ("cmtg") : consonnes courtes sans voyelle
            if (t.Length <= 4 && t.All(c => c is >= 'a' and <= 'z') && !t.Any(IsVowel))
            {
                continue;
            }

            list.Add(t);
        }

        return list;
    }

    private static bool IsVowel(char c) =>
        c is 'a' or 'e' or 'i' or 'o' or 'u' or 'y';

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

        cleaned = Regex.Replace(
            cleaned,
            @"^(?:SPECTATORS?|SPECIATORS?|SPECTMORS|SPECTATO\w*|SPCTATORS|SPECT|SPE|TATORS|CTATORS)\b[\s\d:]*",
            "",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Trim();
        // Relancer si OCR a laissé un fragment spect*
        while (cleaned.Length > 0)
        {
            var first = cleaned.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries)[0];
            if (!IsSpectatorishToken(first))
            {
                break;
            }

            cleaned = cleaned.Length > first.Length ? cleaned[(first.Length)..].Trim() : "";
            cleaned = Regex.Replace(cleaned, @"^[\d:\s]+", "").Trim();
        }

        return CollapseSpaces(cleaned);
    }

    private static string NormalizeToken(string token)
    {
        var chars = token.Where(c => char.IsLetterOrDigit(c) || c is '_' or '-').Select(c =>
        {
            // OCR accents courants sur SPECTATORS
            return c switch
            {
                'é' or 'è' or 'ê' or 'ë' => 'e',
                'á' or 'à' or 'â' => 'a',
                'É' or 'È' or 'Ê' => 'e',
                _ => char.ToLowerInvariant(c)
            };
        }).ToArray();
        return new string(chars);
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

    private bool IsDuplicateStableKey(string key)
    {
        if (_seenKeys.Contains(key))
        {
            return true;
        }

        // OCR proche : mrsweaty / mrweaty
        if (key.Length >= 8 && key.StartsWith("k:", StringComparison.Ordinal))
        {
            var victim = key[2..];
            if (victim.Length >= 6)
            {
                foreach (var seen in _seenKeys)
                {
                    if (seen.Length >= 8 && seen.StartsWith("k:", StringComparison.Ordinal)
                        && KillfeedLineParser.EditDistanceAtMost(victim.AsSpan(), seen.AsSpan(2), 2))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
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

        if (IsDuplicateStableKey(evt.StableKey))
        {
            return;
        }

        _seenKeys.Add(evt.StableKey);

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
