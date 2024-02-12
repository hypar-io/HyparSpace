using System;
using Elements.Geometry;

namespace Elements
{
    public interface IHasParent
    {
        Guid? Parent { get; set; }

        Polygon ParentBoundary { get; set; }
    }
}