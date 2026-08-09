using System.Text;
using MogCoach.Core.Abstractions;
using MogCoach.Core.Model;

namespace MogCoach.Analysis.Report;

/// <summary>Renders coaching reports as Markdown.</summary>
public sealed class MarkdownReportRenderer : IReportRenderer
{
    public string Extension => "md";

    public string RenderSession(IReadOnlyList<CoachingReport> reports)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# MogCoach — Session Review");
        sb.AppendLine();
        sb.AppendLine($"{reports.Count} pull(s) analyzed.");
        sb.AppendLine();
        foreach (var r in reports)
        {
            sb.AppendLine(Render(r));
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    public string Render(CoachingReport r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"## {r.PullId} — {r.EncounterName} ({(r.Cleared ? "clear" : "wipe")})");
        sb.AppendLine();
        sb.AppendLine($"**Job:** {r.Job}  ·  **Duration:** {r.PullDuration.TotalSeconds:F0}s  ·  **Mode:** {r.Mode}");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(r.Summary))
        {
            sb.AppendLine("> " + r.Summary.Replace("\n", "\n> "));
            sb.AppendLine();
        }

        sb.AppendLine("| Metric | Value |");
        sb.AppendLine("|---|---|");
        sb.AppendLine($"| GCD uptime (approx) | {Fmt(r.Metrics.GcdUptime, "P0")} |");
        sb.AppendLine($"| GCD drift (approx) | {FmtSeconds(r.Metrics.GcdDriftSeconds)} |");
        sb.AppendLine($"| Deaths | {r.Metrics.DeathCount} |");
        if (r.Metrics.BenchmarkDps is not null)
            sb.AppendLine($"| FFLogs best rDPS (yours) | {r.Metrics.BenchmarkDps:F0} |");
        if (r.Metrics.EstimatedPercentile is not null)
            sb.AppendLine($"| Estimated percentile | ~p{r.Metrics.EstimatedPercentile} |");
        sb.AppendLine();

        if (r.Findings.Count == 0)
        {
            sb.AppendLine("_No findings._");
            return sb.ToString();
        }

        sb.AppendLine("### Findings");
        sb.AppendLine();
        foreach (var f in r.Findings)
        {
            var at = f.PullOffsetSeconds is { } s ? $" · @{s:F0}s" : "";
            sb.AppendLine($"#### {Badge(f.Severity)} {f.Title}  <sub>({f.Category} · {f.Origin}{at})</sub>");
            if (!string.IsNullOrWhiteSpace(f.Detail)) sb.AppendLine(f.Detail);
            if (!string.IsNullOrWhiteSpace(f.Recommendation)) sb.AppendLine($"**Fix:** {f.Recommendation}");
            if (!string.IsNullOrWhiteSpace(f.EvidenceImagePath))
                sb.AppendLine($"![evidence]({f.EvidenceImagePath})");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string Badge(Severity s) => s switch
    {
        Severity.Critical => "🟥 CRITICAL",
        Severity.Major => "🟧 MAJOR",
        Severity.Minor => "🟨 MINOR",
        _ => "🟦 INFO",
    };

    private static string Fmt(double? v, string fmt) => v is { } d ? d.ToString(fmt) : "n/a";
    private static string FmtSeconds(double? v) => v is { } d ? $"{d:F1}s" : "n/a";
}
