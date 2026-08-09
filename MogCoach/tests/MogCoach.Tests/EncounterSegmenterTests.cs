using MogCoach.Core.Model;
using MogCoach.Ingest;
using Xunit;

namespace MogCoach.Tests;

public class EncounterSegmenterTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static CombatEvent Cast(double atSeconds, bool self = true) => new()
    {
        Timestamp = T0.AddSeconds(atSeconds),
        Type = CombatEventType.Cast,
        IsSelf = self,
        AbilityName = "Ability",
    };

    private static async IAsyncEnumerable<CombatEvent> Stream(IEnumerable<CombatEvent> events)
    {
        foreach (var e in events) { await Task.Yield(); yield return e; }
    }

    [Fact]
    public async Task Splits_two_pulls_separated_by_idle_gap()
    {
        var events = new List<CombatEvent>();
        for (var s = 0; s <= 30; s += 3) events.Add(Cast(s));       // pull 1: 0..30s
        for (var s = 120; s <= 150; s += 3) events.Add(Cast(s));    // pull 2: 120..150s (big gap)

        var segmenter = new EncounterSegmenter(new SegmenterOptions { IdleGapSeconds = 20, MinPullSeconds = 15 });
        var pulls = await segmenter.SegmentAsync(Stream(events), Job.Warrior);

        Assert.Equal(2, pulls.Count);
        Assert.All(pulls, p => Assert.Equal(Job.Warrior, p.Job));
    }

    [Fact]
    public async Task Drops_fragments_shorter_than_minimum()
    {
        var events = new List<CombatEvent> { Cast(0), Cast(2), Cast(4) }; // 4s < MinPullSeconds
        var segmenter = new EncounterSegmenter(new SegmenterOptions { MinPullSeconds = 15 });
        var pulls = await segmenter.SegmentAsync(Stream(events));
        Assert.Empty(pulls);
    }

    [Fact]
    public async Task Zone_change_breaks_a_pull()
    {
        var events = new List<CombatEvent>();
        for (var s = 0; s <= 30; s += 3) events.Add(Cast(s));
        events.Add(new CombatEvent { Timestamp = T0.AddSeconds(31), Type = CombatEventType.ZoneChange, TargetName = "Next Zone" });
        for (var s = 32; s <= 62; s += 3) events.Add(Cast(s));

        var segmenter = new EncounterSegmenter(new SegmenterOptions { IdleGapSeconds = 60, MinPullSeconds = 15 });
        var pulls = await segmenter.SegmentAsync(Stream(events));

        Assert.Equal(2, pulls.Count);
        Assert.Equal("Next Zone", pulls[1].ZoneName);
    }
}
