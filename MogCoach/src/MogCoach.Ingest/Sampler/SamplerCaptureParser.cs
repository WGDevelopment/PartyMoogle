using System.Globalization;
using System.Text.Json;
using MogCoach.Core.Model;

namespace MogCoach.Ingest.Sampler;

/// <summary>Parsing helpers for the .mogcap JSONL format written by the MogCoach.Recorder plugin.</summary>
internal static class SamplerCaptureParser
{
    public static string? Kind(JsonElement line) =>
        line.TryGetProperty("k", out var k) ? k.GetString() : null;

    public static DateTimeOffset Timestamp(JsonElement line) =>
        line.TryGetProperty("ts", out var ts) &&
        DateTimeOffset.TryParse(ts.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t)
            ? t : default;

    public static WorldSnapshot ParseSnapshot(JsonElement line)
    {
        var actors = new List<ActorSnapshot>();
        if (line.TryGetProperty("actors", out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var a in arr.EnumerateArray())
                actors.Add(ParseActor(a));

        return new WorldSnapshot { Timestamp = Timestamp(line), Actors = actors };
    }

    private static ActorSnapshot ParseActor(JsonElement a)
    {
        return new ActorSnapshot
        {
            Id = Str(a, "id") ?? "",
            Name = Str(a, "name"),
            Kind = ParseKind(Str(a, "kind")),
            X = Flt(a, "x"),
            Z = Flt(a, "z"),
            Heading = Flt(a, "h"),
            Hp = Lng(a, "hp"),
            HpMax = Lng(a, "hpm"),
            TargetId = Str(a, "tgt"),
            Cast = ParseCast(a),
            Statuses = ParseStatuses(a),
            Gauge = ParseGauge(a),
        };
    }

    private static CastState? ParseCast(JsonElement a)
    {
        if (!a.TryGetProperty("cast", out var c) || c.ValueKind != JsonValueKind.Object) return null;
        return new CastState
        {
            AbilityId = Str(c, "id") ?? "",
            AbilityName = Str(c, "name"),
            Elapsed = Flt(c, "el"),
            Total = Flt(c, "tot"),
            TargetId = Str(c, "tgt"),
        };
    }

    private static IReadOnlyList<StatusState> ParseStatuses(JsonElement a)
    {
        if (!a.TryGetProperty("st", out var st) || st.ValueKind != JsonValueKind.Array) return [];
        var list = new List<StatusState>();
        foreach (var s in st.EnumerateArray())
            list.Add(new StatusState
            {
                Id = Str(s, "id") ?? "",
                Name = Str(s, "name"),
                Remaining = Flt(s, "rem"),
                Stacks = (int)Lng(s, "stk"),
                SourceId = Str(s, "src"),
            });
        return list;
    }

    private static IReadOnlyDictionary<string, double>? ParseGauge(JsonElement a)
    {
        if (!a.TryGetProperty("gauge", out var g) || g.ValueKind != JsonValueKind.Object) return null;
        var d = new Dictionary<string, double>();
        foreach (var p in g.EnumerateObject())
            if (p.Value.ValueKind == JsonValueKind.Number) d[p.Name] = p.Value.GetDouble();
        return d;
    }

    private static ActorKind ParseKind(string? k) => k switch
    {
        "self" => ActorKind.Self,
        "party" => ActorKind.Party,
        "enemy" => ActorKind.Enemy,
        _ => ActorKind.Other,
    };

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static float Flt(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetSingle() : 0f;

    private static long Lng(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0L;
}
