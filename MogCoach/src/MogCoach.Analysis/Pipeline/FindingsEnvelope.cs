using System.Text.Json;
using System.Text.Json.Serialization;
using MogCoach.Core.Model;

namespace MogCoach.Analysis.Pipeline;

/// <summary>Wire shape the LLM returns; mapped to domain <see cref="Finding"/>s.</summary>
internal sealed record FindingsEnvelope
{
    [JsonPropertyName("summary")] public string? Summary { get; init; }
    [JsonPropertyName("findings")] public List<FindingDto>? Findings { get; init; }

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: true) },
    };

    /// <summary>
    /// Parses a model response into an envelope, tolerating models that wrap JSON in prose or code
    /// fences by extracting the outermost JSON object. Returns an empty envelope on failure.
    /// </summary>
    public static FindingsEnvelope Parse(string content)
    {
        var json = ExtractJsonObject(content);
        if (json is null) return new FindingsEnvelope();
        try
        {
            return JsonSerializer.Deserialize<FindingsEnvelope>(json, JsonOpts) ?? new FindingsEnvelope();
        }
        catch (JsonException)
        {
            return new FindingsEnvelope();
        }
    }

    private static string? ExtractJsonObject(string s)
    {
        var start = s.IndexOf('{');
        var end = s.LastIndexOf('}');
        return start >= 0 && end > start ? s[start..(end + 1)] : null;
    }

    public IReadOnlyList<Finding> ToFindings(string origin, string? evidenceImagePath = null)
    {
        if (Findings is null) return [];
        var list = new List<Finding>(Findings.Count);
        foreach (var f in Findings)
        {
            list.Add(new Finding
            {
                Severity = ParseEnum(f.Severity, Severity.Minor),
                Category = ParseEnum(f.Category, FindingCategory.Other),
                Title = f.Title ?? "(untitled)",
                Detail = f.Detail ?? string.Empty,
                Recommendation = f.Recommendation ?? string.Empty,
                PullOffsetSeconds = f.PullOffsetSeconds,
                Origin = origin,
                EvidenceImagePath = evidenceImagePath,
            });
        }
        return list;
    }

    private static TEnum ParseEnum<TEnum>(string? value, TEnum fallback) where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(value, ignoreCase: true, out var v) ? v : fallback;

    internal sealed record FindingDto
    {
        [JsonPropertyName("severity")] public string? Severity { get; init; }
        [JsonPropertyName("category")] public string? Category { get; init; }
        [JsonPropertyName("title")] public string? Title { get; init; }
        [JsonPropertyName("detail")] public string? Detail { get; init; }
        [JsonPropertyName("recommendation")] public string? Recommendation { get; init; }
        [JsonPropertyName("pullOffsetSeconds")] public double? PullOffsetSeconds { get; init; }
    }
}
