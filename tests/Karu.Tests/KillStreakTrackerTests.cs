namespace Karu.Tests;

public class KillStreakTrackerTests
{
    [Theory]
    [InlineData("ilian  Vandal  enemy1", "ilian", KillfeedEventKind.LocalKill)]
    [InlineData("enemy1  Phantom  ilian", "ilian", KillfeedEventKind.LocalDeath)]
    [InlineData("teammate  Operator  foe", "ilian", KillfeedEventKind.None)]
    [InlineData("pastelghost Noot Noot Rick Grimes Noot Noot im owned Clove", "Rick Grimes", KillfeedEventKind.None)]
    public void Parser_classifies_killer_and_victim(string line, string player, KillfeedEventKind expected)
    {
        Assert.Equal(expected, KillfeedLineParser.Parse(line, player).Kind);
    }

    [Fact]
    public void ExtractEvents_splits_glued_ocr_into_distinct_kills()
    {
        var events = KillfeedLineParser.ExtractEvents(
            "Rick Grimes Clove Rick Grimes SekiR0",
            "Rick Grimes");

        Assert.Equal(2, events.Count(e => e.Kind == KillfeedEventKind.LocalKill));
        Assert.Contains(events, e => e.StableKey == "k:clove");
        Assert.Contains(events, e => e.StableKey == "k:sekiro" || e.StableKey == "k:sekir0");
    }

    [Fact]
    public void ExtractEvents_ignores_name_stuck_in_middle_of_blob()
    {
        var events = KillfeedLineParser.ExtractEvents(
            "pastelghost Noot Noot im owned Clove and more junk Rick Grimes Noot Noot im owned Clove extra",
            "Rick Grimes");

        // Long préfixe (>3 tokens utiles) → pas un glue killfeed court
        Assert.Empty(events.Where(e => e.Kind == KillfeedEventKind.LocalKill && e.StableKey == "k:clove"));
    }

    [Fact]
    public void ExtractEvents_accepts_short_glued_prefix_before_player_kill()
    {
        var events = KillfeedLineParser.ExtractEvents(
            "Clove Rick Grimes Reyna",
            "Rick Grimes");

        Assert.Contains(events, e => e.Kind == KillfeedEventKind.LocalKill && e.StableKey == "k:reyna");
    }

    [Fact]
    public void ExtractEvents_keeps_victim_after_spectators_ocr_noise()
    {
        var events = KillfeedLineParser.ExtractEvents(
            "Rick Grimes CTATORS Noot Noot Clove",
            "Rick Grimes");

        Assert.Contains(events, e => e.Kind == KillfeedEventKind.LocalKill && e.StableKey == "k:noot");
    }

    [Fact]
    public void ExtractEvents_ignores_combat_report_death_screen()
    {
        var events = KillfeedLineParser.ExtractEvents(
            "Rick Grimes KILLED BY REYNA Reyna OUTGOING COMBAT REPORT 230 Unstoppable INCOMING",
            "Rick Grimes");

        Assert.Empty(events);
    }

    [Fact]
    public void ExtractEvents_quad_clip_ocr_reaches_four_kills()
    {
        var frames = new[]
        {
            "Rick Grimes sub catboy",
            "Clove Rick Grimes pastelghost Reyna",
            "Rick Grimes Noot Noot",
            "rimes Rick Grimes CTATORS Noot Noot Clove",
        };

        var tracker = new KillStreakTracker();
        var hits = new List<int>();
        tracker.ThresholdReached += hits.Add;
        var now = DateTime.UtcNow;
        for (var i = 0; i < frames.Length; i++)
        {
            tracker.ObserveOcrText(frames[i], "Rick Grimes", now.AddSeconds(i * 3));
        }

        Assert.Equal(4, tracker.Streak);
        Assert.Equal(new[] { 3, 4 }, hits);
    }

    [Fact]
    public void ExtractEvents_skips_enemy_reyna_glued_in_double_rick_blob()
    {
        var events = KillfeedLineParser.ExtractEvents(
            "Rick Grimes Rick Grimes Reyna natsuki Tejo",
            "Rick Grimes");

        Assert.Contains(events, e => e.StableKey == "k:natsuki");
        Assert.Contains(events, e => e.StableKey == "k:tejo");
        Assert.DoesNotContain(events, e => e.StableKey == "k:reyna");
    }

    [Fact]
    public void ExtractEvents_reads_jiao_after_spectators_noise()
    {
        var events = KillfeedLineParser.ExtractEvents(
            "Rick Grimes SPECTATORS 1 Jiao",
            "Rick Grimes");

        Assert.Contains(events, e => e.Kind == KillfeedEventKind.LocalKill && e.StableKey == "k:jiao");
    }

    [Fact]
    public void Ground_truth_24411_quad_natsuki_tejo_clove_jiao()
    {
        var frames = new[]
        {
            "Rick Grimes Rick Grimes natsuki Tejo",
            "Rick Grimes natsuki Rick Grimes Tejo",
            "Rick Grimes Rick Grimes Reyna natsuki Tejo astelghost",
            "Rick Grimes Clove",
            "Rick Grimes SPECTATORS 1 Clove",
            "Rick Grimes SPECTATORS 1 Jiao",
        };

        var tracker = new KillStreakTracker();
        var hits = new List<int>();
        tracker.ThresholdReached += hits.Add;
        var t0 = DateTime.UtcNow;
        for (var i = 0; i < frames.Length; i++)
        {
            tracker.ObserveOcrText(frames[i], "Rick Grimes", t0.AddSeconds(i));
        }

        Assert.Equal(4, tracker.Streak);
        Assert.Equal(new[] { 3, 4 }, hits);
    }

    [Fact]
    public void ExtractEvents_victim_is_first_token_not_next_killfeed_line()
    {
        var events = KillfeedLineParser.ExtractEvents(
            "Rick Grimes sub catboy Reyna PawsocksEgirluwu",
            "Rick Grimes");

        Assert.Contains(events, e => e.StableKey == "k:sub");
        Assert.DoesNotContain(events, e => e.StableKey == "k:pawsocksegirluwu");
    }

    [Fact]
    public void Medal_ocr_dump_does_not_overcount_to_triple()
    {
        var frames = new[]
        {
            "pastelghost Reyna",
            "pastelghost Noot Noot Rick Grimes Noot Noot im owned Clove",
            "Rick Grimes Clove",
            "Rick Grimes Rick Grimes Clove SekiR0",
            "Rick Grimes Rick Grimes Clove A. SekiR0",
            "Rick Grimes Rick Grimes Clove",
            "Rick Grimes sub catboy pastelghost",
        };

        var tracker = new KillStreakTracker();
        var hits = new List<int>();
        tracker.ThresholdReached += hits.Add;
        var now = DateTime.UtcNow;

        for (var i = 0; i < frames.Length; i++)
        {
            tracker.ObserveOcrText(frames[i], "Rick Grimes", now.AddSeconds(i));
        }

        // Avant : Triple/Quad/Ace fantômes sur les mêmes 2 victimes OCR collées.
        Assert.InRange(tracker.Streak, 2, 3);
        Assert.DoesNotContain(4, hits);
        Assert.DoesNotContain(5, hits);
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
        tracker.ObserveLine("me  Vandal  alpha", "me", now);
        tracker.ObserveLine("me  Vandal  bravo", "me", now.AddSeconds(1));
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
        tracker.ObserveLine("me  Vandal  alpha", "me", now);
        tracker.ObserveLine("me  Vandal  alpha", "me", now.AddSeconds(1));
        tracker.ObserveLine("me  Vandal  bravo", "me", now.AddSeconds(2));
        Assert.Equal(2, tracker.Streak);
        Assert.Empty(hits);
    }

    [Fact]
    public void Duplicate_victim_across_ocr_frames_is_ignored()
    {
        var tracker = new KillStreakTracker();
        var hits = new List<int>();
        tracker.ThresholdReached += hits.Add;
        var now = DateTime.UtcNow;

        tracker.ObserveOcrText("Rick Grimes Clove", "Rick Grimes", now);
        tracker.ObserveOcrText("Rick Grimes Clove Rick Grimes SekiR0", "Rick Grimes", now.AddSeconds(1));
        tracker.ObserveOcrText("Rick Grimes Clove", "Rick Grimes", now.AddSeconds(2));

        // clove une fois + sekiro une fois
        Assert.Equal(2, tracker.Streak);
        Assert.Empty(hits);
    }

    [Fact]
    public void Ocr_blob_can_reach_triple_with_three_victims()
    {
        var tracker = new KillStreakTracker();
        var hits = new List<int>();
        tracker.ThresholdReached += hits.Add;
        var now = DateTime.UtcNow;

        tracker.ObserveOcrText(
            "Rick Grimes Alpha Rick Grimes Bravo Rick Grimes Charlie",
            "Rick Grimes",
            now);

        Assert.Equal(3, tracker.Streak);
        Assert.Equal(new[] { 3 }, hits);
    }

    [Fact]
    public void Idle_timeout_resets_streak()
    {
        var tracker = new KillStreakTracker { IdleReset = TimeSpan.FromSeconds(10) };
        var t0 = DateTime.UtcNow;
        tracker.ObserveLine("me  Vandal  alpha", "me", t0);
        tracker.ObserveLine("me  Vandal  bravo", "me", t0.AddSeconds(1));
        tracker.ObserveLine("me  Vandal  charlie", "me", t0.AddSeconds(20));
        Assert.Equal(1, tracker.Streak);
    }
}
