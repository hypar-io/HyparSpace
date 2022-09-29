using Elements;
using System;
using System.Linq;
using System.Collections.Generic;
using Elements.Geometry;
namespace Elements
{
    // This class just "wraps" standard wall so it looks and acts like a wall but doesn't get imported as one. 
    public class Header : StandardWall
    {
        public Header(Line centerLine, double thickness, double height, Material material = null, Transform transform = null, Representation representation = null, bool isElementDefinition = false, Guid id = default, string name = null) : base(centerLine, thickness, height, material, transform, representation, isElementDefinition, id, name)
        {
        }
    }
}