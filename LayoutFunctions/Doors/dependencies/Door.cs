using Elements;
using Elements.Geometry;
using Elements.Geometry.Solids;

namespace Elements
{
    public partial class Door
    {
        public const double DOOR_THICKNESS = 0.125;
        public const double DOOR_HEIGHT = 2;
        public const double DOOR_OFFSET = 2 * 0.0254; //2 inches

        public Door(Line wallLine, Vector3 position, DoorType type, double width) :
            this(width, type, material: new Material("Door material", new Color(1.0, 0, 0, 1)))
        {
            OriginalPosition = position;
            var adjustedPosition = GetClosestValidDoorPos(wallLine);
            Transform = new Transform(adjustedPosition, wallLine.Direction(), Vector3.ZAxis);
        }

        public Vector3 OriginalPosition
        {
            get; private set;
        }

        public static bool CanFit(Line wallLine, DoorType type, double width)
        {
            return wallLine.Length() - FullWidth(width, type) > DOOR_OFFSET * 2;
        }

        public override void UpdateRepresentations()
        {
            Vector3 left = Vector3.XAxis * FullWidth(ClearWidth, Type) / 2;
            Vector3 right = Vector3.XAxis.Negate() * FullWidth(ClearWidth, Type) / 2;
            var doorPolygon = new Polygon(new List<Vector3>() {
                left - Vector3.YAxis * DOOR_THICKNESS,
                left,
                right,
                right - Vector3.YAxis * DOOR_THICKNESS });

            var extrude = new Extrude(new Profile(doorPolygon), DOOR_HEIGHT, Vector3.ZAxis);
            Representation = extrude;
        }

        private Vector3 GetClosestValidDoorPos(Line wallLine)
        {
            var fullWidth = FullWidth(ClearWidth, Type);
            double wallWidth = wallLine.Length();
            Vector3 p1 = wallLine.PointAt(0.5 * fullWidth);
            Vector3 p2 = wallLine.PointAt(wallWidth - 0.5 * fullWidth);
            var reducedWallLine = new Line(p1, p2);
            return OriginalPosition.ClosestPointOn(reducedWallLine);
        }

        private static double FullWidth(double internalWidth, DoorType type)
        {
            switch (type)
            {
                case DoorType.Single:
                    return internalWidth + DOOR_OFFSET * 2;
                case DoorType.Double:
                    return internalWidth * 2 + DOOR_OFFSET * 2;
            }
            return 0;
        }
    }
}