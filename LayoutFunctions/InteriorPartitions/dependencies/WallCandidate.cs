using Elements;
using System;
using System.Linq;
using System.Collections.Generic;
using Elements.Geometry;
namespace Elements
{
    public partial class WallCandidate
    {
        public (double innerWidth, double outerWidth)? Thickness { get; set; }
    }
}