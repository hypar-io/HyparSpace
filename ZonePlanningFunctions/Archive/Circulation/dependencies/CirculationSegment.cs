using System;
using Circulation;
using Elements.Geometry;
using Newtonsoft.Json;

namespace Elements
{
    public class CirculationSegment : Floor
    {
        public ThickenedPolyline Geometry { get; set; }

        [JsonProperty("Original Geometry")]
        public Polyline OriginalGeometry { get; set; }

        public Guid Level { get; set; }
        public CirculationSegment(Profile profile, double thickness, Transform transform = null, Material material = null, Representation representation = null, bool isElementDefinition = false, Guid id = default, string name = null) : base(profile, thickness, transform, material, representation, isElementDefinition, id, name)
        {
        }
    }
}