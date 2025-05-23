using System;
using System.Collections.Generic;
using Elements;
using Newtonsoft.Json;

namespace Elements
{
    public interface IProgramRequirement
    {
        [JsonProperty("Hypar Space Type")]
        public string HyparSpaceType { get; set; }
        public Guid? SpaceConfig { get; set; }
        public Guid? Catalog { get; set; }
        public Guid Id { get; set; }
    }
}