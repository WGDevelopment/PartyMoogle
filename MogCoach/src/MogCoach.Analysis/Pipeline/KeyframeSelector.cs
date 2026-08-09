using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MogCoach.Core.Abstractions;
using MogCoach.Core.Model;

namespace MogCoach.Analysis.Pipeline;

/// <summary>
/// Picks the moments worth looking at visually and attaches frames for each. Telemetry chooses the
/// timestamps (deaths, wipe); the frame store supplies the pictures. This is what keeps the vision
/// pass bounded — the VLM never scans the whole recording, only these few windows.
/// </summary>
public sealed class KeyframeSelector(IOptions<AnalysisOptions> options, ILogger<KeyframeSelector> log)
    : IKeyframeSelector
{
    private readonly AnalysisOptions _opts = options.Value;

    public async Task<IReadOnlyList<Keyframe>> SelectAsync(
        Pull pull, IFrameStore frames, CoachingMode mode, CancellationToken ct = default)
    {
        var moments = SelectMoments(pull, mode).Take(_opts.MaxKeyframes).ToList();
        var keyframes = new List<Keyframe>(moments.Count);

        foreach (var (ts, reason, label, trigger) in moments)
        {
            ct.ThrowIfCancellationRequested();
            var window = await frames.GetWindowAsync(
                ts,
                TimeSpan.FromSeconds(_opts.KeyframeWindowBeforeSeconds),
                TimeSpan.FromSeconds(_opts.KeyframeWindowAfterSeconds),
                ct).ConfigureAwait(false);

            // Keep at most FramesPerKeyframe, preferring the ones nearest the moment that have image bytes.
            var chosen = window
                .Where(f => f.ImageBytes is { Length: > 0 })
                .OrderBy(f => Math.Abs((f.Timestamp - ts).TotalMilliseconds))
                .Take(_opts.FramesPerKeyframe)
                .OrderBy(f => f.Timestamp)
                .ToList();

            if (chosen.Count == 0)
                log.LogInformation("No materialised frames for keyframe {Label} @ {Ts}", label, ts);

            keyframes.Add(new Keyframe
            {
                Timestamp = ts,
                Reason = reason,
                Label = label,
                Trigger = trigger,
                Frames = chosen,
            });
        }

        return keyframes;
    }

    private static IEnumerable<(DateTimeOffset Ts, KeyframeReason Reason, string Label, CombatEvent? Trigger)>
        SelectMoments(Pull pull, CoachingMode mode)
    {
        // Self deaths are always the highest-value visual moments.
        foreach (var e in pull.Events.Where(e => e.Type == CombatEventType.Death && e.IsSelf))
            yield return (e.Timestamp, KeyframeReason.Death,
                $"Your death{(e.SourceName is { } s ? $" — {s}" : "")}", e);

        // Mechanics mode also reviews party deaths (positioning context).
        if (mode == CoachingMode.Mechanics)
        {
            foreach (var e in pull.Events.Where(e => e.Type == CombatEventType.Death && !e.IsSelf))
                yield return (e.Timestamp, KeyframeReason.Death,
                    $"{e.TargetName} died{(e.SourceName is { } s ? $" — {s}" : "")}", e);
        }

        // The wipe moment.
        foreach (var e in pull.Events.Where(e => e.Type == CombatEventType.Wipe))
            yield return (e.Timestamp, KeyframeReason.Wipe, "Wipe", e);
    }
}
