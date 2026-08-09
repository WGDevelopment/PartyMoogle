using System.Net;
using System.Text;
using MogCoach.Core.Abstractions;
using MogCoach.Core.Model;

namespace MogCoach.Analysis.Report;

/// <summary>Renders coaching reports as a single self-styled HTML document.</summary>
public sealed class HtmlReportRenderer : IReportRenderer
{
    public string Extension => "html";

    public string Render(CoachingReport r) => Document("MogCoach — " + r.PullId, RenderBody(r));

    public string RenderSession(IReadOnlyList<CoachingReport> reports)
    {
        var body = new StringBuilder();
        body.Append($"<h1>MogCoach — Session Review</h1><p>{reports.Count} pull(s) analyzed.</p>");
        foreach (var r in reports)
            body.Append("<section class=\"pull\">").Append(RenderBody(r)).Append("</section>");
        return Document("MogCoach — Session Review", body.ToString());
    }

    private static string RenderBody(CoachingReport r)
    {
        var sb = new StringBuilder();
        sb.Append($"<h2>{Enc(r.PullId)} — {Enc(r.EncounterName ?? "")} " +
                  $"<span class=\"tag {(r.Cleared ? "clear" : "wipe")}\">{(r.Cleared ? "clear" : "wipe")}</span></h2>");
        sb.Append($"<p class=\"meta\">{r.Job} · {r.PullDuration.TotalSeconds:F0}s · {r.Mode}</p>");

        if (!string.IsNullOrWhiteSpace(r.Summary))
            sb.Append($"<blockquote>{Enc(r.Summary)}</blockquote>");

        sb.Append("<table><tbody>");
        Row(sb, "GCD uptime (approx)", r.Metrics.GcdUptime is { } u ? u.ToString("P0") : "n/a");
        Row(sb, "GCD drift (approx)", r.Metrics.GcdDriftSeconds is { } d ? $"{d:F1}s" : "n/a");
        Row(sb, "Deaths", r.Metrics.DeathCount.ToString());
        if (r.Metrics.BenchmarkDps is not null) Row(sb, "Benchmark median rDPS", r.Metrics.BenchmarkDps.Value.ToString("F0"));
        sb.Append("</tbody></table>");

        if (r.Findings.Count == 0)
        {
            sb.Append("<p><em>No findings.</em></p>");
            return sb.ToString();
        }

        sb.Append("<h3>Findings</h3>");
        foreach (var f in r.Findings)
        {
            sb.Append($"<div class=\"finding sev-{f.Severity.ToString().ToLowerInvariant()}\">");
            var at = f.PullOffsetSeconds is { } s ? $" · @{s:F0}s" : "";
            sb.Append($"<h4>{f.Severity.ToString().ToUpperInvariant()} — {Enc(f.Title)} " +
                      $"<span class=\"sub\">({f.Category} · {f.Origin}{at})</span></h4>");
            if (!string.IsNullOrWhiteSpace(f.Detail)) sb.Append($"<p>{Enc(f.Detail)}</p>");
            if (!string.IsNullOrWhiteSpace(f.Recommendation)) sb.Append($"<p class=\"fix\"><strong>Fix:</strong> {Enc(f.Recommendation)}</p>");
            if (!string.IsNullOrWhiteSpace(f.EvidenceImagePath))
                sb.Append($"<img src=\"{Enc(f.EvidenceImagePath)}\" alt=\"evidence\" />");
            sb.Append("</div>");
        }
        return sb.ToString();
    }

    private static void Row(StringBuilder sb, string k, string v) =>
        sb.Append($"<tr><th>{Enc(k)}</th><td>{Enc(v)}</td></tr>");

    private static string Enc(string s) => WebUtility.HtmlEncode(s);

    private static string Document(string title, string body) => $$"""
        <!doctype html>
        <html lang="en"><head><meta charset="utf-8" />
        <meta name="viewport" content="width=device-width, initial-scale=1" />
        <title>{{WebUtility.HtmlEncode(title)}}</title>
        <style>
          :root { color-scheme: light dark; }
          body { font: 15px/1.5 system-ui, sans-serif; max-width: 900px; margin: 2rem auto; padding: 0 1rem; }
          h2 { border-bottom: 1px solid #8884; padding-bottom: .3rem; margin-top: 2rem; }
          .meta { color: #8a8a8a; margin-top: -.5rem; }
          .tag { font-size: .7em; padding: .1em .5em; border-radius: .4em; vertical-align: middle; }
          .tag.clear { background: #2e7d3233; color: #2e7d32; }
          .tag.wipe { background: #c6282833; color: #c62828; }
          blockquote { border-left: 3px solid #8884; margin: 1rem 0; padding: .2rem 1rem; color: #9a9a9a; }
          table { border-collapse: collapse; margin: 1rem 0; }
          th, td { text-align: left; padding: .25rem .8rem; border-bottom: 1px solid #8882; }
          .finding { border-left: 4px solid #8888; padding: .2rem 1rem; margin: 1rem 0; border-radius: 0 .3em .3em 0; }
          .finding.sev-critical { border-color: #c62828; }
          .finding.sev-major { border-color: #ef6c00; }
          .finding.sev-minor { border-color: #f9a825; }
          .finding.sev-info { border-color: #1565c0; }
          .sub { font-weight: normal; font-size: .8em; color: #8a8a8a; }
          .fix { color: #2e7d32; }
          img { max-width: 100%; border-radius: .4em; margin: .5rem 0; }
        </style></head>
        <body>{{body}}</body></html>
        """;
}
