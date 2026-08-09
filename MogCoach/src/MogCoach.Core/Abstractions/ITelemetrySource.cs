using MogCoach.Core.Model;

namespace MogCoach.Core.Abstractions;

/// <summary>
/// Yields normalised combat events for a captured session, in chronological order.
/// Implementations read an IINACT capture file (post-session) or tap the live WebSocket
/// (capture time). The pipeline consumes this via the segmenter.
/// </summary>
public interface ITelemetrySource
{
    IAsyncEnumerable<CombatEvent> ReadAsync(CancellationToken ct = default);
}
