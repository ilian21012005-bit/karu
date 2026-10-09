namespace Karu.Core;

/// <summary>Demande de clip highlight bornée comme Medal (pré/post roll autour de la série).</summary>
public readonly record struct HighlightClipRequest(
    ClipTag Tag,
    DateTime WindowStartUtc,
    DateTime WindowEndUtc);

/// <summary>
/// Arme un seul clip par streak : attend le post-roll après le dernier kill,
/// met à jour le tag (Triple→Quad→Ace) sans multi-sauvegardes.
/// </summary>
public sealed class HighlightClipScheduler
{
    private ClipTag _tag;
    private DateTime _firstKillUtc;
    private DateTime _lastKillUtc;
    private DateTime _readyAtUtc = DateTime.MinValue;
    private DateTime _lastFiredUtc = DateTime.MinValue;
    private bool _pending;

    public TimeSpan PreRoll { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan PostRoll { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan Cooldown { get; set; } = TimeSpan.FromSeconds(15);

    public bool HasPending => _pending;

    public void NotifyThreshold(ClipTag tag, DateTime firstKillUtc, DateTime lastKillUtc, DateTime utcNow)
    {
        if (tag is not (ClipTag.Triple or ClipTag.Quad or ClipTag.Ace))
        {
            return;
        }

        if (_pending)
        {
            if (TagRank(tag) >= TagRank(_tag))
            {
                _tag = tag;
            }

            if (firstKillUtc < _firstKillUtc || _firstKillUtc == DateTime.MinValue)
            {
                _firstKillUtc = firstKillUtc;
            }

            if (lastKillUtc > _lastKillUtc)
            {
                _lastKillUtc = lastKillUtc;
            }

            _readyAtUtc = _lastKillUtc + PostRoll;
            return;
        }

        if (_lastFiredUtc != DateTime.MinValue && utcNow - _lastFiredUtc < Cooldown)
        {
            return;
        }

        _pending = true;
        _tag = tag;
        _firstKillUtc = firstKillUtc;
        _lastKillUtc = lastKillUtc;
        _readyAtUtc = _lastKillUtc + PostRoll;
    }

    public HighlightClipRequest? TryDequeueReady(DateTime utcNow)
    {
        if (!_pending || utcNow < _readyAtUtc)
        {
            return null;
        }

        var request = new HighlightClipRequest(
            _tag,
            _firstKillUtc - PreRoll,
            _lastKillUtc + PostRoll);

        _pending = false;
        _lastFiredUtc = utcNow;
        _firstKillUtc = DateTime.MinValue;
        _lastKillUtc = DateTime.MinValue;
        _readyAtUtc = DateTime.MinValue;
        return request;
    }

    public void Reset()
    {
        _pending = false;
        _firstKillUtc = DateTime.MinValue;
        _lastKillUtc = DateTime.MinValue;
        _readyAtUtc = DateTime.MinValue;
    }

    private static int TagRank(ClipTag tag) => tag switch
    {
        ClipTag.Triple => 3,
        ClipTag.Quad => 4,
        ClipTag.Ace => 5,
        _ => 0
    };
}
