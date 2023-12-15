using Elements;
using Elements.Geometry;
using Elements.Spatial.AdaptiveGrid;
using TravelDistanceAnalyzer;

namespace Elements
{
    internal class RouteDistanceConfiguration : GeometricElement
    {
        public string AddId;

        public List<Vector3> Destinations;

        private double _snapingDistance = 0.25;
        private double _routeHeight = 1;
        private List<Line> _lineRepresentation = new();

        public RouteDistanceConfiguration(string addId, IList<Vector3> destinations) 
        {
            AddId = addId;
            Destinations = destinations.ToList();
            var color = ColorFactory.FromGuid(AddId);
            Material = new Material("Route", color);
        } 

        public override void UpdateRepresentations()
        {
            if (RepresentationInstances.Count == 0)
            {
                foreach (var point in Destinations)
                {
                    var shape = new Circle(point, 0.25).ToPolygon(8);
                    var extrude = new Geometry.Solids.Extrude(shape, _routeHeight, Vector3.ZAxis);
                    SolidRepresentation sr = new SolidRepresentation(extrude);
                    RepresentationInstances.Add(new RepresentationInstance(sr, Material));
                }

                if (_lineRepresentation.Any())
                {
                    LinesRepresentation r = new LinesRepresentation(_lineRepresentation, true);
                    RepresentationInstances.Add(new RepresentationInstance(r, Material));
                }
            }
        }

        public double Distance { get; set; }

        public Color Color
        {
            get { return Material.Color; }
            set { Material.Color = value; }
        }

        public void Compute(AdaptiveGridBuilder builder)
        {
            if (Destinations.Count < 2)
            {
                return;
            }

            ulong start = builder.AddEndPoint(Destinations[0], _snapingDistance);
            var grid = builder.Grid;


            var startVertex = grid.GetVertex(start);
            ulong end;
            double distance = 0;
            //Update positions is case exit is snapped
            Destinations[0] = startVertex.Point;

            for (int i = 1; i < Destinations.Count; i++)
            {
                end = builder.AddEndPoint(Destinations[i], _snapingDistance);
                var endVertex = grid.GetVertex(end);
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

                _lineRepresentation.AddRange(
                    grid.VisualizeTree(accumulatedDistances.Keys, new Transform(0, 0, _routeHeight)));

                start = end;
            }
            Distance = distance;
        }

        public bool OnElevation(double elevation)
        {
            return Destinations.All(d => d.Z.ApproximatelyEquals(elevation));
        }

        public ModelText GrawDestinationLabels()
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
