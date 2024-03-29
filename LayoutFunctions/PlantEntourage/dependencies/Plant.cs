﻿using Elements.Geometry;
using Elements.Geometry.Solids;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Elements
{
    public partial class Plant
    {
        public const double DefaultPlantBaseWidth = 0.5;
        public const double DefaultPlantHeight = 1.8;
        public const double DefaultPlantBaseLength = 0.5;

        public Plant(double @baseLength, double @baseWidth, double @height, Transform transform)
            : this(@baseWidth, @baseLength, @height, transform, material: new Material("Plant settings material", Colors.Green), name: "Plant settings")
        {
        }

        public Plant(Transform transform)
            : this(DefaultPlantBaseLength, DefaultPlantBaseWidth, DefaultPlantHeight, transform) 
        { 
        }

        public override void UpdateRepresentations()
        {
            Vector3 halfLengthVector = 0.5 * BaseLength * Vector3.XAxis;
            Vector3 halfWidthVector = 0.5 * BaseWidth * Vector3.YAxis;

            var plantPolygon = new Polygon(new List<Vector3>()
            {
                halfLengthVector + halfWidthVector,
                halfLengthVector - halfWidthVector,
                halfLengthVector.Negate() - halfWidthVector,
                halfWidthVector - halfLengthVector
            });

            var plantExtrude = new Extrude(new Profile(plantPolygon), Height, Vector3.ZAxis);
            Representation = new Representation(plantExtrude);
        }
    }
}
