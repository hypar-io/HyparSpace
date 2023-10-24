using System;
using System.Collections.Generic;
using Elements;
using Elements.Geometry;
using Newtonsoft.Json;

namespace Elements
{
    public interface ISpaceBoundary
    {
        string Name { get; set; }
        double Height { get; set; }
        Profile Boundary { get; set; }
        Transform Transform { get; set; }

        Vector3? ParentCentroid { get; set; }

        Guid Id { get; set; }

        [JsonProperty("Hypar Space Type", Required = Newtonsoft.Json.Required.Default, NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
        public string HyparSpaceType { get; set; }

    }

}