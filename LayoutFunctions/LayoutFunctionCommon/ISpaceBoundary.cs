using System.Collections.Generic;
using Elements;
using Elements.Geometry;

namespace Elements
{
    public interface ISpaceBoundary
    {
        Profile Boundary { get; set; }
        Transform Transform { get; set; }
    }

}