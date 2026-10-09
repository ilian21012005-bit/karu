namespace Karu.Tests;

public class KillStreakTrackerTests
{
    [Theory]
    [InlineData("ilian  Vandal  enemy1", "ilian", KillfeedEventKind.LocalKill)]
    [InlineData("enemy1  Phantom  ilian", "ilian", KillfeedEventKind.LocalDeath)]
    [InlineData("teammate  Operator  foe", "ilian", KillfeedEventKind.None)]
    public void Parser_classifies_killer_and_victim(string line, string player, KillfeedEventKind expected)
    {
        Assert.Equal(expected, KillfeedLineParser.Parse(line, player).Kind);
    }

    [Fact]
    public void Tracker_fires_on_triple_quad_and_ace()
    {
        var tracker = new KillStreakTracker();
        var hits = new List<int>();
        tracker.ThresholdReached += hits.Add;
        var now = DateTime.UtcNow;

        for (var i = 1; i <= 5; i++)
        {
            tracker.ObserveLine($"me  Vandal  foe{i}", "me", now.AddSeconds(i));
        }

        Assert.Equal(new[] { 3, 4, 5 }, hits);
        Assert.Equal(5, tracker.Streak);
    }

    [Fact]
    public void Death_resets_streak()
    {
        var tracker = new KillStreakTracker();
        var now = DateTime.UtcNow;
        tracker.ObserveLine("me  Vandal  a", "me", now);
        tracker.ObserveLine("me  Vandal  b", "me", now.AddSeconds(1));
        tracker.ObserveLine("enemy  Spectre  me", "me", now.AddSeconds(2));
        Assert.Equal(0, tracker.Streak);
    }

    [Fact]
    public void Duplicate_line_is_ignored()
    {
        var tracker = new KillStreakTracker();
        var hits = new List<int>();
        tracker.ThresholdReached += hits.Add;
        var now = DateTime.UtcNow;
        tracker.ObserveLine("me  Vandal  a", "me", now);
        tracker.ObserveLine("me  Vandal  a", "me", now.AddSeconds(1));
        tracker.ObserveLine("me  Vandal  b", "me", now.AddSeconds(2));
        Assert.Equal(2, tracker.Streak);
        Assert.Empty(hits);
    }

    [Fact]
    public void Idle_timeout_resets_streak()
    {
        var tracker = new KillStreakTracker { IdleReset = TimeSpan.FromSeconds(10) };
        var t0 = DateTime.UtcNow;
        tracker.ObserveLine("me  Vandal  a", "me", t0);
        tracker.ObserveLine("me  Vandal  b", "me", t0.AddSeconds(1));
        tracker.ObserveLine("me  Vandal  c", "me", t0.AddSeconds(20));
        Assert.Equal(1, tracker.Streak);
    }
}
