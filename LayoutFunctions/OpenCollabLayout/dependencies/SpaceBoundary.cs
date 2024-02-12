using System;
using Elements.Geometry;
using Newtonsoft.Json;
namespace Elements
{
    public partial class SpaceBoundary : GeometricElement, ISpaceBoundary, IHasParent
    {
        public Vector3? ParentCentroid { get; set; }
        [JsonProperty("Config Id")]
        public string ConfigId { get; set; }

        public Guid? Parent { get; set; }

        public Polygon ParentBoundary { get; set; }
    }
}