using Elements;
using Elements.Geometry;
using glTFLoader.Schema;

namespace Elements
{
    /// <summary>
    /// An element representation displayed as set of lines.
    /// </summary>
    public class LinesRepresentation : ElementRepresentation
    {
        private List<Line> _lines;
        private bool _isSelectable = true;

        /// <summary>
        /// Initializes a new instance of LinesRepresentation.
        /// </summary>
        /// <param name="lines">The lines.</param>
        /// <param name="isSelectable">If curve is selectable.</param>
        public LinesRepresentation(List<Line> lines, bool isSelectable)
        {
            _lines = lines;
            _isSelectable = isSelectable;
        }

        /// <summary>
        /// Indicates if lines are selectable.
        /// </summary>
        public bool IsSelectable => _isSelectable;

        /// <inheritdoc/>
        public override bool TryToGraphicsBuffers(GeometricElement element, out List<GraphicsBuffers> graphicsBuffers, out string id, out MeshPrimitive.ModeEnum? mode)
        {
            id = _isSelectable ? $"{element.Id}_lines" : $"unselectable_{element.Id}_lines";
            mode = glTFLoader.Schema.MeshPrimitive.ModeEnum.LINES;
            graphicsBuffers = new List<GraphicsBuffers>();

            List<Vector3> points = new List<Vector3>();
            foreach (var line in _lines)
            {
                points.Add(line.Start);
                points.Add(line.End);
            }

            graphicsBuffers.Add(points.ToGraphicsBuffers());
            return true;
        }
    }
}