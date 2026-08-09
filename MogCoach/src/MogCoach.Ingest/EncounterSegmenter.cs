using MogCoach.Core.Abstractions;
using MogCoach.Core.Model;

namespace MogCoach.Ingest;

/// <summary>Tuning for <see cref="EncounterSegmenter"/>.</summary>
public sealed class SegmenterOptions
{
    /// <summary>A quiet stretch longer than this (no cast/ability/death) ends the current pull.</summary>
    public double IdleGapSeconds { get; set; } = 20;

    /// <summary>Pulls shorter than this are discarded as noise (e.g. a single stray cast).</summary>
    public double MinPullSeconds { get; set; } = 15;
}

/// <summary>
/// Splits a flat event stream into pulls. Prefers explicit director markers
/// (<see cref="CombatEventType.EncounterStart"/> / <see cref="CombatEventType.EncounterEnd"/> /
/// <see cref="CombatEventType.Wipe"/>); falls back to combat-activity + idle-gap detection and
/// always breaks on zone changes. Director codes vary by content, so the fallback matters.
/// </summary>
public sealed class EncounterSegmenter(SegmenterOptions? options = null) : IEncounterSegmenter
{
    private readonly SegmenterOptions _opts = options ?? new SegmenterOptions();

    public async Task<IReadOnlyList<Pull>> SegmentAsync(
        IAsyncEnumerable<CombatEvent> events,
        Job job = Job.Unknown,
        CancellationToken ct = default)
    {
        // Materialise: segmentation needs look-back (gaps) and is cheap relative to the LLM passes.
        var all = new List<CombatEvent>();
        await foreach (var e in events.WithCancellation(ct))
            all.Add(e);
        all.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));

        var pulls = new List<Pull>();
        var current = new List<CombatEvent>();
        string zone = "Unknown";
        DateTimeOffset lastCombatAt = default;
        int index = 0;

        void Flush(bool cleared)
        {
            if (current.Count == 0) return;
            var start = current[0].Timestamp;
            var end = current[^1].Timestamp;
            if ((end - start).TotalSeconds >= _opts.MinPullSeconds)
            {
                index++;
                pulls.Add(new Pull
                {
                    Id = $"pull-{index:D2}",
                    ZoneName = zone,
                    EncounterName = zone == "Unknown" ? null : zone,
                    StartedAt = start,
                    EndedAt = end,
                    Job = job,
                    Cleared = cleared,
                    Events = current.ToArray(),
                });
            }
            current = [];
        }

        foreach (var e in all)
        {
            ct.ThrowIfCancellationRequested();

            switch (e.Type)
            {
                case CombatEventType.ZoneChange:
                    Flush(cleared: false);
                    zone = e.TargetName ?? zone;
                    lastCombatAt = default;
                    continue;

                case CombatEventType.EncounterStart:
                    // A new explicit start closes any open pull first.
                    if (current.Count > 0) Flush(cleared: false);
                    current.Add(e);
                    lastCombatAt = e.Timestamp;
                    continue;

                case CombatEventType.EncounterEnd:
                    current.Add(e);
                    Flush(cleared: true);
                    lastCombatAt = default;
                    continue;

                case CombatEventType.Wipe:
                    current.Add(e);
                    Flush(cleared: false);
                    lastCombatAt = default;
                    continue;
            }

            var isCombat = e.Type is CombatEventType.Cast or CombatEventType.Ability
                or CombatEventType.Death or CombatEventType.DotTick;

            // Idle-gap fallback: long silence between combat events ends the pull.
            if (isCombat && lastCombatAt != default &&
                (e.Timestamp - lastCombatAt).TotalSeconds > _opts.IdleGapSeconds)
            {
                Flush(cleared: false);
            }

            // Open a pull implicitly on the first combat activity when none is open.
            if (current.Count == 0 && !isCombat)
                continue; // don't start a pull on a stray status/chat line

            current.Add(e);
            if (isCombat) lastCombatAt = e.Timestamp;
        }

        Flush(cleared: false);
        return pulls;
    }
}
