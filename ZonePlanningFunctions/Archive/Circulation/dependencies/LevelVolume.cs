using Newtonsoft.Json;
using System.Collections.Generic;
using Elements.Geometry;
using System;

namespace Elements
{
    public partial class LevelVolume
    {
        // This is just a convenience attachment for the proxy, so that we can grab it
        // in the code anywhere we have access to the level volume. It won't be serialized
        // with the level — but it will be added separately the model.
        [JsonIgnore]
        public ElementProxy<LevelVolume> Proxy { get; set; }

        // a hint about where corridors should go for bar buildings
        public List<Line> Skeleton { get; set; }

        // A hint about where corridors should go to gain access to vertical
        // circulation. Not serialized w/ the level volume — populated in this
        // function.
        public List<CorridorCandidate> CorridorCandidates { get; set; } = new List<CorridorCandidate>();

        [JsonProperty("Primary Use Category")]
        public string PrimaryUseCategory { get; set; }
    }
}