using MogCoach.Core.Geometry;
using Xunit;

namespace MogCoach.Tests;

public class GeometryTests
{
    [Fact]
    public void Circle_contains_inside_not_outside()
    {
        var s = new AoeShape { Kind = AoeShapeKind.Circle, Radius = 6f };
        Assert.True(Geometry.Contains(s, 0, 0, 0, 3, 0));
        Assert.False(Geometry.Contains(s, 0, 0, 0, 10, 0));
    }

    [Fact]
    public void Donut_excludes_the_hole()
    {
        var s = new AoeShape { Kind = AoeShapeKind.Donut, InnerRadius = 6f, Radius = 30f };
        Assert.False(Geometry.Contains(s, 0, 0, 0, 3, 0));   // in the safe hole
        Assert.True(Geometry.Contains(s, 0, 0, 0, 10, 0));   // in the ring
        Assert.False(Geometry.Contains(s, 0, 0, 0, 40, 0));  // beyond the ring
    }

    [Fact]
    public void Cone_respects_facing_and_angle()
    {
        // heading 0 ⇒ forward (sin0, cos0) = (0, 1), i.e. +Z.
        var s = new AoeShape { Kind = AoeShapeKind.Cone, Radius = 40f, HalfAngleDegrees = 45f };
        Assert.True(Geometry.Contains(s, 0, 0, 0, 0, 10));   // straight ahead
        Assert.False(Geometry.Contains(s, 0, 0, 0, 10, 0));  // 90° to the side
    }

    [Fact]
    public void Line_respects_length_and_width()
    {
        var s = new AoeShape { Kind = AoeShapeKind.Line, Length = 50f, HalfWidth = 3f };
        Assert.True(Geometry.Contains(s, 0, 0, 0, 0, 10));   // on the axis, within length
        Assert.False(Geometry.Contains(s, 0, 0, 0, 5, 10));  // 5 yalms off-axis > halfWidth
        Assert.False(Geometry.Contains(s, 0, 0, 0, 0, 60));  // beyond length
    }
}
