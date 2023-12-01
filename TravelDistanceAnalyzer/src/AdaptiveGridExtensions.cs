using Elements;
using Elements.Geometry;
using Elements.Spatial.AdaptiveGrid;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GridVertex = Elements.Spatial.AdaptiveGrid.Vertex;

namespace TravelDistanceAnalyzer
{
    internal static class AdaptiveGridExtensions
    {
        public static Dictionary<Edge, double> ComputeDistances(
            this AdaptiveGrid grid,
            IEnumerable<GridVertex> leafs,
            IDictionary<ulong, TreeNode> tree)
        {
            Dictionary<Edge, double> accumulatedDistances = new Dictionary<Edge, double>();
            foreach (var leaf in leafs)
            {
                grid.CalculateDistanceRecursive(leaf, tree, accumulatedDistances);
            }
            return accumulatedDistances;
        }

        public static double CalculateDistanceRecursive(
            this AdaptiveGrid grid,
            GridVertex head,
            IDictionary<ulong, TreeNode> tree,
            Dictionary<Edge, double> accumulatedDistances)
        {
            var node = tree[head.Id];
            if (node == null || node.Trunk == null)
            {
                return 0;
            }

            var edge = head.GetEdge(node.Trunk.Id);
            if (accumulatedDistances.TryGetValue(edge, out double distance))
            {
                return distance;
            }

            var tail = grid.GetVertex(node.Trunk.Id);
            var d = CalculateDistanceRecursive(grid, tail, tree, accumulatedDistances);
            d += tail.Point.DistanceTo(head.Point);
            accumulatedDistances[edge] = d;
            return d;
        }

        public static ModelLines TreeVisualization(this AdaptiveGrid grid, 
                                                   IEnumerable<Edge> edges,
                                                   double elevation,
                                                   Material material)
        {
            List<Line> lines = new List<Line>();
            foreach (var item in edges)
            {
                var start = grid.GetVertex(item.StartId);
                var end = grid.GetVertex(item.EndId);
                var shape = new Line(start.Point, end.Point);
                lines.Add(shape);
            }
            ModelLines modelLines = new ModelLines(lines, material, new Transform(0, 0, elevation));
            modelLines.SetSelectable(false);
            return modelLines;
        }
    }
}
