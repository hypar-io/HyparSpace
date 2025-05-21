using Xunit;
using Elements.Geometry;
using System.Linq;

namespace WallsLOD200.Tests;

public class OverlapIndexTest
{
    /* ------------------------------------------------------------------ */
    /* 1 ▸ collinear, same direction – must group                         */
    /* ------------------------------------------------------------------ */
    [Fact]
    public void CollinearSegmentsOverlap_AreGrouped()
    {
        var idx = new OverlapIndex<int>();

        idx.AddItem(1,
            new Line(new Vector3(0, 0, 0), new Vector3(1, 1, 0)), 0.10);
        idx.AddItem(2,
            new Line(new Vector3(0.5, 0.5, 0), new Vector3(2, 2, 0)), 0.10);

        var groups = idx.GetOverlapGroups();

        Assert.Single(groups);                  // 1 group
        Assert.Equal([1, 2], groups[0].group);
    }

    /* ------------------------------------------------------------------ */
    /* 2 ▸ antiparallel but collinear – must still group                   */
    /* ------------------------------------------------------------------ */
    [Fact]
    public void AntiParallelSegmentsOverlap_AreGrouped()
    {
        var idx = new OverlapIndex<string>();

        idx.AddItem("A", new Line((0, 0, 0), (1, 0, 0)), 0.05);
        idx.AddItem("B", new Line((2, 0, 0), (1, 0, 0)), 0.05); // reversed

        var g = idx.GetOverlapGroups().Select(g => g.group).ToList();

        Assert.Single(g);
        Assert.Contains("A", g[0]);
        Assert.Contains("B", g[0]);
    }

    /* ------------------------------------------------------------------ */
    /* 3 ▸ variable thickness bridges gap in offset                       */
    /* ------------------------------------------------------------------ */
    [Fact]
    public void ThickAndThinSegments_StillGroupWhenStripsOverlap()
    {
        var idx = new OverlapIndex<int>();

        idx.AddItem(1, new Line((0, 0, 0), (1, 0, 0)), 0.20);   // y = 0
        idx.AddItem(2, new Line((0, 0.25, 0), (1, 0.25, 0)), 0.60); // y = 0.25

        var g = idx.GetOverlapGroups();

        Assert.Single(g);
        Assert.Equal([1, 2], g[0].group);
    }

    /* ------------------------------------------------------------------ */
    /* 4 ▸ same infinite line but disjoint intervals – two singletons     */
    /* ------------------------------------------------------------------ */
    [Fact]
    public void DisjointIntervals_ReturnSeparateSingletonGroups()
    {
        var idx = new OverlapIndex<int>();

        idx.AddItem(1, new Line((0, 0, 0), (1, 0, 0)), 0.10);
        idx.AddItem(2, new Line((5, 0, 0), (6, 0, 0)), 0.10);

        var groups = idx.GetOverlapGroups().Select(g => g.group).ToList();

        Assert.Equal(2, groups.Count);                    // one per segment
        Assert.Contains(groups, g => g.Length == 1 && g[0] == 1);
        Assert.Contains(groups, g => g.Length == 1 && g[0] == 2);
    }

    /* ------------------------------------------------------------------ */
    /* 5 ▸ real-world data set                                           */
    /* ------------------------------------------------------------------ */
    [Fact]
    public void VariableThicknessOverlaps_AreGroupedCorrectly()
    {
        var lines = new[] {
            new Line((-11.551756, -0.472737, 0), (-7.760806, -0.472737, 0)), // 0
            new Line((-7.760806, -0.472737, 0), (-7.760806,  3.318213, 0)), // 1
            new Line((-7.760806,  3.318213, 0), (-11.551756, 3.318213, 0)), // 2
            new Line((-11.551756, 3.318213, 0), (-11.551756,-0.472737, 0)), // 3
            new Line((-7.598881, -0.472737, 0), (-3.646006, -0.472737, 0)), // 4
            new Line((-3.646006, -0.472737, 0), (-3.646006,  3.318213, 0)), // 5
            new Line((-3.646006,  3.318213, 0), (-7.598881,  3.318213, 0)), // 6
            new Line((-7.598881,  3.318213, 0), (-7.598881, -0.472737, 0))  // 7 ★ thicker
        };

        var thickness = new[] { 0.13335, 0.13335, 0.13335, 0.13335,
                                0.13335, 0.13335, 0.13335, 0.4572 };

        var idx = new OverlapIndex<Line>();
        for (int i = 0; i < lines.Length; i++)
            idx.AddItem(lines[i], lines[i], thickness[i]);

        var groups = idx.GetOverlapGroups();

        // 8 segments → 1 merged pair (1 & 7) + 6 singletons
        Assert.Equal(7, groups.Count);

        // find the pair-group
        var pair = groups.Select(g => g.group).Single(g => g.Length == 2 &&
                                      g.Contains(lines[1]) &&
                                      g.Contains(lines[7]));
        Assert.NotNull(pair);
    }
}