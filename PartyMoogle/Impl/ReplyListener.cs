using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Utility;
using PartyMoogle.Reply;

namespace PartyMoogle.Impl;

/// <summary>
/// Long-lived subscriber to the dedicated inbound ntfy topic. Streams the topic's newline-
/// delimited JSON, filters to real messages newer than startup, dedupes by id, and hands each
/// off to <see cref="ReplySender"/>. Mirrors the On()/Off() convention of the other listeners.
///
/// Safety posture: refuses to arm without an authenticated topic+token; drops backlog older
/// than <see cref="startupUtc"/> and duplicate ids so ntfy's at-least-once delivery and
/// reconnect replays can't inject stale or repeated tells.
/// </summary>
public static class ReplyListener
{
    // No total timeout — this is a long-poll stream; cancellation is via the token.
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    private const int DedupeCap = 256;
    private static readonly object DedupeGate = new();
    private static readonly HashSet<string> Seen = new();
    private static readonly LinkedList<string> SeenOrder = new();

    private static CancellationTokenSource? cts;
    private static DateTime startupUtc;
    private static string? lastId;

    public static void On()
    {
        Off(); // idempotent: also the reconfigure path (Save -> On)

        var cfg = Plugin.Configuration;
        if (!cfg.ReplyEnabled)
            return;

        // Inbound MUST be authenticated (red-team C1). The full arming gate — reject ntfy.sh
        // and an anonymous-publish probe — lands in the hardening phase; this is the floor.
        if (cfg.ReplyTopic.IsNullOrWhitespace() || cfg.ReplyToken.IsNullOrWhitespace())
        {
            Service.PluginLog.Warning("Reply listener not armed: reply topic/token missing.");
            return;
        }

        if (!Uri.IsWellFormedUriString(cfg.NtfyServer, UriKind.Absolute))
        {
            Service.PluginLog.Warning("Reply listener not armed: NtfyServer is not a valid absolute URI.");
            return;
        }

        // Refuse the public server for an inbound command channel: on ntfy.sh a topic name is
        // the only secret, so anyone who learns it could inject /tells (red-team C1).
        var host = new Uri(cfg.NtfyServer).Host;
        if (host.Equals("ntfy.sh", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".ntfy.sh", StringComparison.OrdinalIgnoreCase))
        {
            Service.PluginLog.Warning(
                "Reply listener not armed: refusing public ntfy.sh for an inbound command channel. "
                + "Use a private, authenticated server.");
            return;
        }

        startupUtc = DateTime.UtcNow;
        lastId = null;
        lock (DedupeGate) { Seen.Clear(); SeenOrder.Clear(); }

        cts = new CancellationTokenSource();
        var ct = cts.Token;
        _ = Task.Run(() => RunAsync(ct), ct);
        Service.PluginLog.Debug("Reply listener armed.");
    }

    public static void Off()
    {
        try { cts?.Cancel(); }
        catch { /* already disposed */ }
        try { cts?.Dispose(); }
        catch { /* already disposed */ }
        cts = null;
    }

    private enum Probe { Ok, AnonWritable, Unreachable }

    private static async Task RunAsync(CancellationToken ct)
    {
        var cfg = Plugin.Configuration;
        var baseUrl = cfg.NtfyServer.TrimEnd('/');
        var backoff = 1;

        // Pre-flight: prove the reply topic rejects an UNauthenticated publish before we ever
        // stream from it (red-team C1). Anon-writable → hard disarm. Unreachable → back off and
        // retry (fail-closed: never stream until the topic is proven locked down).
        while (!ct.IsCancellationRequested)
        {
            var probe = await RunArmingProbe(baseUrl, cfg.ReplyTopic, ct);
            if (probe == Probe.Ok)
                break;
            if (probe == Probe.AnonWritable)
            {
                Service.PluginLog.Error(
                    "Reply listener DISARMED: the reply topic accepted an UNAUTHENTICATED publish — "
                    + "anyone could inject /tells. Lock the topic down (deny-all + token) and re-enable.");
                return;
            }

            if (ct.IsCancellationRequested)
                return;
            try { await Task.Delay(TimeSpan.FromSeconds(backoff), ct); }
            catch (OperationCanceledException) { return; }
            backoff = Math.Min(backoff * 2, 60);
        }

        backoff = 1;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var url = $"{baseUrl}/{cfg.ReplyTopic}/json";
                if (lastId is not null)
                    url += $"?since={Uri.EscapeDataString(lastId)}";

                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cfg.ReplyToken);

                using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                resp.EnsureSuccessStatusCode();

                await using var stream = await resp.Content.ReadAsStreamAsync(ct);
                using var reader = new StreamReader(stream);

                backoff = 1; // connected cleanly

                while (!ct.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(ct);
                    if (line is null)
                        break; // stream closed → reconnect
                    if (line.Length == 0)
                        continue;

                    ProcessLine(line);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Never log the token; only the reason.
                Service.PluginLog.Warning($"Reply listener stream error: {ex.Message}; retrying in {backoff}s.");
            }

            if (ct.IsCancellationRequested)
                break;

            try { await Task.Delay(TimeSpan.FromSeconds(backoff), ct); }
            catch (OperationCanceledException) { break; }
            backoff = Math.Min(backoff * 2, 60);
        }

        Service.PluginLog.Debug("Reply listener stopped.");
    }

    /// <summary>
    /// POST to the reply topic with NO auth header. A rejection (401/403) proves the topic is
    /// locked down (Ok). A success means anyone can publish (AnonWritable → must not arm). A
    /// network failure is inconclusive (Unreachable → retry). The probe body carries no valid
    /// "&lt;index&gt; " prefix, so even if it were ever processed it is rejected by BuildCommand.
    /// </summary>
    private static async Task<Probe> RunArmingProbe(string baseUrl, string topic, CancellationToken ct)
    {
        try
        {
            var url = $"{baseUrl}/{topic}";
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent("partymoogle arming probe - unauthenticated - ignore"),
            };
            // Deliberately no Authorization header.
            using var resp = await Http.SendAsync(req, ct);
            return resp.IsSuccessStatusCode ? Probe.AnonWritable : Probe.Ok;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return Probe.Unreachable;
        }
        catch (Exception ex)
        {
            Service.PluginLog.Warning($"Arming probe inconclusive ({ex.Message}); staying disarmed and retrying.");
            return Probe.Unreachable;
        }
    }

    private static void ProcessLine(string line)
    {
        NtfyMessage? msg;
        try
        {
            msg = JsonSerializer.Deserialize<NtfyMessage>(line);
        }
        catch (Exception ex)
        {
            Service.PluginLog.Debug($"Reply listener: skipping unparseable line ({ex.Message}).");
            return;
        }

        if (msg?.Id is null)
            return;

        // Advance the reconnect cursor on every event (incl. keepalive) so gap-recovery works.
        lastId = msg.Id;

        if (msg.Event != "message")
            return; // open / keepalive / poll_request

        // Backlog-replay guard (H1): never act on anything queued before this session armed.
        if (msg.Time > 0 && DateTimeOffset.FromUnixTimeSeconds(msg.Time).UtcDateTime < startupUtc)
        {
            Service.PluginLog.Debug($"Reply {msg.Id} dropped: predates startup.");
            return;
        }

        if (!MarkSeen(msg.Id))
        {
            Service.PluginLog.Debug($"Reply {msg.Id} dropped: duplicate id.");
            return;
        }

        if (string.IsNullOrEmpty(msg.Message))
            return;

        _ = ReplySender.HandleInbound(msg.Id, msg.Message);
    }

    /// <summary>Bounded-LRU id dedupe. True if new (and recorded); false if already seen.</summary>
    private static bool MarkSeen(string id)
    {
        lock (DedupeGate)
        {
            if (!Seen.Add(id))
                return false;

            SeenOrder.AddLast(id);
            if (SeenOrder.Count > DedupeCap)
            {
                var oldest = SeenOrder.First!.Value;
                SeenOrder.RemoveFirst();
                Seen.Remove(oldest);
            }

            return true;
        }
    }

    private sealed class NtfyMessage
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("time")] public long Time { get; set; }
        [JsonPropertyName("event")] public string? Event { get; set; }
        [JsonPropertyName("message")] public string? Message { get; set; }
    }
}
