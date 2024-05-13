using System;
using Elements.Geometry;

namespace Elements
{   
    /// <summary>
    /// Not to be confused with the frontend concept of parent spaces for
    /// nesting, this interface is used exclusively for open office spaces which
    /// contain open collaboration spaces — the open collaboration spaces get a
    /// `ParentSpace` which points to the open office, so that they transform
    /// with it as expected.
    /// </summary>
    public interface IHasParentSpace
    {
        Guid? ParentSpace { get; set; }

        Polygon ParentBoundary { get; set; }
    }
}