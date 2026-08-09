namespace MogCoach.Core.Model;

/// <summary>
/// A trusted rotation reference for a job (optionally encounter-specific), loaded from
/// curated sources (e.g. The Balance). Used by the rotation lens to compare actual play
/// against intended priority / opener.
/// </summary>
public sealed record RotationReference
{
    public required Job Job { get; init; }

    /// <summary>Optional encounter this reference is tuned for; null = generic.</summary>
    public string? Encounter { get; init; }

    /// <summary>Short provenance string, e.g. "The Balance — Endwalker WAR 6.5".</summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>Ordered opener, ability names.</summary>
    public IReadOnlyList<string> Opener { get; init; } = [];

    /// <summary>Steady-state priority list, highest first.</summary>
    public IReadOnlyList<string> Priority { get; init; } = [];

    /// <summary>Free-form coaching notes / rules (e.g. "hold gauge for burst windows").</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];
}

/// <summary>
/// Performance benchmark for an encounter+job, sourced from FFLogs. DPS values are rDPS
/// unless otherwise noted. Percentile brackets let the coach say "this pull was ~p60".
/// </summary>
public sealed record FightBenchmark
{
    public required string Encounter { get; init; }
    public required Job Job { get; init; }

    /// <summary>DPS at each percentile bracket (key = percentile, value = dps).</summary>
    public IReadOnlyDictionary<int, double> DpsByPercentile { get; init; } =
        new Dictionary<int, double>();

    public double? MedianDps => DpsByPercentile.TryGetValue(50, out var v) ? v : null;

    public string Source { get; init; } = "FFLogs";
}
