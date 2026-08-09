using System.Runtime.CompilerServices;
using System.Text.Json;
using MogCoach.Core.Abstractions;
using MogCoach.Core.Model;

namespace MogCoach.Ingest.Sampler;

/// <summary>
/// <see cref="ITelemetrySource"/> over a sampler .mogcap file. Emits the discrete events
/// (pull start/end, death, zone) directly, and derives self cast-start events from consecutive
/// snapshots so the existing segmenter/rotation path works on a sampler-only capture.
/// <para>
/// Note: only cast-time actions surface here (they show as cast bars in snapshots); instant/oGCD
/// actions do not. For full rotation fidelity use an IINACT capture alongside, or enable the
/// sampler's action hook. Positional/mechanics analysis reads snapshots via <see cref="SamplerSnapshotProvider"/>.
/// </para>
/// </summary>
public sealed class SamplerCaptureSource(string path) : ITelemetrySource
{
    public async IAsyncEnumerable<CombatEvent> ReadAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Capture file not found: {path}", path);

        string? playerId = null;
        string? prevSelfCast = null;

        using var reader = new StreamReader(path);
        while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line)) continue;

            JsonDocument doc;
            try { doc = JsonDocument.Parse(line); }
            catch (JsonException) { continue; }
            using (doc)
            {
                var root = doc.RootElement;
                switch (SamplerCaptureParser.Kind(root))
                {
                    case "hdr":
                        playerId = root.TryGetProperty("playerId", out var pid) ? pid.GetString() : null;
                        break;

                    case "evt":
                        if (MapEvent(root, playerId, line) is { } ev) yield return ev;
                        break;

                    case "snap":
                        var snap = SamplerCaptureParser.ParseSnapshot(root);
                        var self = snap.Self;
                        var castId = self?.Cast?.AbilityId;
                        if (castId is not null && castId != prevSelfCast)
                        {
                            yield return new CombatEvent
                            {
                                Timestamp = snap.Timestamp,
                                Type = CombatEventType.Cast,
                                IsSelf = true,
                                SourceId = self!.Id,
                                SourceName = self.Name,
                                AbilityId = castId,
                                AbilityName = self.Cast?.AbilityName,
                                Raw = line,
                            };
                        }
                        prevSelfCast = castId;
                        break;
                }
            }
        }
    }

    private static CombatEvent? MapEvent(JsonElement e, string? playerId, string line)
    {
        var ts = SamplerCaptureParser.Timestamp(e);
        var type = e.TryGetProperty("type", out var t) ? t.GetString() : null;
        var id = e.TryGetProperty("id", out var i) ? i.GetString() : null;
        var name = e.TryGetProperty("name", out var n) ? n.GetString() : null;

        return type switch
        {
            "PullStart" => Base(ts, CombatEventType.EncounterStart, line),
            "PullEnd" => Base(ts, ClearedType(e), line),
            "Death" => Base(ts, CombatEventType.Death, line) with
            {
                TargetId = id, TargetName = name, IsSelf = id is not null && id == playerId,
            },
            "Zone" => Base(ts, CombatEventType.ZoneChange, line) with { TargetName = name },
            _ => null,
        };
    }

    private static CombatEventType ClearedType(JsonElement e) =>
        e.TryGetProperty("cleared", out var c) && c.ValueKind == JsonValueKind.False
            ? CombatEventType.Wipe
            : CombatEventType.EncounterEnd;

    private static CombatEvent Base(DateTimeOffset ts, CombatEventType type, string raw) =>
        new() { Timestamp = ts, Type = type, Raw = raw };
}
