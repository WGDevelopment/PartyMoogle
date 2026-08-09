namespace MogCoach.References.FfLogs;

/// <summary>
/// FFLogs v2 API credentials + the character/zone to benchmark against. Create a client at
/// https://www.fflogs.com/api/clients/ and supply the id/secret via config or env
/// (FFLOGS__CLIENTID / FFLOGS__CLIENTSECRET). Benchmarks use the character's own parses
/// (zoneRankings), so the character must have logs uploaded to FFLogs.
/// </summary>
public sealed class FfLogsOptions
{
    public const string SectionName = "FfLogs";

    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }

    public string TokenUrl { get; set; } = "https://www.fflogs.com/oauth/token";
    public string ApiUrl { get; set; } = "https://www.fflogs.com/api/v2/client";

    /// <summary>Character to benchmark (must exist on FFLogs).</summary>
    public string? CharacterName { get; set; }
    public string? Server { get; set; }       // world/server slug, e.g. "Gilgamesh"
    public string? Region { get; set; }       // "NA" | "EU" | "JP" | "OC" | "KR" | "CN"

    /// <summary>FFLogs zone id for the tier being progged (its encounters are returned together).</summary>
    public int ZoneId { get; set; }

    /// <summary>Difficulty id (savage is 101).</summary>
    public int Difficulty { get; set; } = 101;

    /// <summary>Ranking metric — rdps | adps | ndps | dps. Inlined into the query (allowlisted).</summary>
    public string Metric { get; set; } = "rdps";

    /// <summary>Map from our encounter names → FFLogs encounter ids, to pick the right zoneRankings entry.</summary>
    public Dictionary<string, int> EncounterIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret) &&
        !string.IsNullOrWhiteSpace(CharacterName) && !string.IsNullOrWhiteSpace(Server) &&
        !string.IsNullOrWhiteSpace(Region) && ZoneId > 0;
}
