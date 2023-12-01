using Elements;
using Elements.Geometry;
using Elements.Spatial.AdaptiveGrid;
using Newtonsoft.Json;
using TravelDistanceAnalyzer;
using GridVertex = Elements.Spatial.AdaptiveGrid.Vertex;

namespace Elements
{
    internal class WalkingDistanceConfiguration : GeometricElement
    {
        public string AddId;

        [JsonProperty("Program Types")]
        public List<string> ProgramTypes;

        public WalkingDistanceConfiguration(string addId, IList<string> programTypes, Transform transform)
        {
            AddId = addId;
            Transform = transform;

            if (programTypes != null)
            {
                ProgramTypes = programTypes.ToList();
            }
            else
            {
                ProgramTypes = new List<string>();
            }

            var color = ColorFactory.FromGuid(AddId);
            Material = new Material("Route", color);
        }

        public override void UpdateRepresentations()
        {
            Representation = new Representation();

            var shape = new Polygon((-0.1, -0.1), (-0.25, 0), (-0.1, 0.1), (0, 0.25),
                        (0.1, 0.1), (0.25, 0), (0.1, -0.1), (0, -0.25));
            Representation.SolidOperations.Add(new Geometry.Solids.Extrude(
                shape, 0.5, Vector3.ZAxis));
        }

        public List<WalkingDistanceStatistics> Statistics { get; set; }

        public Color Color
        {
            get { return Material.Color; }
            set { Material.Color = value; }
        }

        public List<Element> Compute(AdaptiveGridBuilder builder)
        {
            List<Element> additionalVisualization = new List<Element>();
            var exit = builder.AddEndPoint(Transform.Origin, 0.25, out _);
            if (exit == 0)
            {
                return additionalVisualization;
            }

            var grid = builder.Grid;

            //Update positions is case exit is snapped
            Transform = new Transform(grid.GetVertex(exit).Point);

            var alg = new AdaptiveGraphRouting(grid, new RoutingConfiguration());

            var exits = new List<ulong> { exit };
            var filteredRooms = builder.RoomExits.Where(
                kvp => !ProgramTypes.Any() || ProgramTypes.Contains(kvp.Key.ProgramType));
            var roomExits = filteredRooms.SelectMany(kvp => kvp.Value.Select(
                v => new RoutingVertex(v.Id, 0)));

            var tree = alg.BuildSimpleNetwork(roomExits.ToList(), exits, null);

            List<(SpaceBoundary Room, GridVertex Exit)> bestExits = new();
            foreach (var room in filteredRooms)
            {
                var bestExit = ChooseExit(grid, tree, room.Value);
                if (bestExit != null)
                {
                    bestExits.Add((room.Key, bestExit));
                }
            }

            additionalVisualization.AddRange(CalculateDistances(grid, bestExits, tree));
            return additionalVisualization;
        }

        private List<ModelCurve> CalculateDistances(
            AdaptiveGrid grid,
            List<(SpaceBoundary Room, GridVertex Exit)> inputs,
            IDictionary<ulong, TreeNode> tree)
        {
            Dictionary<Edge, double> accumulatedDistances = new Dictionary<Edge, double>();
            Dictionary<string, (WalkingDistanceStatistics Stat, int Num)> statisticsByType = new();
            foreach (var input in inputs)
            {
                //Distances are caches in Dictionary. It's mostly to draw edge lines only once as
                //distance calculations are cheap.
                grid.CalculateDistanceRecursive(input.Exit, tree, accumulatedDistances);
                var next = tree[input.Exit.Id];
                if (next != null && next.Trunk != null)
                {
                    var edge = input.Exit.GetEdge(tree[input.Exit.Id].Trunk.Id);
                    var distance = accumulatedDistances[edge];
                    var type = input.Room.ProgramType;
                    if (statisticsByType.TryGetValue(type, out var value))
                    {
                        value.Num++;
                        value.Stat.LongestDistance = Math.Max(value.Stat.LongestDistance, distance);
                        value.Stat.ShortestDistance = Math.Min(value.Stat.ShortestDistance, distance);
                        value.Stat.AverageDistance += distance;
                    }
                    else
                    {
                        statisticsByType[type] = (new WalkingDistanceStatistics(type, distance, distance, distance), 1);
                    }
                }
            }

            foreach (var item in statisticsByType)
            {
                item.Value.Stat.AverageDistance /= item.Value.Num;
            }

            Statistics = statisticsByType.Select(s => s.Value.Stat).ToList();

            double VisualizationHeight = 1.5;
            var t = new Transform(0, 0, VisualizationHeight);
            List<ModelCurve> visualizations = new List<ModelCurve>();
            foreach (var item in accumulatedDistances)
            {
                var start = grid.GetVertex(item.Key.StartId);
                var end = grid.GetVertex(item.Key.EndId);
                var shape = new Line(start.Point, end.Point);
                var modelCurve = new ModelCurve(shape, Material, t);
                modelCurve.SetSelectable(false);
                visualizations.Add(modelCurve);
            }
            return visualizations;
        }

        /// <summary>
        /// For each room find exit that provides smallest distance.
        /// </summary>
        /// <param name="grid">AdaptiveGrid to traverse.</param>
        /// <param name="tree">Traveling tree from rooms corners to exits.</param>
        /// <param name="exits">Combinations of exits and their corresponding corners for each room.</param>
        /// <returns>Most distance efficient exit.</returns>
        private static GridVertex ChooseExit(
            AdaptiveGrid grid,
            IDictionary<ulong, TreeNode> tree,
            List<GridVertex> exits)
        {
            if (exits.Count == 1)
            {
                return exits.First();
            }
            else
            {
                double bestLength = double.MaxValue;
                GridVertex bestExit = null;
                foreach (var exit in exits)
                {
                    double accumulatedLength = 0;
                    var current = tree[exit.Id];
                    while (current.Trunk != null)
                    {
                        var p0 = grid.GetVertex(current.Id).Point;
                        var p1 = grid.GetVertex(current.Trunk.Id).Point;
                        accumulatedLength += p0.DistanceTo(p1);
                        current = current.Trunk;
                    }

                    if (accumulatedLength < bestLength)
                    {
                        bestLength = accumulatedLength;
                        bestExit = exit;
                    }
                }

                return bestExit;
            }
        }
    }
}
