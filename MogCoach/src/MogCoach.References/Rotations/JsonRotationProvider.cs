using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MogCoach.Core.Abstractions;
using MogCoach.Core.Model;

namespace MogCoach.References.Rotations;

/// <summary>
/// Loads <see cref="RotationReference"/>s from JSON files in a directory. Files are curated by hand
/// from trusted sources (e.g. The Balance). See reference-data/rotations for the schema.
/// </summary>
public sealed class JsonRotationProvider : IReferenceProvider
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly ReferenceOptions _opts;
    private readonly ILogger<JsonRotationProvider> _log;

    public JsonRotationProvider(IOptions<ReferenceOptions> opts, ILogger<JsonRotationProvider> log)
    {
        _opts = opts.Value;
        _log = log;
    }

    public async Task<RotationReference?> GetRotationAsync(
        Job job, string? encounter = null, CancellationToken ct = default)
    {
        var dir = _opts.RotationsDirectory;
        if (!Directory.Exists(dir))
        {
            _log.LogWarning("Rotations directory not found: {Dir}", dir);
            return null;
        }

        foreach (var candidate in CandidateNames(job, encounter))
        {
            var path = Path.Combine(dir, candidate);
            if (!File.Exists(path)) continue;
            try
            {
                await using var fs = File.OpenRead(path);
                var dto = await JsonSerializer.DeserializeAsync<RotationDto>(fs, JsonOpts, ct).ConfigureAwait(false);
                if (dto is null) continue;
                _log.LogInformation("Loaded rotation reference {File}", candidate);
                return dto.ToModel(job);
            }
            catch (JsonException ex)
            {
                _log.LogWarning(ex, "Malformed rotation file {File}", candidate);
            }
        }

        _log.LogInformation("No rotation reference for {Job} / {Encounter}", job, encounter ?? "generic");
        return null;
    }

    private static IEnumerable<string> CandidateNames(Job job, string? encounter)
    {
        var j = job.ToString().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(encounter))
        {
            var e = Slug(encounter);
            yield return $"{j}.{e}.json";
        }
        yield return $"{j}.json";
    }

    private static string Slug(string s) =>
        new string(s.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray())
            .Trim('-');

    private sealed record RotationDto
    {
        [JsonPropertyName("encounter")] public string? Encounter { get; init; }
        [JsonPropertyName("source")] public string? Source { get; init; }
        [JsonPropertyName("opener")] public List<string>? Opener { get; init; }
        [JsonPropertyName("priority")] public List<string>? Priority { get; init; }
        [JsonPropertyName("notes")] public List<string>? Notes { get; init; }
        [JsonPropertyName("maintainedBuffs")] public List<MaintainedBuffDto>? MaintainedBuffs { get; init; }

        public RotationReference ToModel(Job job) => new()
        {
            Job = job,
            Encounter = Encounter,
            Source = Source ?? "unknown",
            Opener = Opener ?? [],
            Priority = Priority ?? [],
            Notes = Notes ?? [],
            MaintainedBuffs = MaintainedBuffs?.Select(b => b.ToModel()).ToList() ?? [],
        };
    }

    private sealed record MaintainedBuffDto
    {
        [JsonPropertyName("statusId")] public string? StatusId { get; init; }
        [JsonPropertyName("name")] public string? Name { get; init; }
        [JsonPropertyName("targetUptime")] public double? TargetUptime { get; init; }

        public MaintainedBuff ToModel() => new()
        {
            StatusId = StatusId ?? "",
            Name = Name,
            TargetUptime = TargetUptime ?? 0.95,
        };
    }
}
