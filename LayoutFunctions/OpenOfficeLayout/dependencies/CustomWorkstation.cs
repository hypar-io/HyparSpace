using System.Linq;
using Elements.Geometry;
using Elements.Geometry.Solids;

namespace Elements
{
    public class CustomWorkstation : GeometricElement
    {
        private static Material CustomWorkstationMaterial = new Material("Workstation", (0.7, 0.7, 0.7, 0.6));
        public CustomWorkstation(double length, double width)
        {
            var boundary = Polygon.Rectangle((0, 0), (length, width)).Offset(-0.05).First();
            this.Representation = new Extrude(boundary, 1, Vector3.ZAxis, false);
            this.Material = CustomWorkstationMaterial;
            this.IsElementDefinition = true;
        }
    }
}