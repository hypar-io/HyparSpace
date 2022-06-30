using System;
using System.Collections.Generic;
using System.Linq;
using Elements.Geometry;

namespace LayoutFunctionCommon
{
    public class SearchablePointCollection<T>
    {
        public int Count => _count;
        private int _count = 0;

        public readonly int Dimensions;


        private readonly Dictionary<Vector3, List<T>> _map = new Dictionary<Vector3, List<T>>();
        private readonly List<SortedDictionary<double, List<Vector3>>> _coords
         = new List<SortedDictionary<double, List<Vector3>>>();

        private List<List<double>> _keys => __keys ??= _coords.Select(c => c.Keys.ToList()).ToList();
        private List<List<double>> __keys;

        public SearchablePointCollection()
        {
            Dimensions = 3;
            for (int i = 0; i < 3; i++)
            {
                _coords.Add(new SortedDictionary<double, List<Vector3>>());
                _keys.Add(new List<double> { });
            }
        }

        public SearchablePointCollection(IEnumerable<Vector3> points = null, IEnumerable<T> elements = null, int dimensions = 3)
        {
            Dimensions = dimensions;
            for (int i = 0; i < dimensions; i++)
            {
                _coords.Add(new SortedDictionary<double, List<Vector3>>());
                _keys.Add(new List<double> { });
            }
            if (points == null && elements == null) { return; }

            if (elements == null)
            {
                foreach (var p in points)
                {
                    Add(p, default(T));
                }
                return;
            }

            if (points == null) { throw new Exception("If elements are provided, points must be provided as well."); }
            using var pe = points.GetEnumerator();
            using var ee = elements.GetEnumerator();
            while (pe.MoveNext() && ee.MoveNext())
            {
                Add(pe.Current, ee.Current);
            }
        }

        public SearchablePointCollection(IEnumerable<(Vector3 Loc, T Elem)> pointsAndElements = null, int dimensions = 3)
        {
            Dimensions = dimensions;
            for (int i = 0; i < dimensions; i++)
            {
                _coords.Add(new SortedDictionary<double, List<Vector3>>());
                _keys.Add(new List<double> { });
            }
            if (pointsAndElements != null)
            {
                foreach (var (Loc, Elem) in pointsAndElements)
                {
                    Add(Loc, Elem);
                }
            }
        }

        public List<T> GetElementsAtPoint(Vector3 location, bool returnNearestOnFailure = false)
        {
            if (!_map.ContainsKey(location))
            {
                if (!returnNearestOnFailure)
                {
                    throw new Exception($"Location {location} is not in collection");
                }
                else
                {
                    return GetElementsAtPoint(FindClosestPoint(location, Dimensions), false);
                }
            }
            else
            {
                return _map[location];
            }
        }

        public IEnumerable<Vector3> FindWithinRange(List<(double Min, double Max)> bounds, double tolerance = 0)
        {
            if (bounds == null || bounds.Count == 0) { yield break; }
            HashSet<Vector3> prev = null;
            for (int i = 0; i < bounds.Count; i++)
            {
                List<Vector3> found = new List<Vector3>();
                int dMin = Math.Max(0, (~_keys[i].BinarySearch(bounds[i].Min - tolerance) - 1));
                int dMax = Math.Min(_keys[i].Count - 1, ~_keys[i].BinarySearch(bounds[i].Max + tolerance));
                int j = dMin;
                while (j <= dMax)
                {
                    double coord = _keys[i][j];
                    if (coord < (bounds[i].Min - tolerance)) { j++; continue; }
                    if (coord > (bounds[i].Max + tolerance)) { break; }
                    foreach (var potential in _coords[i][coord])
                    {
                        found.Add(potential);
                    }
                    j++;
                }
                if (prev != null)
                {
                    prev = prev.Intersect(found).ToHashSet();
                    // If no matches here, then there are no items within the provided range.
                }
                else
                {
                    prev = found.ToHashSet();
                }
                if (prev.Count == 0) { yield break; }
            }
            foreach (var v in prev)
            {
                yield return v;
            }
        }

        public bool HasPointInBounds(BBox3 bbox, double tolerance = 0, int dimensions = 3)
        {
            List<(double, double)> bounds = new List<(double, double)>();
            for (int i = 0; i < Math.Min(dimensions, 3); i++)
            {
                bounds.Add(GetDimensionalRange(i, bbox));
            }
            return FindWithinRange(bounds, tolerance).Any();
        }

        public IEnumerable<Vector3> FindWithinBounds(BBox3 bbox, double tolerance = 0, int dimensions = 3)
        {
            List<(double, double)> bounds = new List<(double, double)>();
            for (int i = 0; i < Math.Min(dimensions, 3); i++)
            {
                bounds.Add(GetDimensionalRange(i, bbox));
            }
            return FindWithinRange(bounds, tolerance);
        }

        public Vector3 FindClosestPoint(Vector3 location, int dimensions = 2)
        {
            List<Vector3> potential = new List<Vector3>();
            for (int d = 0; d < Math.Min(dimensions, Dimensions); d++)
            {
                var dVal = GetDimensionalValue(d, location);
                int min = Math.Max(0, (~_keys[d].BinarySearch(dVal)) - 1);
                potential.AddRange(_coords[d][_keys[d][min]]);
            }
            return potential.OrderBy(p => p.DistanceTo(location)).First();
        }

        public IEnumerable<Vector3> FindClosestPoints(Vector3 location, double distance, int dimensions = 2) => FindWithinRange(Inflate(location, distance));

        public List<(double, double)> Inflate(Vector3 center, double radius, int dimensions = 2)
        {
            var output = new List<(double, double)>();
            for (int d = 0; d < Math.Min(Dimensions, dimensions); d++)
            {
                output.Add(
                    (GetDimensionalValue(d, center) - radius,
                     GetDimensionalValue(d, center) + radius)
                     );
            }
            return output;
        }

        public void AddRange(IEnumerable<(Vector3 Loc, T Elem)> pointsAndElements)
        {
            if (pointsAndElements != null)
            {
                foreach (var (Loc, Elem) in pointsAndElements)
                {
                    Add(Loc, Elem);
                }
            }
        }
        public void Add(Vector3 point, T element)
        {
            for (int d = 0; d < Dimensions; d++)
            {
                var dval = GetDimensionalValue(d, point);
                if (!_coords[d].ContainsKey(dval))
                {
                    _coords[d].Add(dval, new List<Vector3>() { point });

                }
                else
                {
                    _coords[d][dval].Add(point);

                }
            }
            if (!_map.ContainsKey(point))
            {
                _map.Add(point, new List<T>() { element });
            }
            else
            {
                _map[point].Add(element);
            }
            __keys = null;
            _count++;
        }

        public void Remove(Vector3 point)
        {
            for (int d = 0; d < Dimensions; d++)
            {
                var dval = GetDimensionalValue(d, point);
                if (!_coords[d].ContainsKey(dval))
                {
                    // Could not find
                    return;
                }
                else if (_coords[d][dval].Count == 1)
                {
                    // Only point which has this dimensional value
                    _coords[d].Remove(dval);
                }
                else
                {
                    _coords[d][dval].Remove(point);
                }
            }
            _map.Remove(point);
            __keys = null;
            _count--;
        }

        double GetDimensionalValue(int dim, Vector3 point)
        {
            switch (dim)
            {
                case 0:
                    return point.X;
                case 1:
                    return point.Y;
                case 2:
                    return point.Z;
                default:
                    throw new Exception($"Can't handle dimension {dim}");
            }
        }


        (double, double) GetDimensionalRange(int dim, BBox3 bounds)
        {
            switch (dim)
            {
                case 0:
                    return (bounds.Min.X, bounds.Max.X);
                case 1:
                    return (bounds.Min.Y, bounds.Max.Y);
                case 2:
                    return (bounds.Min.Z, bounds.Max.Z);
                default:
                    throw new Exception($"Can't handle dimension {dim}");
            }
        }
    }


}