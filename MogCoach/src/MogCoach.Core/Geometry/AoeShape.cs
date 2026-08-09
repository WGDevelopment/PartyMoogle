namespace MogCoach.Core.Geometry;

public enum AoeShapeKind { Circle, Donut, Cone, Line, Cross }

/// <summary>
/// Geometry of a ground AoE, in yalms/degrees. Only the fields relevant to <see cref="Kind"/> are used
/// (Circle→Radius; Donut→Inner/Radius; Cone→Radius/HalfAngle; Line→Length/HalfWidth; Cross→Length/HalfWidth).
/// </summary>
public sealed record AoeShape
{
    public required AoeShapeKind Kind { get; init; }
    public float Radius { get; init; }
    public float InnerRadius { get; init; }
    public float HalfAngleDegrees { get; init; }
    public float Length { get; init; }
    public float HalfWidth { get; init; }
}
