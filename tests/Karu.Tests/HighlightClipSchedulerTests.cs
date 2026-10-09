namespace Karu.Tests;

public class HighlightClipSchedulerTests
{
    [Fact]
    public void Arms_after_post_roll_from_last_kill_not_immediately()
    {
        var scheduler = new HighlightClipScheduler
        {
            PreRoll = TimeSpan.FromSeconds(10),
            PostRoll = TimeSpan.FromSeconds(10)
        };
        var t0 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        scheduler.NotifyThreshold(ClipTag.Triple, firstKillUtc: t0, lastKillUtc: t0.AddSeconds(20), utcNow: t0.AddSeconds(20));

        Assert.Null(scheduler.TryDequeueReady(t0.AddSeconds(25)));

        var ready = scheduler.TryDequeueReady(t0.AddSeconds(30));
        Assert.NotNull(ready);
        Assert.Equal(ClipTag.Triple, ready!.Value.Tag);
        Assert.Equal(t0.AddSeconds(-10), ready.Value.WindowStartUtc);
        Assert.Equal(t0.AddSeconds(30), ready.Value.WindowEndUtc);
    }

    [Fact]
    public void Upgrades_tag_and_extends_window_when_streak_grows()
    {
        var scheduler = new HighlightClipScheduler
        {
            PreRoll = TimeSpan.FromSeconds(10),
            PostRoll = TimeSpan.FromSeconds(10)
        };
        var first = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        scheduler.NotifyThreshold(ClipTag.Triple, first, first.AddSeconds(10), first.AddSeconds(10));
        scheduler.NotifyThreshold(ClipTag.Quad, first, first.AddSeconds(40), first.AddSeconds(40));
        scheduler.NotifyThreshold(ClipTag.Ace, first, first.AddSeconds(90), first.AddSeconds(90));

        Assert.Null(scheduler.TryDequeueReady(first.AddSeconds(95)));

        var ready = scheduler.TryDequeueReady(first.AddSeconds(100));
        Assert.NotNull(ready);
        Assert.Equal(ClipTag.Ace, ready!.Value.Tag);
        Assert.Equal(first.AddSeconds(-10), ready.Value.WindowStartUtc);
        Assert.Equal(first.AddSeconds(100), ready.Value.WindowEndUtc);
    }

    [Fact]
    public void Single_fire_per_pending_then_cooldown_blocks_rearm()
    {
        var scheduler = new HighlightClipScheduler
        {
            PostRoll = TimeSpan.FromSeconds(5),
            Cooldown = TimeSpan.FromSeconds(15)
        };
        var t0 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        scheduler.NotifyThreshold(ClipTag.Triple, t0, t0, t0);
        var first = scheduler.TryDequeueReady(t0.AddSeconds(5));
        Assert.NotNull(first);
        Assert.Null(scheduler.TryDequeueReady(t0.AddSeconds(6)));

        scheduler.NotifyThreshold(ClipTag.Triple, t0.AddSeconds(10), t0.AddSeconds(10), t0.AddSeconds(10));
        Assert.Null(scheduler.TryDequeueReady(t0.AddSeconds(20)));

        scheduler.NotifyThreshold(ClipTag.Triple, t0.AddSeconds(25), t0.AddSeconds(25), t0.AddSeconds(25));
        Assert.NotNull(scheduler.TryDequeueReady(t0.AddSeconds(30)));
    }
}

public class SegmentWindowTests
{
    [Fact]
    public void FilesToSaveInWindow_keeps_only_overlapping_segments()
    {
        var t0 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var files = Enumerable.Range(0, 10)
            .Select(i => $@"C:\buf\seg_{i:D5}.ts")
            .ToArray();

        // Chaque segment i se termine à t0 + (i+1)*5s et dure 5s.
        DateTime EndUtc(string path)
        {
            var name = Path.GetFileNameWithoutExtension(path)!;
            var index = int.Parse(name.Split('_')[1]);
            return t0.AddSeconds((index + 1) * 5);
        }

        // Fenêtre couvrant kills ~25s–45s → segments autour de ça (+ marge déjà dans window)
        var windowStart = t0.AddSeconds(20);
        var windowEnd = t0.AddSeconds(50);

        var selected = SegmentRetention.FilesToSaveInWindow(
            files,
            windowStart,
            windowEnd,
            currentlyWriting: @"C:\buf\seg_00009.ts",
            getSegmentEndUtc: EndUtc);

        Assert.Equal(
            new[]
            {
                @"C:\buf\seg_00003.ts", // 15–20 overlaps start? start=20 end=20 → edge
                @"C:\buf\seg_00004.ts", // 20–25
                @"C:\buf\seg_00005.ts",
                @"C:\buf\seg_00006.ts",
                @"C:\buf\seg_00007.ts",
                @"C:\buf\seg_00008.ts", // 40–45
            },
            selected);
    }
}
