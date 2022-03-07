using Elements.Geometry;

namespace Elements
{
    public partial class SpaceBoundary : GeometricElement, ISpaceBoundary
    {
        public Vector3? ParentCentroid { get; set; }
        
        public Vector3? IndividualCentroid { get; set; }
    }
}