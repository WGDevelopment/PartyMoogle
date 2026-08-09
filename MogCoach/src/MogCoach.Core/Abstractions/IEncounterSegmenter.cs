using MogCoach.Core.Model;

namespace MogCoach.Core.Abstractions;

/// <summary>Splits a flat event stream into discrete <see cref="Pull"/>s (encounter attempts).</summary>
public interface IEncounterSegmenter
{
    Task<IReadOnlyList<Pull>> SegmentAsync(
        IAsyncEnumerable<CombatEvent> events,
        Job job = Job.Unknown,
        CancellationToken ct = default);
}
