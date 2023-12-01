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
    }
}
