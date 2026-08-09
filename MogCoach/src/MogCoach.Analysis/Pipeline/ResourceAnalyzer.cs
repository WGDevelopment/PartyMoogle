using Microsoft.Extensions.Logging;
using MogCoach.Core.Model;

namespace MogCoach.Analysis.Pipeline;

/// <summary>
/// Buff/gauge pass over the sampler snapshots: job-gauge overcap (wasted resource generation) and
/// maintained-buff uptime (from the rotation reference's target list). Deterministic — no LLM call.
/// Both need the sampler's continuous state, which IINACT doesn't provide.
/// </summary>
public sealed class ResourceAnalyzer(ILogger<ResourceAnalyzer> log)
{
    private const double OvercapThresholdSeconds = 5.0;

    /// <summary>Gauge key → maximum value (a value at the cap is wasting further generation).</summary>
    private static readonly IReadOnlyDictionary<string, double> Caps = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
    {
        ["beastGauge"] = 100, ["oath"] = 100, ["kenki"] = 100, ["ninki"] = 100, ["soul"] = 100,
        ["shroud"] = 100, ["heat"] = 100, ["battery"] = 100, ["esprit"] = 100, ["whiteMana"] = 100,
        ["blackMana"] = 100, ["blood"] = 100, ["soulVoice"] = 100,
        ["chakra"] = 5, ["feathers"] = 4, ["repertoire"] = 4, ["lily"] = 3, ["bloodLily"] = 3,
        ["aetherflow"] = 3, ["addersgall"] = 3, ["addersting"] = 3, ["meditation"] = 3,
        ["polyglot"] = 3, ["firstmindsFocus"] = 2, ["ammo"] = 3,
    };

    public Task<IReadOnlyList<Finding>> AnalyzeAsync(
        Pull pull, IReadOnlyList<WorldSnapshot> snapshots, RotationReference? rotation, CancellationToken ct = default)
    {
        var findings = new List<Finding>();
        findings.AddRange(GaugeOvercap(snapshots));
        findings.AddRange(BuffUptime(snapshots, rotation));
        log.LogInformation("{Pull}: resource pass — {N} finding(s)", pull.Id, findings.Count);
        return Task.FromResult<IReadOnlyList<Finding>>(findings);
    }

    private static IEnumerable<Finding> GaugeOvercap(IReadOnlyList<WorldSnapshot> snapshots)
    {
        var overcap = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        DateTimeOffset prevTs = default;
        var havePrev = false;

        foreach (var snap in snapshots)
        {
            var self = snap.Self;
            if (self?.Gauge is { } gauge && havePrev)
            {
                var dt = (snap.Timestamp - prevTs).TotalSeconds;
                if (dt is > 0 and <= 1.0)
                    foreach (var (key, value) in gauge)
                        if (Caps.TryGetValue(key, out var cap) && value >= cap)
                            overcap[key] = overcap.GetValueOrDefault(key) + dt;
            }
            if (self is not null) { prevTs = snap.Timestamp; havePrev = true; }
        }

        foreach (var (key, seconds) in overcap.Where(kv => kv.Value > OvercapThresholdSeconds))
        {
            yield return new Finding
            {
                Severity = seconds > 15 ? Severity.Major : Severity.Minor,
                Category = FindingCategory.Resource,
                Title = $"Overcapped {key}",
                Detail = $"Your {key} gauge sat at its cap for ~{seconds:F0}s — generation past the cap is wasted.",
                Recommendation = $"Spend {key} before it caps (watch it during downtime and filler).",
                Origin = "resource",
            };
        }
    }

    private static IEnumerable<Finding> BuffUptime(IReadOnlyList<WorldSnapshot> snapshots, RotationReference? rotation)
    {
        if (rotation is null || rotation.MaintainedBuffs.Count == 0) yield break;

        var selfSnaps = snapshots.Where(s => s.Self is not null).ToList();
        if (selfSnaps.Count == 0) yield break;

        foreach (var buff in rotation.MaintainedBuffs)
        {
            var wanted = Normalize(buff.StatusId);
            var present = selfSnaps.Count(s => s.Self!.Statuses.Any(st => Normalize(st.Id) == wanted));
            var uptime = (double)present / selfSnaps.Count;
            if (uptime >= buff.TargetUptime) continue;

            yield return new Finding
            {
                Severity = uptime < buff.TargetUptime - 0.10 ? Severity.Major : Severity.Minor,
                Category = FindingCategory.Uptime,
                Title = $"Low uptime: {buff.Name ?? buff.StatusId}",
                Detail = $"{buff.Name ?? buff.StatusId} was up ~{uptime:P0} of the pull (target {buff.TargetUptime:P0}).",
                Recommendation = $"Refresh {buff.Name ?? "the buff"} before it drops to keep it near 100%.",
                Origin = "resource",
            };
        }
    }

    private static string Normalize(string hexId) => hexId.TrimStart('0').ToUpperInvariant();
}
