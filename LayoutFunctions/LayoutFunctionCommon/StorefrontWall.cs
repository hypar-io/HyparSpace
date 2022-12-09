using System;
using Elements.Geometry;

namespace Elements
{
    public class StorefrontWall : StandardWall
    {
        public StorefrontWall(Line centerLine, double thickness, double height, Material material = null, Transform transform = null, Representation representation = null, bool isElementDefinition = false, Guid id = default, string name = null) : base(centerLine, thickness, height, material, transform, representation, isElementDefinition, id, name)
        {
        }
    }
}