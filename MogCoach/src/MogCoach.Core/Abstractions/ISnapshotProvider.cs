using MogCoach.Core.Model;

namespace MogCoach.Core.Abstractions;

/// <summary>
/// Supplies the continuous world snapshots for a capture (the sampler's .mogcap). Returns an empty
/// list for captures that carry no snapshots (e.g. an IINACT-only .log), so the positional passes
/// simply no-op rather than failing.
/// </summary>
public interface ISnapshotProvider
{
    Task<IReadOnlyList<WorldSnapshot>> ReadSnapshotsAsync(string capturePath, CancellationToken ct = default);
}
