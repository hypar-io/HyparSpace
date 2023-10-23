using Elements;
using System;
using System.Linq;
using System.Collections.Generic;
using Elements.Geometry;
namespace Elements
{
    public partial class SpaceBoundary : ISpaceBoundary
    {
        public Vector3? ParentCentroid { get; set; }
    }
}