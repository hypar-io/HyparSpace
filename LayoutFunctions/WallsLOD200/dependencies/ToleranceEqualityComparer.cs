namespace WallsLOD200
{
    public class ToleranceEqualityComparer : IEqualityComparer<double>
    {
        private readonly double _tolerance;

        public ToleranceEqualityComparer(double tolerance)
        {
            _tolerance = tolerance;
        }

        public bool Equals(double x, double y)
        {
            return Math.Abs(x - y) <= _tolerance;
        }

        public int GetHashCode(double obj) => 1;
    }
}