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

        var map = new Dictionary<string, AoeShape>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var text = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(text, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });

            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                _log.LogWarning("AoE shapes file {Path} is not a JSON object; ignoring.", path);
                return _cache = map;
            }

            // Iterate entries so comment/schema keys (e.g. "//") and any single malformed entry are
            // skipped without failing the whole file.
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Name.StartsWith("//", StringComparison.Ordinal)) continue;
                if (prop.Value.ValueKind != JsonValueKind.Object) continue;
                try
                {
                    var dto = prop.Value.Deserialize<ShapeDto>(JsonOpts);
                    if (dto is not null) map[prop.Name] = dto.ToModel();
                }
                catch (JsonException ex)
                {
                    _log.LogWarning(ex, "Skipping malformed AoE entry '{Key}' in {Path}", prop.Name, path);
                }
            }

            _log.LogInformation("Loaded {N} AoE shape definitions.", map.Count);
            return _cache = map;
        }
        catch (JsonException ex)
        {
            _log.LogWarning(ex, "Malformed AoE shapes file {Path}", path);
            return _cache = map;
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
