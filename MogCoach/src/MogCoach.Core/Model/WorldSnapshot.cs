namespace MogCoach.Core.Model;

public enum ActorKind { Self, Party, Enemy, Other }

/// <summary>An actor's cast state at a moment (from the sampler's per-frame read).</summary>
public sealed record CastState
{
    public required string AbilityId { get; init; }
    public string? AbilityName { get; init; }
    public float Elapsed { get; init; }
    public float Total { get; init; }
    public string? TargetId { get; init; }

    /// <summary>Fraction of the cast completed (0..1).</summary>
    public float Progress => Total > 0 ? Math.Clamp(Elapsed / Total, 0, 1) : 0;
}

public sealed record StatusState
{
    public required string Id { get; init; }
    public string? Name { get; init; }
    public float Remaining { get; init; }
    public int Stacks { get; init; }
    public string? SourceId { get; init; }
}

/// <summary>One actor within a <see cref="WorldSnapshot"/>. Ground plane is X (east/west) × Z (north/south).</summary>
public sealed record ActorSnapshot
{
    public required string Id { get; init; }
    public string? Name { get; init; }
    public required ActorKind Kind { get; init; }

    public float X { get; init; }
    public float Z { get; init; }

    /// <summary>Facing, radians (FFXIV Rotation). See <c>Geometry.ForwardVector</c> for the convention.</summary>
    public float Heading { get; init; }

    public long Hp { get; init; }
    public long HpMax { get; init; }
    public string? TargetId { get; init; }

    public CastState? Cast { get; init; }
    public IReadOnlyList<StatusState> Statuses { get; init; } = [];
    public IReadOnlyDictionary<string, double>? Gauge { get; init; }
}

/// <summary>A single sampled instant of the world — the unit the positional passes consume.</summary>
public sealed record WorldSnapshot
{
    public required DateTimeOffset Timestamp { get; init; }
    public IReadOnlyList<ActorSnapshot> Actors { get; init; } = [];

    public ActorSnapshot? Self => Actors.FirstOrDefault(a => a.Kind == ActorKind.Self);
    public IEnumerable<ActorSnapshot> Enemies => Actors.Where(a => a.Kind == ActorKind.Enemy);
}
