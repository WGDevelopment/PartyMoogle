namespace MogCoach.Core.Geometry;

/// <summary>
/// 2D geometry for "was the player inside this AoE" tests. This is what turns "visual signal vs
/// placement" into a data computation: given the caster's position + heading and the AoE shape, decide
/// whether a point (the player) was inside at resolution.
/// </summary>
public static class Geometry
{
    /// <summary>
    /// FFXIV facing → unit forward vector on the (X, Z) plane. VERIFY against your data: FFXIV's
    /// Rotation is radians with 0 facing south (+Z) and increasing clockwise, which gives
    /// (sin h, cos h). If cone/line directions come out mirrored, flip the signs here — one place.
    /// </summary>
    public static (float X, float Z) ForwardVector(float heading) =>
        (MathF.Sin(heading), MathF.Cos(heading));

    /// <summary>True if point (px,pz) is inside the AoE cast from (ox,oz) facing <paramref name="heading"/>.</summary>
    public static bool Contains(AoeShape shape, float ox, float oz, float heading, float px, float pz)
    {
        var dx = px - ox;
        var dz = pz - oz;
        var dist = MathF.Sqrt(dx * dx + dz * dz);

        return shape.Kind switch
        {
            AoeShapeKind.Circle => dist <= shape.Radius,
            AoeShapeKind.Donut => dist > shape.InnerRadius && dist <= shape.Radius,
            AoeShapeKind.Cone => dist <= shape.Radius && AngleFromFacing(heading, dx, dz) <= Deg2Rad(shape.HalfAngleDegrees),
            AoeShapeKind.Line => InLine(shape, heading, dx, dz),
            AoeShapeKind.Cross => InLine(shape, heading, dx, dz) || InLine(shape, heading + MathF.PI / 2f, dx, dz),
            _ => false,
        };
    }

    private static bool InLine(AoeShape shape, float heading, float dx, float dz)
    {
        var (fx, fz) = ForwardVector(heading);
        var along = dx * fx + dz * fz;            // projection onto facing axis
        if (along < 0 || along > shape.Length) return false;
        var lateral = MathF.Abs(dx * -fz + dz * fx); // perpendicular distance
        return lateral <= shape.HalfWidth;
    }

    /// <summary>Absolute angle (radians) between the facing direction and the vector to (dx,dz).</summary>
    private static float AngleFromFacing(float heading, float dx, float dz)
    {
        var (fx, fz) = ForwardVector(heading);
        var len = MathF.Sqrt(dx * dx + dz * dz);
        if (len < 1e-4f) return 0;
        var dot = (dx * fx + dz * fz) / len;
        return MathF.Acos(Math.Clamp(dot, -1f, 1f));
    }

    private static float Deg2Rad(float d) => d * MathF.PI / 180f;
}
