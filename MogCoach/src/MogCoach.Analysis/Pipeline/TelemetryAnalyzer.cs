using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using MogCoach.Core.Abstractions;
using MogCoach.Core.Model;
using MogCoach.Analysis.Prompts;

namespace MogCoach.Analysis.Pipeline;

/// <summary>Result of the telemetry (text) pass for one pull.</summary>
public sealed record TelemetryAnalysisResult(
    PullMetrics Metrics,
    IReadOnlyList<Finding> Findings,
    string Summary);

/// <summary>
/// The text pass: computes objective metrics from the log, renders a compact pull digest, and asks
/// the LLM (text-only prompt) for rotation/mechanics findings against the trusted reference and
/// benchmark. Metrics are computed here (not by the LLM) so numbers are never hallucinated.
/// </summary>
public sealed class TelemetryAnalyzer(ILlmClient llm, ILogger<TelemetryAnalyzer> log)
{
    private const double AssumedGcdSeconds = 2.5;
    private const double DowntimeGapSeconds = 10.0; // gaps longer than this are treated as mechanics, not drift
    private const int MaxTimelineLines = 120;

    public async Task<TelemetryAnalysisResult> AnalyzeAsync(
        Pull pull,
        CoachingMode mode,
        RotationReference? rotation,
        FightBenchmark? benchmark,
        CancellationToken ct = default)
    {
        var metrics = ComputeMetrics(pull, benchmark);
        var digest = BuildDigest(pull, metrics, rotation, benchmark);

        var request = new LlmChatRequest
        {
            Messages =
            [
                LlmMessage.System(CoachPrompts.TelemetrySystem(mode)),
                LlmMessage.UserText(digest),
            ],
            JsonMode = true,
            Temperature = 0.2,
        };

        try
        {
            var resp = await llm.CompleteAsync(request, ct).ConfigureAwait(false);
            var env = FindingsEnvelope.Parse(resp.Content);
            return new TelemetryAnalysisResult(metrics, env.ToFindings("telemetry"), env.Summary ?? string.Empty);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Telemetry analysis failed for {Pull}", pull.Id);
            return new TelemetryAnalysisResult(metrics, [], string.Empty);
        }
    }

    private static PullMetrics ComputeMetrics(Pull pull, FightBenchmark? benchmark)
    {
        var selfCasts = pull.Events
            .Where(e => e.Type == CombatEventType.Cast && e.IsSelf)
            .Select(e => e.Timestamp)
            .OrderBy(t => t)
            .ToList();

        double onGcd = 0, drift = 0;
        for (var i = 1; i < selfCasts.Count; i++)
        {
            var gap = (selfCasts[i] - selfCasts[i - 1]).TotalSeconds;
            if (gap > DowntimeGapSeconds) continue; // mechanic downtime, not clipping
            onGcd += Math.Min(gap, AssumedGcdSeconds);
            drift += Math.Max(0, gap - AssumedGcdSeconds);
        }

        var duration = pull.Duration.TotalSeconds;
        double? uptime = duration > 0 && selfCasts.Count > 1 ? Math.Clamp(onGcd / duration, 0, 1) : null;
        var deaths = pull.Events.Count(e => e.Type == CombatEventType.Death && e.IsSelf);

        return new PullMetrics
        {
            GcdUptime = uptime,
            GcdDriftSeconds = selfCasts.Count > 1 ? drift : null,
            DeathCount = deaths,
            BenchmarkDps = benchmark?.MedianDps,
            // ActualDps intentionally null: derived DPS requires IINACT aggregated data, not log lines.
        };
    }

    private static string BuildDigest(Pull pull, PullMetrics m, RotationReference? rotation, FightBenchmark? benchmark)
    {
        var sb = new StringBuilder();
        var start = pull.StartedAt;

        sb.AppendLine("## Pull");
        sb.AppendLine($"Job: {pull.Job}  ({pull.Job.Role()})");
        sb.AppendLine($"Zone/Encounter: {pull.EncounterName ?? pull.ZoneName}");
        sb.AppendLine($"Duration: {pull.Duration.TotalSeconds:F0}s   Result: {(pull.Cleared ? "CLEAR" : "wipe")}");
        sb.AppendLine();

        sb.AppendLine("## Computed metrics (authoritative — do not contradict)");
        sb.AppendLine($"GCD uptime (approx): {(m.GcdUptime is { } u ? $"{u:P0}" : "n/a")}");
        sb.AppendLine($"GCD drift (approx): {(m.GcdDriftSeconds is { } d ? $"{d:F1}s" : "n/a")}");
        sb.AppendLine($"Self deaths: {m.DeathCount}");
        if (benchmark is not null)
        {
            sb.AppendLine($"Benchmark ({benchmark.Source}) median rDPS: {benchmark.MedianDps?.ToString("F0") ?? "n/a"}");
            sb.AppendLine("Benchmark percentiles: " +
                string.Join(", ", benchmark.DpsByPercentile.OrderBy(kv => kv.Key).Select(kv => $"p{kv.Key}={kv.Value:F0}")));
        }
        sb.AppendLine();

        // Deaths timeline (self + party), with the killing ability where known.
        var deaths = pull.Events.Where(e => e.Type == CombatEventType.Death).ToList();
        if (deaths.Count > 0)
        {
            sb.AppendLine("## Deaths");
            foreach (var e in deaths)
                sb.AppendLine($"[{Offset(start, e.Timestamp)}] {(e.IsSelf ? "YOU" : e.TargetName)} died" +
                              $"{(e.SourceName is { } s ? $" — killed by {s}" : "")}");
            sb.AppendLine();
        }

        sb.AppendLine("## Your cast timeline (offset s : ability)");
        var selfCasts = pull.Events.Where(e => e.Type == CombatEventType.Cast && e.IsSelf).ToList();
        var shown = 0;
        foreach (var e in selfCasts)
        {
            if (shown++ >= MaxTimelineLines) { sb.AppendLine($"... (+{selfCasts.Count - MaxTimelineLines} more)"); break; }
            sb.AppendLine($"[{Offset(start, e.Timestamp)}] {e.AbilityName ?? e.AbilityId ?? "?"}");
        }
        sb.AppendLine();

        if (rotation is not null)
        {
            sb.AppendLine("## Trusted rotation reference");
            sb.AppendLine($"Source: {rotation.Source}");
            if (rotation.Opener.Count > 0) sb.AppendLine("Opener: " + string.Join(" > ", rotation.Opener));
            if (rotation.Priority.Count > 0)
            {
                sb.AppendLine("Priority:");
                foreach (var p in rotation.Priority) sb.AppendLine($"  - {p}");
            }
            if (rotation.Notes.Count > 0)
            {
                sb.AppendLine("Notes:");
                foreach (var n in rotation.Notes) sb.AppendLine($"  - {n}");
            }
        }
        else
        {
            sb.AppendLine("## Trusted rotation reference: none available for this job/encounter.");
        }

        return sb.ToString();
    }

    private static string Offset(DateTimeOffset start, DateTimeOffset t) =>
        (t - start).TotalSeconds.ToString("F1", CultureInfo.InvariantCulture);
}
