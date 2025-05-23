using Elements.Geometry;

namespace WallsLOD200;

/// <summary>
/// Represents a line with thickness (a "fat line").
/// </summary>
public readonly record struct FatLine(Line Centerline, double Thickness);

/// <summary>
/// A connected set of overlapping segments and the fat-lines
/// obtained by slicing/merging them along the longitudinal axis.
/// </summary>
public class OverlapMergeGroup<T>
{
    /// <summary>
    /// The original items in this group.
    /// </summary>
    public IReadOnlyList<T> Items { get; }

    /// <summary>
    /// The merged fat lines representing this group.
    /// </summary>
    public IReadOnlyList<FatLine> FatLines { get; }

    internal OverlapMergeGroup(List<SegmentRecord<T>> segments)
    {
        Items = [.. segments.Select(s => s.Payload)];

        /* ----  build the non-overlapping intervals  ------------------ */

        // 1. collect all break-points on the s-axis
        var sCuts = new SortedSet<double>();
        foreach (var s in segments) { sCuts.Add(s.MinS); sCuts.Add(s.MaxS); }
        var sList = sCuts.ToArray();

        // direction basis (same for whole group)
        double ang = segments[0].Angle;
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
            if (s1 - s0 < Vector3.EPSILON)
            {
                continue;            // zero-length slot
            }

            // 2.  which segments cover [s0,s1]?
            var active = segments.Where(s => s.MinS <= s0 + Vector3.EPSILON && s.MaxS >= s1 - Vector3.EPSILON)
                             .ToList();
            if (active.Count == 0)
            {
                continue;
            }

            // 3. perpendicular envelope for those segments
            double low = active.Min(s => s.Offset - s.OffsetTolerance);
            double high = active.Max(s => s.Offset + s.OffsetTolerance);
            double thick = high - low;
            double offC = 0.5 * (low + high);

            // 4. build centerline for this slice
            var start = new Vector3(u.X * s0 + n.X * offC,
                                    u.Y * s0 + n.Y * offC, 0);
            var end = new Vector3(u.X * s1 + n.X * offC,
                                    u.Y * s1 + n.Y * offC, 0);
            if ((start - end).Length() < Vector3.EPSILON)
            {
                continue;
            }
            var slice = new Line(start, end);

            // 5. merge with previous slice if thickness matches
            if (open is not null && Math.Abs(thick - openThick) < Vector3.EPSILON)
            {
                open = new Line(open.Start, end);
            }
            else
            {
                if (open is not null)
                {
                    lines.Add(new FatLine(open, openThick));
                }
                open = slice;
                openThick = thick;
            }
        }
        if (open is not null)
        {
            lines.Add(new FatLine(open, openThick));
        }

        FatLines = lines;
    }
}

/// <summary>
/// Internal record to track segment data for processing.
/// </summary>
readonly struct SegmentRecord<T>(T payload, double angle, double offset, double minS, double maxS, double offsetTolerance)
{
    public readonly T Payload = payload;
    public readonly double Angle = angle;
    public readonly double Offset = offset;
    public readonly double MinS = minS, MaxS = maxS;
    public readonly double OffsetTolerance = offsetTolerance;
}

/// <summary>
/// Groups arbitrary payload objects carried by 2-D line segments into "overlap groups".
/// A group is a connected component of:
/// • strips that are (anti-)parallel within <see cref="_angleTol"/>
/// • whose offset intervals overlap, taking each segment's own thickness into account
/// • whose scalar intervals along the line overlap
/// </summary>
/// <typeparam name="T">Type of payload objects associated with each segment</typeparam>
/// <param name="angleTolerance">
/// Angular tolerance in **radians** for folding two directions into the same bucket
/// (defaults to 1 × 10⁻³ ≈ 0.057°).
/// </param>
/// <param name="longTolerance">
/// Longitudinal tolerance for determining overlaps along the direction of segments.
/// </param>
public class OverlapIndex<T>(double angleTolerance = 1e-3, double longTolerance = 1e-6)
{
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
    /// </summary>
    /// <param name="thicknessTolerance">
    /// Extra gap you are willing to tolerate between two strips that *almost* touch.
    /// Set to 0 for strict behavior.
    /// </param>
    /// <returns>List of overlap groups where each contains multiple items</returns>
    public List<OverlapMergeGroup<T>> GetOverlapGroups(double thicknessTolerance = 0.0)
    {
        // 1.  Bucket by canonical direction
        var dirBuckets = new Dictionary<int, List<SegmentRecord<T>>>();

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
                (a.Offset - a.OffsetTolerance).CompareTo(b.Offset - b.OffsetTolerance));

            var currentCluster = new List<SegmentRecord<T>>();
            double currentHigh = double.NegativeInfinity;

            foreach (var seg in bucket)
            {
                double low = seg.Offset - seg.OffsetTolerance - thicknessTolerance;
                double high = seg.Offset + seg.OffsetTolerance + thicknessTolerance;

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

    private readonly double _angleQuantum = 1.0 / angleTolerance;
    private readonly double _longTolerance = longTolerance;
    private readonly List<SegmentRecord<T>> _records = [];

    /// <summary>
    /// Converts a line segment into a canonical internal representation.
    /// </summary>
    private static SegmentRecord<T> Canonicalise(T item, Line line, double perpTol)
    {
        // ---- 1. basic checks ------------------------------------------------
        var p0 = line.Start;
        var p1 = line.End;
        var d3 = p1 - p0;
        var len = Math.Sqrt(d3.X * d3.X + d3.Y * d3.Y);      // ignore Z
        if (len < 1e-12)
            throw new ArgumentException("Zero-length line.");

        // ---- 2. direction as unit vector -----------------------------------
        double ux = d3.X / len;
        double uy = d3.Y / len;

        // ---- 3. canonical angle  [0, π)  -----------------------------------
        double ang = Math.Atan2(uy, ux);      // (-π, π]
        if (ang < 0) ang += Math.PI;         // → 0 … π
        if (ang >= Math.PI - 1e-12) ang = 0;  // fold the π boundary onto 0

        // and regenerate *exactly* from the angle so every collinear line shares
        // identical bases, independent of its original sign fuzz.
        double cos = Math.Cos(ang);
        double sin = Math.Sin(ang);
        var u = new Vector3((float)cos, (float)sin);
        var n = new Vector3((float)-sin, (float)cos);

        // ---- 4. scalar data -------------------------------------------------
        double offset = n.X * p0.X + n.Y * p0.Y;          // signed distance
        double s0 = u.X * p0.X + u.Y * p0.Y;
        double s1 = u.X * p1.X + u.Y * p1.Y;

        return new SegmentRecord<T>(
            item,
            ang,
            offset,
            Math.Min(s0, s1),
            Math.Max(s0, s1),
            perpTol);
    }

    /// <summary>
    /// Calculates a direction key for bucketing by angle.
    /// </summary>
    private int DirKey(double angle) => (int)Math.Round(angle * _angleQuantum);

    /// <summary>
    /// Split a fat-cluster into longitudinal overlap groups and add them to <paramref name="sink"/>.
    /// </summary>
    /// <param name="cluster">Cluster of segments to process</param>
    /// <param name="sink">Collection to add resulting groups to</param>
    private void EmitLongitudinalGroups(
    List<SegmentRecord<T>> cluster,
    List<OverlapMergeGroup<T>> sink)
    {
        if (cluster.Count == 0)
        {
            return;
        }

        // sort by MinS and sweep exactly as before to split by longitudinal gaps
        cluster.Sort((a, b) => a.MinS.CompareTo(b.MinS));

        var current = new List<SegmentRecord<T>>();
        double currentMax = double.NegativeInfinity;

        foreach (var seg in cluster)
        {
            if (seg.MinS <= currentMax + _longTolerance)        // overlaps current set
            {
                current.Add(seg);
                currentMax = Math.Max(currentMax, seg.MaxS);
            }
            else
            {
                if (current.Count > 0)
                {
                    sink.Add(new OverlapMergeGroup<T>(current));
                }
                current = [seg];
                currentMax = seg.MaxS;
            }
        }
        if (current.Count > 0)
        {
            sink.Add(new OverlapMergeGroup<T>(current));
        }
    }
}