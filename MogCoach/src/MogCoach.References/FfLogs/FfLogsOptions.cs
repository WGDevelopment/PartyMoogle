namespace MogCoach.References.FfLogs;

/// <summary>
/// FFLogs v2 API credentials (OAuth2 client-credentials). Create a client at
/// https://www.fflogs.com/api/clients/ and supply the id/secret via config or env
/// (FFLOGS__CLIENTID / FFLOGS__CLIENTSECRET).
/// </summary>
public sealed class FfLogsOptions
{
    public const string SectionName = "FfLogs";

    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }

    public string TokenUrl { get; set; } = "https://www.fflogs.com/oauth/token";
    public string ApiUrl { get; set; } = "https://www.fflogs.com/api/v2/client";

    /// <summary>
    /// Map from our encounter names to FFLogs encounter ids. FFLogs rankings are keyed by numeric
    /// encounter id, so this must be populated for benchmarks to resolve. Empty by default.
    /// </summary>
    public Dictionary<string, int> EncounterIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Percentile brackets to request (must be values FFLogs exposes).</summary>
    public int[] Percentiles { get; set; } = [25, 50, 75, 95, 99];

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}
