using Elements;
using Elements.Geometry;

namespace WallsLOD200.Tests;

public class JoinVerticalStacksTest
{
    private static StandardWall Wall(double x0, double x1, int level, double thickness = 0.135) =>
        new(new Line((x0, 0, 0), (x1, 0, 0)), thickness, 3, null, new Transform().Moved(0, 0, level * 3.0));

    [Fact]
    public void StackChangesAlongWall_SplitsAtLevel()
    {
        // L1 and L2 run 0-10 m, L3 runs 0-6 m, L4 runs 2-6 m (the design doc's example).
        var walls = new[] { Wall(0, 10, 0), Wall(10, 0, 1), Wall(0, 6, 2), Wall(2, 6, 3) };

        var result = WallsLOD200.JoinVerticalStacks(walls)
            .Select(w => (w.Transform.Origin.Z, w.Height, w.CenterLine.Length()))
            .OrderBy(t => t.Z).ToList();

        Assert.Equal([(0.0, 6.0, 10.0), (6.0, 3.0, 6.0), (9.0, 3.0, 4.0)], result);
    }

    [Fact]
    public void DifferentThickness_DoesNotJoin()
    {
        var result = WallsLOD200.JoinVerticalStacks([Wall(0, 10, 0), Wall(0, 10, 1, 0.2)]);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void GapBetweenLevels_DoesNotJoin()
    {
        var result = WallsLOD200.JoinVerticalStacks([Wall(0, 10, 0), Wall(0, 10, 2)]);
        Assert.Equal(2, result.Count);
    }
}
