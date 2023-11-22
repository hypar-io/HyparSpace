using Elements.Geometry;
using System.Collections.Generic;
using Line = Elements.Geometry.Line;
namespace Elements
{
    public partial class WallCandidate
    {
        public double Height { get; set; }

        public Transform LevelTransform { get; set; }
        public bool? PrimaryEntryEdge { get; set; }

        public string AddId { get; set; }

        public WallCandidate(Line line, string type, double height, Transform levelTransform, IList<SpaceBoundary> spaceAdjacencies = null, System.Guid id = default, string name = null)
            : this(line, type, spaceAdjacencies, id, name)
        {
            Height = height;
            LevelTransform = levelTransform;
        }
        public (double innerWidth, double outerWidth)? Thickness { get; set; }
    }
}