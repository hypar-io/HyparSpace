using System;
using System.Collections.Generic;
using Elements;
using Elements.Geometry;

namespace Elements
{
    public interface ISpaceBoundary
    {
        string Name { get; set; }
        Profile Boundary { get; set; }
        Transform Transform { get; set; }

        Vector3? ParentCentroid { get; set; }

        Guid Id { get; set; }

        string HyparSpaceType { get; set; }
    }

}