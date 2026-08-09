using Microsoft.Extensions.Logging;
using MogCoach.Core.Abstractions;
using MogCoach.Core.Model;

namespace MogCoach.Analysis.Pipeline;

/// <summary>
/// End-to-end orchestrator: ingest → segment → (per pull) telemetry analysis → keyframe select →
/// vision analysis → fuse. Pulls are processed sequentially: the VLM is the bottleneck and shares
/// the GPU with other services, so we deliberately avoid concurrent inference here.
/// </summary>
public sealed class CoachingPipeline(
    ITelemetrySourceFactory sourceFactory,
    IEncounterSegmenter segmenter,
    IFrameStore frameStore,
    IKeyframeSelector keyframeSelector,
    IReferenceProvider referenceProvider,
    IBenchmarkProvider benchmarkProvider,
    ISnapshotProvider snapshotProvider,
    TelemetryAnalyzer telemetryAnalyzer,
    VisionAnalyzer visionAnalyzer,
    PositionalAnalyzer positionalAnalyzer,
    FindingFuser fuser,
    ILogger<CoachingPipeline> log) : ICoachingPipeline
{
    public async Task<IReadOnlyList<CoachingReport>> RunAsync(CoachingRequest request, CancellationToken ct = default)
    {
        var source = sourceFactory.Create(request.CapturePath, request.PlayerName);
        var pulls = await segmenter.SegmentAsync(source.ReadAsync(ct), request.Job, ct).ConfigureAwait(false);

        if (request.OnlyPullId is { } only)
            pulls = pulls.Where(p => p.Id == only).ToList();

        // Continuous world snapshots (sampler .mogcap). Empty for IINACT-only captures.
        var snapshots = await snapshotProvider.ReadSnapshotsAsync(request.CapturePath, ct).ConfigureAwait(false);

        log.LogInformation("Segmented {Count} pull(s) from {Capture}; {Snaps} snapshot(s)",
            pulls.Count, request.CapturePath, snapshots.Count);

        var reports = new List<CoachingReport>(pulls.Count);
        foreach (var pull in pulls)
        {
            ct.ThrowIfCancellationRequested();
            log.LogInformation("Analyzing {Pull} ({Encounter}, {Dur:F0}s)",
                pull.Id, pull.EncounterName ?? pull.ZoneName, pull.Duration.TotalSeconds);

            var job = request.Job != Job.Unknown ? request.Job : pull.Job;
            var encounter = pull.EncounterName ?? pull.ZoneName;

            var rotation = await referenceProvider.GetRotationAsync(job, encounter, ct).ConfigureAwait(false);
            var benchmark = await benchmarkProvider.GetBenchmarkAsync(encounter, job, ct).ConfigureAwait(false);

            var telemetry = await telemetryAnalyzer
                .AnalyzeAsync(pull, request.Mode, rotation, benchmark, ct).ConfigureAwait(false);

            var extraFindings = new List<Finding>();

            // Positional/mechanics from the sampler snapshots that fall in this pull's window.
            var pullSnaps = snapshots
                .Where(s => s.Timestamp >= pull.StartedAt && s.Timestamp <= pull.EndedAt)
                .ToList();
            if (pullSnaps.Count > 0)
                extraFindings.AddRange(await positionalAnalyzer.AnalyzeAsync(pull, pullSnaps, ct).ConfigureAwait(false));

            if (request.EnableVision)
            {
                var keyframes = await keyframeSelector
                    .SelectAsync(pull, frameStore, request.Mode, ct).ConfigureAwait(false);
                log.LogInformation("{Pull}: {N} keyframe(s) for vision", pull.Id, keyframes.Count);
                extraFindings.AddRange(await visionAnalyzer.AnalyzeAsync(pull, keyframes, ct).ConfigureAwait(false));
            }

            reports.Add(fuser.Fuse(pull, request.Mode, telemetry, extraFindings));
        }

        return reports;
    }
}
