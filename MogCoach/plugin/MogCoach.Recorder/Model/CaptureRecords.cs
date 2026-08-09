using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MogCoach.Recorder.Model;

// Wire DTOs for the .mogcap JSONL format. Keys are short to keep 10 Hz captures compact.
// The MogCoach analyzer (MogCoach.Ingest) reads the mirror of these. Keep the two in sync.

public sealed class HeaderRecord
{
    [JsonPropertyName("k")] public string K => "hdr";
    [JsonPropertyName("ver")] public int Ver { get; set; } = 1;
    [JsonPropertyName("ts")] public string Ts { get; set; } = "";
    [JsonPropertyName("player")] public string? Player { get; set; }
    [JsonPropertyName("playerId")] public string? PlayerId { get; set; }
    [JsonPropertyName("job")] public string? Job { get; set; }
    [JsonPropertyName("sampleHz")] public int SampleHz { get; set; }
}

public sealed class SnapshotRecord
{
    [JsonPropertyName("k")] public string K => "snap";
    [JsonPropertyName("ts")] public string Ts { get; set; } = "";
    [JsonPropertyName("actors")] public List<ActorRecord> Actors { get; set; } = new();
}

public sealed class EventRecord
{
    [JsonPropertyName("k")] public string K => "evt";
    [JsonPropertyName("ts")] public string Ts { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("cleared")] public bool? Cleared { get; set; }
    [JsonPropertyName("zone")] public string? Zone { get; set; }
}

public sealed class ActorRecord
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("kind")] public string Kind { get; set; } = "other"; // self|party|enemy|other
    [JsonPropertyName("x")] public float X { get; set; }
    [JsonPropertyName("z")] public float Z { get; set; }
    [JsonPropertyName("h")] public float H { get; set; } // heading, radians
    [JsonPropertyName("hp")] public uint Hp { get; set; }
    [JsonPropertyName("hpm")] public uint Hpm { get; set; }
    [JsonPropertyName("tgt")] public string? Tgt { get; set; }
    [JsonPropertyName("cast")] public CastRecord? Cast { get; set; }
    [JsonPropertyName("st")] public List<StatusRecord>? St { get; set; }
    [JsonPropertyName("gauge")] public Dictionary<string, double>? Gauge { get; set; }
}

public sealed class CastRecord
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("el")] public float El { get; set; }  // elapsed seconds
    [JsonPropertyName("tot")] public float Tot { get; set; } // total seconds
    [JsonPropertyName("tgt")] public string? Tgt { get; set; }
}

public sealed class StatusRecord
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("rem")] public float Rem { get; set; } // remaining seconds
    [JsonPropertyName("stk")] public int Stk { get; set; }
    [JsonPropertyName("src")] public string? Src { get; set; }
}
