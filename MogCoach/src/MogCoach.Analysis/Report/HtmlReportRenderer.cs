using System.Net;
using System.Text;
using MogCoach.Core.Abstractions;
using MogCoach.Core.Model;

namespace MogCoach.Analysis.Report;

/// <summary>Renders coaching reports as a single, self-contained, theme-aware HTML document.</summary>
public sealed class HtmlReportRenderer : IReportRenderer
{
    public string Extension => "html";

    public string Render(CoachingReport r) => Document(Title(r), RenderPull(r));

    public string RenderSession(IReadOnlyList<CoachingReport> reports)
    {
        var body = new StringBuilder();
        var totalFindings = reports.Sum(r => r.Findings.Count);
        var deaths = reports.Sum(r => r.Metrics.DeathCount);

        body.Append("<header class=\"top\">");
        body.Append("<h1>MogCoach — Session Review</h1>");
        body.Append($"<p class=\"sub\">{reports.Count} pull(s) · {totalFindings} finding(s) · {deaths} death(s)</p>");
        body.Append("</header>");

        foreach (var r in reports)
            body.Append("<section class=\"pull\">").Append(RenderPull(r)).Append("</section>");

        return Document("MogCoach — Session Review", body.ToString());
    }

    private static string Title(CoachingReport r) => $"MogCoach — {r.EncounterName ?? r.PullId}";

    private static string RenderPull(CoachingReport r)
    {
        var sb = new StringBuilder();
        var resultClass = r.Cleared ? "clear" : "wipe";
        var resultText = r.Cleared ? "CLEAR" : "WIPE";

        sb.Append("<div class=\"pull-head\">");
        sb.Append($"<h2>{Enc(r.EncounterName ?? "Unknown encounter")} <span class=\"pill {resultClass}\">{resultText}</span></h2>");
        sb.Append($"<p class=\"meta\">{Enc(r.PullId)} · {r.Job} · {r.PullDuration.TotalSeconds:F0}s · {r.Mode} mode</p>");
        sb.Append("</div>");

        if (!string.IsNullOrWhiteSpace(r.Summary))
            sb.Append($"<div class=\"summary\">{Enc(r.Summary)}</div>");

        // Metric tiles
        sb.Append("<div class=\"tiles\">");
        Tile(sb, "GCD uptime", r.Metrics.GcdUptime is { } u ? u.ToString("P0") : "—", "approx");
        Tile(sb, "GCD drift", r.Metrics.GcdDriftSeconds is { } d ? $"{d:F1}s" : "—", "approx");
        Tile(sb, "Deaths", r.Metrics.DeathCount.ToString(), r.Metrics.DeathCount == 0 ? "clean" : null);
        if (r.Metrics.BenchmarkDps is { } bd) Tile(sb, "Your best rDPS", bd.ToString("N0"), "FFLogs");
        sb.Append("</div>");

        if (r.Findings.Count == 0)
        {
            sb.Append("<p class=\"none\">No findings for this pull. 🎉</p>");
            return sb.ToString();
        }

        sb.Append($"<h3 class=\"findings-h\">Findings <span class=\"count\">{r.Findings.Count}</span></h3>");
        foreach (var f in r.Findings)
            sb.Append(RenderFinding(f));

        return sb.ToString();
    }

    private static string RenderFinding(Finding f)
    {
        var sev = f.Severity.ToString().ToLowerInvariant();
        var at = f.PullOffsetSeconds is { } s ? $" · <span class=\"at\">{s:F0}s</span>" : "";
        var sb = new StringBuilder();

        sb.Append($"<article class=\"finding sev-{sev}\">");
        sb.Append("<div class=\"f-top\">");
        sb.Append($"<span class=\"sevpill {sev}\">{f.Severity.ToString().ToUpperInvariant()}</span>");
        sb.Append($"<span class=\"tags\">{f.Category} · {f.Origin}{at}</span>");
        sb.Append("</div>");

        sb.Append($"<div class=\"f-title\">{Enc(f.Title)}</div>");
        if (!string.IsNullOrWhiteSpace(f.Detail))
            sb.Append($"<p class=\"f-detail\">{Enc(f.Detail)}</p>");
        if (!string.IsNullOrWhiteSpace(f.Recommendation))
            sb.Append($"<p class=\"f-fix\"><span class=\"fixlabel\">Fix</span>{Enc(f.Recommendation)}</p>");
        if (!string.IsNullOrWhiteSpace(f.EvidenceImagePath))
            sb.Append($"<img class=\"evidence\" src=\"{Enc(f.EvidenceImagePath!)}\" alt=\"evidence frame\" loading=\"lazy\" />");
        sb.Append("</article>");

        return sb.ToString();
    }

    private static void Tile(StringBuilder sb, string label, string value, string? note)
    {
        sb.Append("<div class=\"tile\">");
        sb.Append($"<div class=\"t-val\">{Enc(value)}</div>");
        sb.Append($"<div class=\"t-lab\">{Enc(label)}</div>");
        if (note is not null) sb.Append($"<div class=\"t-note\">{Enc(note)}</div>");
        sb.Append("</div>");
    }

    private static string Enc(string s) => WebUtility.HtmlEncode(s);

    private static string Document(string title, string body) => $$"""
        <!doctype html>
        <html lang="en"><head><meta charset="utf-8" />
        <meta name="viewport" content="width=device-width, initial-scale=1" />
        <title>{{WebUtility.HtmlEncode(title)}}</title>
        <style>
          :root {
            --bg:#f6f7f9; --card:#ffffff; --ink:#1c2024; --muted:#6b7280; --line:#e5e7eb;
            --accent:#4f46e5;
            --crit:#dc2626; --major:#ea580c; --minor:#ca8a04; --info:#2563eb; --good:#16a34a;
            --shadow:0 1px 2px rgba(0,0,0,.06), 0 1px 8px rgba(0,0,0,.04);
          }
          @media (prefers-color-scheme: dark) {
            :root:not([data-theme="light"]) {
              --bg:#0f1216; --card:#181c22; --ink:#e6e8eb; --muted:#9aa4b2; --line:#262c34;
              --accent:#818cf8;
              --crit:#f87171; --major:#fb923c; --minor:#facc15; --info:#60a5fa; --good:#4ade80;
              --shadow:0 1px 2px rgba(0,0,0,.4);
            }
          }
          * { box-sizing:border-box; }
          body { margin:0; background:var(--bg); color:var(--ink);
            font:15px/1.55 system-ui,-apple-system,Segoe UI,Roboto,sans-serif; }
          .wrap { max-width:820px; margin:0 auto; padding:28px 18px 64px; }
          .top h1 { margin:0 0 4px; font-size:1.5rem; letter-spacing:-.01em; }
          .top .sub { margin:0 0 8px; color:var(--muted); }
          .pull { margin-top:28px; }
          .pull-head h2 { margin:0 0 2px; font-size:1.25rem; display:flex; align-items:center; gap:.5rem; }
          .meta { margin:0 0 16px; color:var(--muted); font-size:.9rem; }
          .pill { font-size:.62rem; font-weight:700; letter-spacing:.06em; padding:.18em .5em;
            border-radius:999px; vertical-align:middle; }
          .pill.clear { background:color-mix(in srgb,var(--good) 18%,transparent); color:var(--good); }
          .pill.wipe  { background:color-mix(in srgb,var(--crit) 18%,transparent); color:var(--crit); }
          .summary { background:var(--card); border:1px solid var(--line); border-left:3px solid var(--accent);
            border-radius:10px; padding:12px 14px; margin:0 0 18px; box-shadow:var(--shadow); }
          .tiles { display:grid; grid-template-columns:repeat(auto-fit,minmax(120px,1fr)); gap:10px; margin:0 0 24px; }
          .tile { background:var(--card); border:1px solid var(--line); border-radius:10px; padding:12px 14px;
            box-shadow:var(--shadow); }
          .t-val { font-size:1.5rem; font-weight:650; letter-spacing:-.02em; }
          .t-lab { color:var(--muted); font-size:.82rem; margin-top:1px; }
          .t-note { color:var(--muted); font-size:.68rem; text-transform:uppercase; letter-spacing:.05em; margin-top:3px; opacity:.8; }
          .findings-h { font-size:1.05rem; margin:0 0 12px; display:flex; align-items:center; gap:.5rem; }
          .findings-h .count { background:var(--line); color:var(--muted); border-radius:999px;
            font-size:.75rem; padding:.05em .55em; font-weight:600; }
          .finding { background:var(--card); border:1px solid var(--line); border-left:4px solid var(--muted);
            border-radius:10px; padding:14px 16px; margin:0 0 12px; box-shadow:var(--shadow); }
          .finding.sev-critical { border-left-color:var(--crit); }
          .finding.sev-major { border-left-color:var(--major); }
          .finding.sev-minor { border-left-color:var(--minor); }
          .finding.sev-info  { border-left-color:var(--info); }
          .f-top { display:flex; align-items:center; gap:.6rem; margin-bottom:6px; }
          .sevpill { font-size:.64rem; font-weight:700; letter-spacing:.05em; padding:.16em .5em; border-radius:6px; color:#fff; }
          .sevpill.critical { background:var(--crit); } .sevpill.major { background:var(--major); }
          .sevpill.minor { background:var(--minor); color:#1c2024; } .sevpill.info { background:var(--info); }
          .tags { color:var(--muted); font-size:.8rem; }
          .tags .at { color:var(--ink); font-weight:600; }
          .f-title { font-weight:640; font-size:1.02rem; margin-bottom:4px; }
          .f-detail { margin:0 0 8px; color:var(--ink); }
          .f-fix { margin:0; background:color-mix(in srgb,var(--good) 12%,transparent); border-radius:8px;
            padding:8px 10px; }
          .fixlabel { display:inline-block; font-size:.64rem; font-weight:700; letter-spacing:.05em;
            text-transform:uppercase; color:var(--good); margin-right:.5rem; }
          .evidence { display:block; max-width:100%; border-radius:8px; margin-top:10px; border:1px solid var(--line); }
          .none { color:var(--good); background:color-mix(in srgb,var(--good) 10%,transparent);
            border-radius:10px; padding:14px 16px; }
        </style></head>
        <body><main class="wrap">{{body}}</main></body></html>
        """;
}
