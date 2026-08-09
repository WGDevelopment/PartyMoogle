using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MogCoach.Core.Abstractions;
using MogCoach.Core.Model;

namespace MogCoach.References.FfLogs;

/// <summary>
/// <see cref="IBenchmarkProvider"/> over the FFLogs v2 GraphQL API. Handles OAuth2 client-credentials
/// token acquisition/caching and generic GraphQL execution, and benchmarks a fight from the configured
/// character's own parses via <c>characterData.character.zoneRankings</c> — returning best rDPS + its
/// percentile, median-parse percentile, and kills (see <see cref="FfLogsOptions.EncounterIds"/> for how
/// the encounter entry is matched). Returns null (rather than throwing) whenever a benchmark can't be
/// resolved (unconfigured, character/logs not found, encounter unmapped), so analysis proceeds.
/// </summary>
public sealed class FfLogsClient : IBenchmarkProvider
{
    private readonly HttpClient _http;
    private readonly FfLogsOptions _opts;
    private readonly ILogger<FfLogsClient> _log;

    private string? _token;
    private DateTimeOffset _tokenExpiresAt;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    public FfLogsClient(HttpClient http, IOptions<FfLogsOptions> opts, ILogger<FfLogsClient> log)
    {
        _http = http;
        _opts = opts.Value;
        _log = log;
    }

    private static readonly string[] AllowedMetrics = ["rdps", "adps", "ndps", "dps"];

    public async Task<FightBenchmark?> GetBenchmarkAsync(string encounter, Job job, CancellationToken ct = default)
    {
        if (!_opts.IsConfigured)
        {
            _log.LogInformation("FFLogs not configured (needs client + character + zone); skipping benchmark.");
            return null;
        }
        if (!_opts.EncounterIds.TryGetValue(encounter, out var encounterId))
        {
            _log.LogInformation("No FFLogs encounter id mapped for '{Encounter}'; skipping benchmark.", encounter);
            return null;
        }

        var metric = AllowedMetrics.Contains(_opts.Metric?.ToLowerInvariant()) ? _opts.Metric!.ToLowerInvariant() : "rdps";
        var spec = job != Job.Unknown ? job.ToString() : null;

        try
        {
            var query = BuildZoneRankingsQuery(metric, spec is not null);
            var vars = new JsonObject
            {
                ["name"] = _opts.CharacterName,
                ["server"] = _opts.Server,
                ["region"] = _opts.Region,
                ["zone"] = _opts.ZoneId,
                ["difficulty"] = _opts.Difficulty,
            };
            if (spec is not null) vars["spec"] = spec;

            var data = await QueryAsync(query, vars, ct).ConfigureAwait(false);
            return ParseCharacterBenchmark(data, encounter, encounterId, job);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "FFLogs benchmark query failed for {Encounter}", encounter);
            return null;
        }
    }

    /// <summary>Executes an arbitrary GraphQL query against the FFLogs client endpoint.</summary>
    public async Task<JsonElement> QueryAsync(string query, JsonObject? variables, CancellationToken ct = default)
    {
        var token = await GetTokenAsync(ct).ConfigureAwait(false);

        var payload = new JsonObject { ["query"] = query };
        if (variables is not null) payload["variables"] = variables;

        using var req = new HttpRequestMessage(HttpMethod.Post, _opts.ApiUrl)
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(body);
        // Clone so the element survives disposal of the document.
        return doc.RootElement.Clone();
    }

    private async Task<string> GetTokenAsync(CancellationToken ct)
    {
        if (_token is not null && DateTimeOffset.UtcNow < _tokenExpiresAt) return _token;

        await _tokenLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_token is not null && DateTimeOffset.UtcNow < _tokenExpiresAt) return _token;

            using var req = new HttpRequestMessage(HttpMethod.Post, _opts.TokenUrl)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                }),
            };
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_opts.ClientId}:{_opts.ClientSecret}"));
            req.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);

            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            _token = root.GetProperty("access_token").GetString()
                     ?? throw new InvalidOperationException("FFLogs token response missing access_token.");
            var expiresIn = root.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 3600;
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn - 60);
            return _token;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    /// <summary>
    /// Character zoneRankings query. Metric is an enum literal (inlined, allowlisted). zoneRankings
    /// returns a JSON scalar whose <c>rankings</c> array has one entry per encounter in the zone.
    /// </summary>
    private static string BuildZoneRankingsQuery(string metric, bool hasSpec)
    {
        var specParam = hasSpec ? ", $spec: String" : "";
        var specArg = hasSpec ? ", specName: $spec" : "";
        return $$"""
            query($name: String!, $server: String!, $region: String!, $zone: Int!, $difficulty: Int!{{specParam}}) {
              characterData {
                character(name: $name, serverSlug: $server, serverRegion: $region) {
                  zoneRankings(zoneID: $zone, difficulty: $difficulty, metric: {{metric}}{{specArg}})
                }
              }
            }
            """;
    }

    private FightBenchmark? ParseCharacterBenchmark(JsonElement data, string encounter, int encounterId, Job job)
    {
        if (data.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
        {
            _log.LogWarning("FFLogs returned errors: {Errors}", errors.ToString());
            return null;
        }

        if (!data.TryGetProperty("data", out var d) ||
            !d.TryGetProperty("characterData", out var cd) ||
            !cd.TryGetProperty("character", out var ch) || ch.ValueKind != JsonValueKind.Object ||
            !ch.TryGetProperty("zoneRankings", out var zr) || zr.ValueKind != JsonValueKind.Object ||
            !zr.TryGetProperty("rankings", out var rankings) || rankings.ValueKind != JsonValueKind.Array)
        {
            _log.LogInformation("FFLogs: no zoneRankings for character (not found, hidden, or no kills).");
            return null;
        }

        foreach (var r in rankings.EnumerateArray())
        {
            if (!r.TryGetProperty("encounter", out var enc) ||
                !enc.TryGetProperty("id", out var idEl) || idEl.GetInt32() != encounterId)
                continue;

            return new FightBenchmark
            {
                Encounter = encounter,
                Job = job,
                BestRdps = GetNullableDouble(r, "bestAmount"),
                BestPercentile = GetNullableDouble(r, "rankPercent"),
                MedianPercentile = GetNullableDouble(r, "medianPercent"),
                Kills = r.TryGetProperty("totalKills", out var k) && k.ValueKind == JsonValueKind.Number ? k.GetInt32() : null,
            };
        }

        _log.LogInformation("FFLogs: character has no ranking for encounter {Id}.", encounterId);
        return null;
    }

    private static double? GetNullableDouble(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
}
