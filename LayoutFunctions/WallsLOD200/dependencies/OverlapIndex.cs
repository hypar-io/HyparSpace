using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Elements.Geometry;          // Vector3, Line

namespace WallsLOD200;

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
    public List<(T[] group, Line mergedCenterline, double mergedThickness)>
    GetOverlapGroups(double thicknessTolerance = 0.0)
    {
        // 1.  Bucket by canonical direction
        var dirBuckets = new Dictionary<int, List<SegmentRec>>();

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
        var result = new List<(T[] group, Line center, double thick)>();

        foreach (var bucket in dirBuckets.Values)
        {
            // sort by low edge of [offset ± tol] interval
            bucket.Sort((a, b) =>
                (a.Offset - a.PerpTol).CompareTo(b.Offset - b.PerpTol));

            var currentCluster = new List<SegmentRec>();
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
    private readonly List<SegmentRec> _records = [];

    private readonly struct SegmentRec(
        T payload,
        double angle,
        double offset,
        double minS,
        double maxS,
        double perpTol)
    {
        public readonly T Payload = payload;
        public readonly double Angle = angle;      // canonical (0 … π)
        public readonly double Offset = offset;     // signed distance from origin
        public readonly double MinS = minS, MaxS = maxS; // 1-D interval on the line
        public readonly double PerpTol = perpTol;    // half-thickness
    }

    private static SegmentRec Canonicalise(T item, Line ln, double perpTol)
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

        return new SegmentRec(
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
    List<SegmentRec> cluster,
    List<(T[] group, Line center, double thick)> sink)
    {
        if (cluster.Count == 0) return;

        // sort by MinS so we can sweep and merge intervals
        cluster.Sort((a, b) => a.MinS.CompareTo(b.MinS));

        var currentSegs = new List<SegmentRec>();
        double segMinS = double.NaN;
        double segMaxS = double.NaN;
        double lowEdge = double.NaN;   // min(offset – perpTol)
        double highEdge = double.NaN;   // max(offset + perpTol)

        void FlushGroup()
        {
            if (currentSegs.Count == 0) return;

            // direction basis from the first segment
            double ang = currentSegs[0].Angle;
            double cos = Math.Cos(ang);
            double sin = Math.Sin(ang);
            var u = new Vector3(cos, sin, 0);      // unit direction
            var n = new Vector3(-sin, cos, 0);      // unit normal

            double offsetCenter = 0.5 * (lowEdge + highEdge);
            var start = new Vector3(
                u.X * segMinS + n.X * offsetCenter,
                u.Y * segMinS + n.Y * offsetCenter,
                0);
            var end = new Vector3(
                u.X * segMaxS + n.X * offsetCenter,
                u.Y * segMaxS + n.Y * offsetCenter,
                0);

            var centerLine = new Line(start, end);
            double mergedThick = highEdge - lowEdge;

            sink.Add((
                currentSegs.Select(s => s.Payload).ToArray(),
                centerLine,
                mergedThick));

            currentSegs.Clear();
        }

        foreach (var seg in cluster)
        {
            if (currentSegs.Count == 0)
            {
                // start new group
                currentSegs.Add(seg);
                segMinS = seg.MinS;
                segMaxS = seg.MaxS;
                lowEdge = seg.Offset - seg.PerpTol;
                highEdge = seg.Offset + seg.PerpTol;
                continue;
            }

            if (seg.MinS <= segMaxS + _longTol)   // overlaps current group
            {
                currentSegs.Add(seg);
                segMaxS = Math.Max(segMaxS, seg.MaxS);
                lowEdge = Math.Min(lowEdge, seg.Offset - seg.PerpTol);
                highEdge = Math.Max(highEdge, seg.Offset + seg.PerpTol);
            }
            else                                  // gap ⇒ flush & restart
            {
                FlushGroup();

                currentSegs.Add(seg);
                segMinS = seg.MinS;
                segMaxS = seg.MaxS;
                lowEdge = seg.Offset - seg.PerpTol;
                highEdge = seg.Offset + seg.PerpTol;
            }
        }
        FlushGroup();     // final group
    }
}