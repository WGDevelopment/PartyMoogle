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
/// token acquisition/caching and generic GraphQL execution. The benchmark query itself is scaffolded:
/// FFLogs keys rankings by numeric encounter id (see <see cref="FfLogsOptions.EncounterIds"/>) and the
/// exact percentile→DPS extraction depends on the ranking metric you choose — finalise the query in
/// <see cref="BuildRankingsQuery"/> against the current schema (docs/SPEC.md, "FFLogs benchmarks").
/// Returns null (rather than throwing) whenever a benchmark can't be resolved, so analysis proceeds.
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

    public async Task<FightBenchmark?> GetBenchmarkAsync(string encounter, Job job, CancellationToken ct = default)
    {
        if (!_opts.IsConfigured)
        {
            _log.LogInformation("FFLogs not configured; skipping benchmark for {Encounter}", encounter);
            return null;
        }
        if (!_opts.EncounterIds.TryGetValue(encounter, out var encounterId))
        {
            _log.LogInformation("No FFLogs encounter id mapped for '{Encounter}'; skipping benchmark.", encounter);
            return null;
        }

        try
        {
            var query = BuildRankingsQuery();
            var vars = new JsonObject { ["encounterId"] = encounterId, ["specName"] = job.ToString() };
            var data = await QueryAsync(query, vars, ct).ConfigureAwait(false);
            return ParseBenchmark(data, encounter, job);
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
    /// TODO: finalise against the FFLogs v2 schema. Rankings come back as a JSON blob under
    /// worldData.encounter.characterRankings; choose your metric (rdps) and derive the percentile
    /// brackets from the returned distribution.
    /// </summary>
    private static string BuildRankingsQuery() => """
        query($encounterId: Int!, $specName: String!) {
          worldData {
            encounter(id: $encounterId) {
              name
              characterRankings(specName: $specName, metric: rdps)
            }
          }
        }
        """;

    private FightBenchmark? ParseBenchmark(JsonElement data, string encounter, Job job)
    {
        // characterRankings is returned as an opaque JSON value; shape varies. Parse defensively.
        if (!TryNavigate(data, out var rankings)) return null;

        var byPercentile = new Dictionary<int, double>();
        // Placeholder: real extraction depends on the rankings payload. Left empty on purpose so a
        // misparse yields "no benchmark" rather than fabricated numbers.
        _ = rankings;

        if (byPercentile.Count == 0)
        {
            _log.LogInformation("FFLogs returned rankings but percentile extraction is not yet implemented.");
            return null;
        }

        return new FightBenchmark { Encounter = encounter, Job = job, DpsByPercentile = byPercentile };
    }

    private static bool TryNavigate(JsonElement root, out JsonElement rankings)
    {
        rankings = default;
        if (root.TryGetProperty("data", out var d) &&
            d.TryGetProperty("worldData", out var w) &&
            w.TryGetProperty("encounter", out var enc) &&
            enc.TryGetProperty("characterRankings", out rankings))
        {
            return true;
        }
        return false;
    }
}
