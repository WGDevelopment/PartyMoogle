using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MogCoach.Core.Abstractions;
using MogCoach.Core.Model;
using MogCoach.Ingest.Iinact;
using MogCoach.Ingest.Screenpipe;
using MogCoach.Llm;

namespace MogCoach.Cli;

/// <summary>
/// `mogcoach doctor` — checks each moving part of a from-scratch setup and reports what's missing:
/// the Screenpipe DB + schema, ffmpeg, the live IINACT WebSocket, and the LLM endpoint (including a
/// real image round-trip to confirm the model is actually vision-capable). Run it after each install
/// step. Exit code is non-zero if any hard check fails.
/// </summary>
public static class Doctor
{
    // 1x1 transparent PNG — a real image payload to exercise the multimodal request path end to end.
    private const string TinyPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

    public static async Task<int> RunAsync(IHost host, CliArgs a, CancellationToken ct)
    {
        var sp = host.Services;
        var screen = sp.GetRequiredService<IOptions<ScreenpipeOptions>>().Value;
        var llmOpts = sp.GetRequiredService<IOptions<OpenAiOptions>>().Value;
        var cfg = sp.GetRequiredService<IConfiguration>();
        var iinact = cfg.GetSection(IinactOptions.SectionName).Get<IinactOptions>() ?? new IinactOptions();
        var llm = sp.GetRequiredService<ILlmClient>();

        var anyFail = false;
        void Report(string level, string name, string detail)
        {
            if (level == "FAIL") anyFail = true;
            Console.WriteLine($"  [{level,-4}] {name,-22} {detail}");
        }

        Console.WriteLine("MogCoach doctor\n");
        Console.WriteLine("Config:");
        Console.WriteLine($"  LLM        {llmOpts.BaseUrl}  (model: {llmOpts.Model})");
        Console.WriteLine($"  Screenpipe {screen.DbPath}");
        Console.WriteLine($"  IINACT     {iinact.WebSocketUrl}\n");

        Console.WriteLine("Checks:");

        // --- Screenpipe DB + schema ---
        var sr = await ScreenpipeDiagnostics.RunAsync(screen, ct);
        if (!sr.FileExists)
            Report("FAIL", "Screenpipe DB", $"not found at {screen.DbPath} — is Screenpipe installed/running?");
        else if (sr.Error is not null)
            Report("FAIL", "Screenpipe DB", $"error: {sr.Error}");
        else if (sr.MissingTables.Count > 0)
            Report("FAIL", "Screenpipe schema", $"missing tables: {string.Join(", ", sr.MissingTables)}");
        else if (sr.MissingColumns.Count > 0)
            Report("FAIL", "Screenpipe schema", $"missing columns: {string.Join(", ", sr.MissingColumns)} (adjust the SQL)");
        else
            Report("OK", "Screenpipe DB", $"{sr.FrameCount:N0} frames, latest {ScreenpipeDiagnostics.FormatTimestamp(sr.LatestTimestamp)}");

        // --- ffmpeg ---
        var ff = await ProbeFfmpegAsync(screen.FfmpegPath, ct);
        Report(ff is null ? "WARN" : "OK", "ffmpeg",
            ff ?? $"'{screen.FfmpegPath}' not runnable — vision frames can't be extracted (telemetry still works)");

        // --- IINACT live probe ---
        var seconds = int.TryParse(a.Get("seconds"), out var s) ? Math.Clamp(s, 2, 60) : 8;
        Console.WriteLine($"  ....   IINACT               probing {iinact.WebSocketUrl} for {seconds}s (be in-game/combat for data)...");
        var pr = await IinactDiagnostics.ProbeAsync(new Uri(iinact.WebSocketUrl), TimeSpan.FromSeconds(seconds), ct);
        if (!pr.Connected)
            Report("FAIL", "IINACT WebSocket", $"cannot connect: {pr.Error} — is IINACT running with its WS server enabled?");
        else if (pr.LogLineCount == 0)
            Report("WARN", "IINACT WebSocket", "connected but no LogLine events (fine if idle in a town; retry during combat)");
        else
            Report("OK", "IINACT WebSocket", $"{pr.LogLineCount} LogLine event(s); e.g. {Truncate(pr.SampleRawLine, 60)}");

        // --- LLM endpoint: models list ---
        var models = await ProbeModelsAsync(llmOpts, ct);
        if (models is null)
            Report("FAIL", "LLM /models", $"no response from {llmOpts.BaseUrl} — is llama-server up?");
        else
            Report("OK", "LLM /models", models.Count == 0 ? "reachable" : string.Join(", ", models));

        // --- LLM text ping ---
        var (textOk, textErr) = await PingTextAsync(llm, ct);
        Report(textOk ? "OK" : "FAIL", "LLM text", textOk ? "chat completion responded" : $"failed: {textErr}");

        // --- LLM vision ping (real image round-trip) ---
        var (visionOk, visionErr) = await PingVisionAsync(llm, ct);
        Report(visionOk ? "OK" : "FAIL", "LLM vision",
            visionOk ? "accepted an image (multimodal path works)" : $"image request failed — is a VL model loaded? {visionErr}");

        Console.WriteLine();
        Console.WriteLine(anyFail ? "One or more checks FAILED — see above." : "All hard checks passed.");
        return anyFail ? 1 : 0;
    }

    private static async Task<string?> ProbeFfmpegAsync(string ffmpegPath, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-version");
            using var proc = Process.Start(psi);
            if (proc is null) return null;
            var line = await proc.StandardOutput.ReadLineAsync(ct).ConfigureAwait(false);
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
            return proc.ExitCode == 0 ? (line ?? "ok") : null;
        }
        catch { return null; }
    }

    private static async Task<IReadOnlyList<string>?> ProbeModelsAsync(OpenAiOptions opts, CancellationToken ct)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var b = opts.BaseUrl.TrimEnd('/');
            if (!b.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)) b += "/v1";
            using var resp = await http.GetAsync($"{b}/models", ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body);
            var ids = new List<string>();
            if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                foreach (var m in data.EnumerateArray())
                    if (m.TryGetProperty("id", out var id) && id.GetString() is { } sid) ids.Add(sid);
            return ids;
        }
        catch { return null; }
    }

    private static async Task<(bool ok, string? error)> PingTextAsync(ILlmClient llm, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            var r = await llm.CompleteAsync(new LlmChatRequest
            {
                Messages = [LlmMessage.System("Reply with exactly: OK"), LlmMessage.UserText("ping")],
                MaxTokens = 5,
            }, cts.Token).ConfigureAwait(false);
            return (!string.IsNullOrWhiteSpace(r.Content), null);
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    private static async Task<(bool ok, string? error)> PingVisionAsync(ILlmClient llm, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            var png = Convert.FromBase64String(TinyPngBase64);
            var r = await llm.CompleteAsync(new LlmChatRequest
            {
                Messages =
                [
                    LlmMessage.System("Reply with exactly: OK"),
                    LlmMessage.User(LlmContentPart.FromText("Reply OK."), LlmContentPart.FromImage(png, "image/png")),
                ],
                MaxTokens = 5,
            }, cts.Token).ConfigureAwait(false);
            return (!string.IsNullOrWhiteSpace(r.Content), null);
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    private static string Truncate(string? s, int max) =>
        string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s[..max] + "…";
}
