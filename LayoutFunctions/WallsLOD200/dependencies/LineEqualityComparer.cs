using Elements.Geometry;

namespace WallsLOD200
{

    public class LineEqualityComparer : IEqualityComparer<Line>
    {
        public bool Equals(Line? x, Line? y)
        {
            if (x != null && y != null)
            {
                return (x.Start.IsAlmostEqualTo(y.Start) && x.End.IsAlmostEqualTo(y.End)) || (x.Start.IsAlmostEqualTo(y.End) && x.End.IsAlmostEqualTo(y.Start));
            }
            return false;
        }

        public int GetHashCode(Line obj)
        {
            return obj.GetHashCode();
        }
    }
}