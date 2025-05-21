using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Elements.Geometry;          // Vector3, Line

namespace WallsLOD200;

public readonly record struct FatLine(Line Centerline, double Thickness);

/// <summary>
/// Represents a group of overlapping items with their merged centerline and thickness
/// </summary>
/// <summary>
/// A connected set of overlapping segments and the fat-lines
/// obtained by slicing/merging them along the longitudinal axis.
/// </summary>
public class OverlapMergeGroup<T>
{
    public IReadOnlyList<T> Items { get; }
    public IReadOnlyList<FatLine> FatLines { get; }

    internal OverlapMergeGroup(List<SegmentRec<T>> segs)
    {
        Items = segs.Select(s => s.Payload).ToArray();

        /* ----  build the non-overlapping intervals  ------------------ */

        // 1. collect all break-points on the s-axis
        var sCuts = new SortedSet<double>();
        foreach (var s in segs) { sCuts.Add(s.MinS); sCuts.Add(s.MaxS); }
        var sList = sCuts.ToArray();

        // direction basis (same for whole group)
        double ang = segs[0].Angle;
        double cos = Math.Cos(ang);
        double sin = Math.Sin(ang);
        var u = new Vector3(cos, sin, 0);          // unit direction
        var n = new Vector3(-sin, cos, 0);          // unit normal

        var lines = new List<FatLine>();
        Line? open = null;
        double openThick = double.NaN;

        for (int i = 0; i < sList.Length - 1; i++)
        {
            double s0 = sList[i];
            double s1 = sList[i + 1];
            if (s1 - s0 < Vector3.EPSILON) continue;            // zero-length slot

            // 2.  which segments cover [s0,s1]?
            var active = segs.Where(s => s.MinS <= s0 + Vector3.EPSILON && s.MaxS >= s1 - Vector3.EPSILON)
                             .ToList();
            if (active.Count == 0) continue;

            // 3. perpendicular envelope for those segments
            double low = active.Min(s => s.Offset - s.PerpTol);
            double high = active.Max(s => s.Offset + s.PerpTol);
            double thick = high - low;
            double offC = 0.5 * (low + high);

            // 4. build centre-line for this slice
            var start = new Vector3(u.X * s0 + n.X * offC,
                                    u.Y * s0 + n.Y * offC, 0);
            var end = new Vector3(u.X * s1 + n.X * offC,
                                    u.Y * s1 + n.Y * offC, 0);
            if ((start - end).Length() < Vector3.EPSILON) continue;
            var slice = new Line(start, end);

            // 5. merge with previous slice if thickness matches
            if (open is not null && Math.Abs(thick - openThick) < Vector3.EPSILON)
            {
                open = new Line(open.Start, end);
            }
            else
            {
                if (open is not null)
                    lines.Add(new FatLine(open, openThick));
                open = slice;
                openThick = thick;
            }
        }
        if (open is not null)
            lines.Add(new FatLine(open, openThick));

        FatLines = lines;
    }
}

readonly struct SegmentRec<T>
{
    public SegmentRec(T payload, double angle, double offset,
                      double minS, double maxS, double perpTol)
    {
        Payload = payload; Angle = angle; Offset = offset;
        MinS = minS; MaxS = maxS; PerpTol = perpTol;
    }

    public readonly T Payload;
    public readonly double Angle;
    public readonly double Offset;
    public readonly double MinS, MaxS;
    public readonly double PerpTol;
}

/// <summary>
/// Groups arbitrary payload objects carried by 2-D line segments into "overlap groups".
/// A group is a connected component of:
/// • strips that are (anti-)parallel within <see cref="_angleTol"/>
/// • whose offset intervals overlap, taking each segment's own thickness into account
/// • whose scalar intervals along the line overlap
/// </summary>
/// <param name="angleTol">
/// Angular tolerance in **radians** for folding two directions into the same bucket
/// (defaults to 1 × 10⁻³ ≈ 0.057°).
/// </param>
public class OverlapIndex<T>(double angleTol = 1e-3, double longTol = 1e-6)
{

    /*---------------------------------------------------------------------
     * public API
     *-------------------------------------------------------------------*/

    /// <summary>Add one segment + payload.</summary>
    /// <param name="item">User data to carry along.</param>
    /// <param name="line">2-D segment (Z is ignored).</param>
    /// <param name="thickness">
    /// Full thickness of the "fat line".
    /// Internally we use half-thickness = thickness / 2.
    /// </param>
    public void AddItem(T item, Line line, double thickness)
    {
        var rec = Canonicalise(item, line, thickness * 0.5);
        _records.Add(rec);
    }

    /// <summary>
    /// Compute and return all overlap groups.
    /// Groups with a single member (i.e. nobody overlaps anybody) are omitted.
    /// </summary>
    /// <param name="thicknessTolerance">
    /// Extra gap you are willing to tolerate between two strips that *almost* touch.
    /// Set to 0 for strict behaviour.
    /// </param>
    public List<OverlapMergeGroup<T>>
    GetOverlapGroups(double thicknessTolerance = 0.0)
    {
        // 1.  Bucket by canonical direction
        var dirBuckets = new Dictionary<int, List<SegmentRec<T>>>();

        foreach (var r in _records)
        {
            int key = DirKey(r.Angle);
            if (!dirBuckets.TryGetValue(key, out var list))
            {
                list = [];
                dirBuckets.Add(key, list);
            }
            list.Add(r);
        }

        // 2.  Build fat-strip clusters in every direction bucket
        var result = new List<OverlapMergeGroup<T>>();

        foreach (var bucket in dirBuckets.Values)
        {
            // sort by low edge of [offset ± tol] interval
            bucket.Sort((a, b) =>
                (a.Offset - a.PerpTol).CompareTo(b.Offset - b.PerpTol));

            var currentCluster = new List<SegmentRec<T>>();
            double currentHigh = double.NegativeInfinity;

            foreach (var seg in bucket)
            {
                double low = seg.Offset - seg.PerpTol - thicknessTolerance;
                double high = seg.Offset + seg.PerpTol + thicknessTolerance;

                if (low <= currentHigh)                // still inside same fat strip
                {
                    currentCluster.Add(seg);
                    currentHigh = Math.Max(currentHigh, high);
                }
                else                                   // gap ⇒ close cluster
                {
                    EmitLongitudinalGroups(currentCluster, result);
                    currentCluster = [seg];
                    currentHigh = high;
                }
            }
            EmitLongitudinalGroups(currentCluster, result);
        }

        return result;
    }

    /*---------------------------------------------------------------------
     * private helpers
     *-------------------------------------------------------------------*/

    private readonly double _angleTol = angleTol;
    private readonly double _angleQuantum = 1.0 / angleTol;
    private readonly double _longTol = longTol;
    private readonly List<SegmentRec<T>> _records = [];

    private static SegmentRec<T> Canonicalise(T item, Line ln, double perpTol)
    {
        // Project into XY
        var p0 = ln.Start;
        var p1 = ln.End;
        var d = p1 - p0;
        var dir2 = new Vector3(d.X, d.Y);
        var len = dir2.Length();
        if (len < 1e-12) throw new ArgumentException("Zero-length line.");

        // Unit direction
        dir2 /= len;

        // Fold anti-parallel into the same half-plane
        if (dir2.X < 0 || (Math.Abs(dir2.X) < 1e-12 && dir2.Y < 0))
            dir2 = dir2.Negate();

        double angle = Math.Atan2(dir2.Y, dir2.X);          // ∈ [0, π)

        // Unit normal (90° CCW)
        var n = new Vector3(-dir2.Y, dir2.X);

        // Offset of the infinite line
        double offset = n.X * p0.X + n.Y * p0.Y;

        // Scalars along direction
        double s0 = dir2.X * p0.X + dir2.Y * p0.Y;
        double s1 = dir2.X * p1.X + dir2.Y * p1.Y;

        return new SegmentRec<T>(
            item,
            angle,
            offset,
            Math.Min(s0, s1),
            Math.Max(s0, s1),
            perpTol
        );
    }

    private int DirKey(double angle) => (int)Math.Round(angle * _angleQuantum);

    /// Split a fat-cluster into longitudinal overlap groups and add them to <paramref name="sink"/>.
    private void EmitLongitudinalGroups(
    List<SegmentRec<T>> cluster,
    List<OverlapMergeGroup<T>> sink)
    {
        if (cluster.Count == 0) return;

        // sort by MinS and sweep exactly as before to split by longitudinal gaps
        cluster.Sort((a, b) => a.MinS.CompareTo(b.MinS));

        var current = new List<SegmentRec<T>>();
        double currentMax = double.NegativeInfinity;

        foreach (var seg in cluster)
        {
            if (seg.MinS <= currentMax + _longTol)        // overlaps current set
            {
                current.Add(seg);
                currentMax = Math.Max(currentMax, seg.MaxS);
            }
            else
            {
                if (current.Count > 0)
                    sink.Add(new OverlapMergeGroup<T>(current));
                current = [seg];
                currentMax = seg.MaxS;
            }
        }
        if (current.Count > 0)
            sink.Add(new OverlapMergeGroup<T>(current));
    }
}