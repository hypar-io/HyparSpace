using Elements;
using Elements.Geometry;
using Elements.Geometry.Solids;

namespace Elements
{
    public partial class Door
    {
        public const double DOOR_THICKNESS = 0.125;
        public const double DOOR_FRAME_THICKNESS = 0.15;
        public const double DOOR_FRAME_WIDTH = 2 * 0.0254; //2 inches

        public Door(WallCandidate wall, Vector3 position, DoorType type, double width, double height) :
            this(WidthWithoutFrame(width, type), type, wall, height, material: new Material("Door material", Colors.White))
        {
            OriginalPosition = position;
            var adjustedPosition = GetClosestValidDoorPos(wall.Line);
            Transform = new Transform(adjustedPosition, wall.Line.Direction(), Vector3.ZAxis);
        }

        public Vector3 OriginalPosition
        {
            get; private set;
        }

        public static bool CanFit(Line wallLine, DoorType type, double width)
        {
            var doorWidth = WidthWithoutFrame(width, type) + DOOR_FRAME_WIDTH * 2;
            return wallLine.Length() - doorWidth > DOOR_FRAME_WIDTH * 2;
        }

        public override void UpdateRepresentations()
        {
            Vector3 left = Vector3.XAxis * ClearWidth / 2;
            Vector3 right = Vector3.XAxis.Negate() * ClearWidth / 2;

            var doorPolygon = new Polygon(new List<Vector3>() {
                left + Vector3.YAxis * DOOR_THICKNESS, 
                left - Vector3.YAxis * DOOR_THICKNESS,
                right - Vector3.YAxis * DOOR_THICKNESS,
                right + Vector3.YAxis * DOOR_THICKNESS});
            var doorExtrude = new Extrude(new Profile(doorPolygon), ClearHeight, Vector3.ZAxis);

            var frameLeft = left + Vector3.XAxis * DOOR_FRAME_WIDTH;
            var frameRight = right - Vector3.XAxis * DOOR_FRAME_WIDTH;
            var frameOffset = Vector3.YAxis * DOOR_FRAME_THICKNESS;
            var doorFramePolygon = new Polygon(new List<Vector3>() {
                left + Vector3.ZAxis * ClearHeight - frameOffset,
                left - frameOffset,
                frameLeft - frameOffset,
                frameLeft + Vector3.ZAxis * (ClearHeight + DOOR_FRAME_WIDTH) - frameOffset,
                frameRight + Vector3.ZAxis * (ClearHeight + DOOR_FRAME_WIDTH) - frameOffset,
                frameRight - frameOffset,
                right - frameOffset,
                right + Vector3.ZAxis * ClearHeight - frameOffset });
            var doorFrameExtrude = new Extrude(new Profile(doorFramePolygon), DOOR_FRAME_THICKNESS * 2, Vector3.YAxis);

            Representation = new Representation(new List<SolidOperation>() { doorExtrude, doorFrameExtrude });
        }

        private Vector3 GetClosestValidDoorPos(Line wallLine)
        {
            var fullWidth = ClearWidth + DOOR_FRAME_WIDTH * 2;
            double wallWidth = wallLine.Length();
            Vector3 p1 = wallLine.PointAt(0.5 * fullWidth);
            Vector3 p2 = wallLine.PointAt(wallWidth - 0.5 * fullWidth);
            var reducedWallLine = new Line(p1, p2);
            return OriginalPosition.ClosestPointOn(reducedWallLine);
        }

        private static double WidthWithoutFrame(double internalWidth, DoorType type)
        {
            switch (type)
            {
                case DoorType.Single:
                    return internalWidth;
                case DoorType.Double:
                    return internalWidth * 2;
            }
            return 0;
        }
    }
}