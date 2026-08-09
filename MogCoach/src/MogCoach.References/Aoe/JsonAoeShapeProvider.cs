using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MogCoach.Core.Abstractions;
using MogCoach.Core.Geometry;

namespace MogCoach.References.Aoe;

/// <summary>
/// Loads an ability-id → <see cref="AoeShape"/> map from a JSON file (see reference-data/aoe/shapes.json
/// for the schema). Cached after first load. Missing ids simply return null (no danger-zone finding).
/// </summary>
public sealed class JsonAoeShapeProvider : IAoeShapeProvider
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly ReferenceOptions _opts;
    private readonly ILogger<JsonAoeShapeProvider> _log;
    private Dictionary<string, AoeShape>? _cache;

    public JsonAoeShapeProvider(IOptions<ReferenceOptions> opts, ILogger<JsonAoeShapeProvider> log)
    {
        _opts = opts.Value;
        _log = log;
    }

    public async Task<AoeShape?> GetShapeAsync(string abilityId, CancellationToken ct = default)
    {
        var map = await LoadAsync(ct).ConfigureAwait(false);
        return map.TryGetValue(abilityId, out var shape)
            || map.TryGetValue(abilityId.TrimStart('0'), out shape)
            ? shape : null;
    }

    private async Task<Dictionary<string, AoeShape>> LoadAsync(CancellationToken ct)
    {
        if (_cache is not null) return _cache;

        var path = _opts.AoeShapesFile;
        if (!File.Exists(path))
        {
            _log.LogInformation("AoE shapes file not found ({Path}); positional danger-zone checks disabled.", path);
            return _cache = new Dictionary<string, AoeShape>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            await using var fs = File.OpenRead(path);
            var dto = await JsonSerializer.DeserializeAsync<Dictionary<string, ShapeDto>>(fs, JsonOpts, ct)
                .ConfigureAwait(false) ?? new();
            _cache = dto.ToDictionary(kv => kv.Key, kv => kv.Value.ToModel(), StringComparer.OrdinalIgnoreCase);
            _log.LogInformation("Loaded {N} AoE shape definitions.", _cache.Count);
            return _cache;
        }
        catch (JsonException ex)
        {
            _log.LogWarning(ex, "Malformed AoE shapes file {Path}", path);
            return _cache = new Dictionary<string, AoeShape>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private sealed record ShapeDto
    {
        [JsonPropertyName("kind")] public AoeShapeKind Kind { get; init; }
        [JsonPropertyName("radius")] public float Radius { get; init; }
        [JsonPropertyName("innerRadius")] public float InnerRadius { get; init; }
        [JsonPropertyName("halfAngleDegrees")] public float HalfAngleDegrees { get; init; }
        [JsonPropertyName("length")] public float Length { get; init; }
        [JsonPropertyName("halfWidth")] public float HalfWidth { get; init; }

        public AoeShape ToModel() => new()
        {
            Kind = Kind, Radius = Radius, InnerRadius = InnerRadius,
            HalfAngleDegrees = HalfAngleDegrees, Length = Length, HalfWidth = HalfWidth,
        };
    }
}
