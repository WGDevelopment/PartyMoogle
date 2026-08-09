using MogCoach.Core.Geometry;

namespace MogCoach.Core.Abstractions;

/// <summary>
/// Maps an ability id (hex) to its AoE geometry, so the positional pass can test whether the player
/// stood in it. This DB is the authoring cost of turning positioning into pure data; BossMod's
/// encounter modules are a good source to seed it from.
/// </summary>
public interface IAoeShapeProvider
{
    Task<AoeShape?> GetShapeAsync(string abilityId, CancellationToken ct = default);
}
