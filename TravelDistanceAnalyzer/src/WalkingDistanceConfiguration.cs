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

        private double _snapingDistance = 0.25;
        private double _routeHeight = 1;
        private LinesRepresentation? _lineRepresentation = null;

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
            Statistics = new();
        }

        public override void UpdateRepresentations()
        {
            if (RepresentationInstances.Count == 0)
            {
                var shape = new Polygon((-0.1, -0.1), (-0.25, 0), (-0.1, 0.1), (0, 0.25),
                            (0.1, 0.1), (0.25, 0), (0.1, -0.1), (0, -0.25));
                var extrude = new Geometry.Solids.Extrude(shape, _routeHeight, Vector3.ZAxis);
                SolidRepresentation sr = new SolidRepresentation(extrude);
                RepresentationInstances.Add(new RepresentationInstance(sr, Material));

                if (_lineRepresentation != null)
                {
                    RepresentationInstances.Add(new RepresentationInstance(_lineRepresentation, Material));
                }
            }
        }

        public List<WalkingDistanceStatistics> Statistics { get; set; }

        public Color Color
        {
            get { return Material.Color; }
            set { Material.Color = value; }
        }

        public void Compute(AdaptiveGridBuilder builder)
        {
            var exit = builder.AddEndPoint(Transform.Origin, _snapingDistance);
            if (exit == 0)
            {
                return;
            }

            var grid = builder.Grid;

            //Update positions is case exit is snapped
            Transform = new Transform(grid.GetVertex(exit).Point);

            var alg = new AdaptiveGraphRouting(grid, new RoutingConfiguration(turnCost: 0.01));

            var exits = new List<ulong> { exit };
            var filteredRooms = builder.RoomExits.Where(
                kvp => !ProgramTypes.Any() || ProgramTypes.Contains(kvp.Key.ProgramType));
            var roomExits = filteredRooms.SelectMany(kvp => kvp.Value.Select(
                v => new RoutingVertex(v.Id, 0)));

            var tree = alg.BuildSimpleNetwork(roomExits.ToList(), exits, null);

            List<(SpaceBoundary Room, GridVertex Exit)> bestExits = new();
            foreach (var room in filteredRooms)
            {
                var bestExit = ChooseClosestExit(grid, tree, room.Value);
                if (bestExit != null)
                {
                    bestExits.Add((room.Key, bestExit));
                }
            }

            var distances = grid.CalculateDistances(bestExits.Select(e => e.Exit), tree);
            RecordStatistics(grid, distances, bestExits, tree);

            //Representation instance will apply transformation so everything need to be in its local frame.
            Transform t = Transform.Inverted().Moved(z: _routeHeight);
            _lineRepresentation = new LinesRepresentation(grid.TreeVisualization(distances.Keys, t), true);
        }

        public bool OnElevation(double elevation)
        {
            return Transform.Origin.Z.ApproximatelyEquals(elevation);
        }

        private void RecordStatistics(AdaptiveGrid grid,
                                      Dictionary<Edge, double> accumulatedDistances,
                                      List<(SpaceBoundary Room, GridVertex Exit)> inputs,
                                      IDictionary<ulong, TreeNode> tree)
        {

            Dictionary<string, (WalkingDistanceStatistics Stat, int Num)> statisticsByType = new();
            foreach (var input in inputs)
            {
                //Distances are caches in Dictionary. It's mostly to draw edge lines only once as
                //distance calculations are cheap.
                var next = tree[input.Exit.Id];
                if (next != null && next.Trunk != null)
                {
                    var edge = input.Exit.GetEdge(tree[input.Exit.Id].Trunk.Id);
                    var distance = accumulatedDistances[edge];
                    var type = input.Room.ProgramType;
                    if (statisticsByType.TryGetValue(type, out var value))
                    {
                        value.Stat.LongestDistance = Math.Max(value.Stat.LongestDistance, distance);
                        value.Stat.ShortestDistance = Math.Min(value.Stat.ShortestDistance, distance);
                        value.Stat.AverageDistance += distance;
                        statisticsByType[type] = (value.Stat, value.Num + 1);
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
        }

        private static GridVertex? ChooseClosestExit(
            AdaptiveGrid grid,
            IDictionary<ulong, TreeNode> tree,
            List<GridVertex> exits)
        {
            if (exits.Count == 1)
            {
                return exits.First();
            }

            double bestLength = double.MaxValue;
            GridVertex? bestExit = null;
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
