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
        Assert.Equal([1, 2], groups[0].Items);
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

        var g = idx.GetOverlapGroups().Select(g => g.Items).ToList();

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
        Assert.Equal([1, 2], g[0].Items);
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

        var groups = idx.GetOverlapGroups().Select(g => g.Items).ToList();

        Assert.Equal(2, groups.Count);                    // one per segment
        Assert.Contains(groups, g => g.Count == 1 && g[0] == 1);
        Assert.Contains(groups, g => g.Count == 1 && g[0] == 2);
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
        var pair = groups.Select(g => g.Items).Single(g => g.Count == 2 &&
                                      g.Contains(lines[1]) &&
                                      g.Contains(lines[7]));
        Assert.NotNull(pair);
    }

    [Fact]
    public void VariableThicknessOverlaps_SplitCorrectly()
    {
        var inputFatLines = new[] {
            (new Line((0,0), (2,2)), 0.1),
            (new Line((1,1), (3,3)), 0.3),
            (new Line((3,3), (5,5)), 0.3),
            (new Line((7,7), (9,9)), 0.1),
        };

        // Diagonal but looks like this:
        // 0  1  2  3  4  5  6  7  8  9
        //    ------+------
        // =======              =======
        //    ------+------
        //
        // So we want two merge groups,
        // and the first merge group should have two fat lines:
        // 0-1 at 0.1, and 1-5 at 0.3.


        var idx = new OverlapIndex<Line>();
        foreach (var (line, thickness) in inputFatLines)
        {
            idx.AddItem(line, line, thickness);
        }

        var groups = idx.GetOverlapGroups();
        Assert.Equal(2, groups.Count);
        // One of the groups is a single line
        var groupsSorted = groups.OrderBy((g) => g.Items.Count);
        var singleGroup = groupsSorted.First();
        Assert.Single(singleGroup.Items);
        // The other is the other three lines
        var pairGroup = groupsSorted.Skip(1).First();
        Assert.Equal(3, pairGroup.Items.Count);
        // There should be two resulting fat lines from the group merged from 3 input lines
        var fatLines = pairGroup.FatLines;
        Assert.Equal(2, fatLines.Count);
        var fatlinesOrdered = fatLines.OrderBy((l) => l.Thickness);
        // One is from (0,0) to (1,1) with thickness 0.1
        var fatline1 = fatlinesOrdered.First();
        Assert.Equal(0.1, fatline1.Thickness);

        Assert.True(new Line((0, 0), (1, 1)).Equals(fatline1.Centerline));
        // The other is from (1,1) to (5,5) with thickness 0.3
        var fatline2 = fatlinesOrdered.Last();
        Assert.Equal(0.3, fatline2.Thickness);
        Assert.True(new Line((1, 1), (5, 5)).Equals(fatline2.Centerline));
    }
}