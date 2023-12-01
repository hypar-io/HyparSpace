using Elements;
using Elements.Geometry;
using Elements.Spatial.AdaptiveGrid;
using TravelDistanceAnalyzer;
using GridVertex = Elements.Spatial.AdaptiveGrid.Vertex;

namespace Elements
{
    internal class RouteDistanceConfiguration : GeometricElement
    {
        public string AddId;

        public List<Vector3> Destinations;

        private double _snapingDistance = 0.25;
        private double _routeHeight = 1;

        public RouteDistanceConfiguration(string addId, IList<Vector3> destinations) 
        {
            AddId = addId;
            Destinations = destinations.ToList();
            var color = ColorFactory.FromGuid(AddId);
            Material = new Material("Route", color);
        } 

        public override void UpdateRepresentations()
        {
            Representation = new Representation();

            foreach (var point in Destinations)
            {
                var shape = new Circle(point, 0.25).ToPolygon(8);
                Representation.SolidOperations.Add(new Geometry.Solids.Extrude(
                    shape, _routeHeight, Vector3.ZAxis));
            }
        }

        public double Distance { get; set; }

        public Color Color
        {
            get { return Material.Color; }
            set { Material.Color = value; }
        }

        public List<Element> Compute(AdaptiveGridBuilder builder)
        {
            List<Element> additionalVisualization = new List<Element>();
            if (Destinations.Count < 2)
            {
                return additionalVisualization;
            }

            ulong start = builder.AddEndPoint(Destinations[0], _snapingDistance, out var connection);
            var grid = builder.Grid;


            var startVertex = grid.GetVertex(start);
            AdditionalConnections(grid, startVertex, connection);
            ulong end;
            double distance = 0;
            //Update positions is case exit is snapped
            Destinations[0] = startVertex.Point;

            for (int i = 1; i < Destinations.Count; i++)
            {
                end = builder.AddEndPoint(Destinations[i], _snapingDistance, out connection);
                var endVertex = grid.GetVertex(end);
                AdditionalConnections(grid, endVertex, connection);
                Destinations[i] = endVertex.Point;
                var alg = new AdaptiveGraphRouting(grid, new RoutingConfiguration(turnCost: 0.01));
                var leafs = new List<RoutingVertex> { new RoutingVertex(start, 0) };
                var trunks = new List<ulong> { end };
                var tree = alg.BuildSimpleNetwork(leafs, trunks, null);

                //Distances are caches in Dictionary. It's mostly to draw edge lines only once as
                //distance calculations are cheap.
                startVertex = grid.GetVertex(start);
                Dictionary<Edge, double> accumulatedDistances = new Dictionary<Edge, double>();
                grid.CalculateDistanceRecursive(startVertex, tree, accumulatedDistances);
                var next = tree[startVertex.Id];
                if (next != null && next.Trunk != null)
                {
                    var edge = startVertex.GetEdge(tree[startVertex.Id].Trunk.Id);
                    distance += accumulatedDistances[edge];
                }

                //TODO: height on level can have extra elevation.
                additionalVisualization.Add(grid.TreeVisualization(
                    accumulatedDistances.Keys, _routeHeight, Material));

                start = end;
            }
            Distance = distance;
            additionalVisualization.Add(DestinationLabels());
            return additionalVisualization;
        }

        public bool OnElevation(double elevation)
        {
            return Destinations.All(d => d.Z.ApproximatelyEquals(elevation));
        }

        private void AdditionalConnections(AdaptiveGrid grid,
                                           GridVertex exit,
                                           GridVertex mainConnection)
        {
            var basePoint = exit.Point;
            var maxDist = basePoint.DistanceTo(mainConnection.Point) * 2;
            var additionalConnections = new (double Distance, Vector3 Point)[4]
            {
                (double.MaxValue, Vector3.Origin),
                (double.MaxValue, Vector3.Origin),
                (double.MaxValue, Vector3.Origin),
                (double.MaxValue, Vector3.Origin)
            };

            var xDir = (mainConnection.Point - basePoint).Unitized();
            var yDir = xDir.Cross(Vector3.ZAxis);

            foreach (var edge in grid.GetEdges())
            {
                if (edge.StartId == mainConnection.Id || edge.EndId == mainConnection.Id)
                {
                    continue;
                }

                var start = grid.GetVertex(edge.StartId);
                var end = grid.GetVertex(edge.EndId);

                var line = new Line(start.Point, end.Point);
                var closest = exit.Point.ClosestPointOn(line);
                var delta = closest - basePoint;
                var length = delta.Length();
                if (length > maxDist)
                {
                    continue;
                }

                var directionIndex = -1;
                var dot = delta.Unitized().Dot(xDir);
                if (dot.ApproximatelyEquals(1, 0.01))
                {
                    directionIndex = 0;
                }
                else if (dot.ApproximatelyEquals(-1, 0.01))
                {
                    directionIndex = 1;
                }

                dot = delta.Unitized().Dot(yDir);
                if (dot.ApproximatelyEquals(1, 0.01))
                {
                    directionIndex = 2;
                }
                else if (dot.ApproximatelyEquals(-1, 0.01))
                {
                    directionIndex = 3;
                }

                if (directionIndex >= 0)
                {
                    var connection = additionalConnections[directionIndex];
                    if (length < connection.Distance)
                    {
                        additionalConnections[directionIndex] = (length, closest);
                    }
                }
            }

            foreach (var connection in additionalConnections)
            {
                if (connection.Distance != double.MaxValue)
                {
                    grid.AddEdge(exit.Point, connection.Point);
                }
            }
        }

        private ModelText DestinationLabels()
        {
            var texts = new List<(Vector3 Location, Vector3 FacingDirection, Vector3 LineDirection, string Text, Color? Color)>();
            for (int i = 0; i < Destinations.Count; i++)
            {
                texts.Add((Destinations[i] + new Vector3(0, 0, _routeHeight + Vector3.EPSILON),
                           Vector3.ZAxis,
                           Vector3.XAxis,
                           (i + 1).ToString(),
                           Colors.Black));
            }
            return new ModelText(texts, FontSize.PT72);
        }

    }
}
