using System.Text;
using MogCoach.Core.Model;

namespace MogCoach.Analysis.Pipeline;

/// <summary>
/// Merges telemetry and vision findings into a single per-pull <see cref="CoachingReport"/>: dedupes
/// overlapping observations, orders by severity then time, and composes the summary. No LLM call —
/// fusion is deterministic so reports are stable and cheap.
/// </summary>
public sealed class FindingFuser
{
    public CoachingReport Fuse(
        Pull pull,
        CoachingMode mode,
        TelemetryAnalysisResult telemetry,
        IReadOnlyList<Finding> visionFindings)
    {
        var merged = telemetry.Findings.Concat(visionFindings);

        var deduped = merged
            .GroupBy(f => (f.Category, Title: f.Title.Trim().ToLowerInvariant()))
            .Select(g => g.OrderByDescending(f => f.Severity).First())
            .OrderByDescending(f => f.Severity)
            .ThenBy(f => f.PullOffsetSeconds ?? double.MaxValue)
            .ToList();

        return new CoachingReport
        {
            PullId = pull.Id,
            Mode = mode,
            Job = pull.Job,
            EncounterName = pull.EncounterName ?? pull.ZoneName,
            PullDuration = pull.Duration,
            Cleared = pull.Cleared,
            Metrics = telemetry.Metrics,
            Findings = deduped,
            Summary = ComposeSummary(pull, telemetry, deduped),
        };
    }

    private static string ComposeSummary(Pull pull, TelemetryAnalysisResult telemetry, IReadOnlyList<Finding> findings)
    {
        if (!string.IsNullOrWhiteSpace(telemetry.Summary))
            return telemetry.Summary;

        var sb = new StringBuilder();
        sb.Append(pull.Cleared ? "Clear" : "Wipe");
        sb.Append($" on {pull.EncounterName ?? pull.ZoneName} ({pull.Duration.TotalSeconds:F0}s). ");
        var critical = findings.Count(f => f.Severity == Severity.Critical);
        var major = findings.Count(f => f.Severity == Severity.Major);
        sb.Append($"{findings.Count} findings ({critical} critical, {major} major). ");
        if (telemetry.Metrics.DeathCount > 0)
            sb.Append($"{telemetry.Metrics.DeathCount} death(s). ");
        return sb.ToString().Trim();
    }
}
