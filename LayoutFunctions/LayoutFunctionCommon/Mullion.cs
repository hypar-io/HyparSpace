using Elements;
using System;
using System.Linq;
using System.Collections.Generic;
using Elements.Geometry;
using Elements.Geometry.Solids;

namespace Elements
{
    public class Mullion : GeometricElement
    {
        public Line BaseLine { get; set; }

        public double Width { get; set; }

        public double Height { get; set; }

        public Mullion()
        {

        }

        public override void UpdateRepresentations()
        {
            var a = BaseLine.Offset(Width / 2.0, false);
            var b = BaseLine.Offset(Width / 2.0, true);
            var rect = new Polygon(
                a.Start,
                a.End,
                b.End,
                b.Start
            );
            Representation = new Extrude(rect, Height, Vector3.ZAxis, false);
        }
    }
}