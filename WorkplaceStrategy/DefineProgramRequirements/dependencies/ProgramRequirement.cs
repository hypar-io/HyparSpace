using System;
using Newtonsoft.Json;

namespace Elements
{
    public partial class ProgramRequirement : Element
    {
        [JsonProperty("Qualified Program Name")]
        public string QualifiedProgramName => String.IsNullOrEmpty(this.ProgramGroup) ? this.ProgramName : $"{this.ProgramGroup} - {this.ProgramName}";

        public Guid? SpaceConfig { get; set; }

        public Guid? Catalog { get; set; }

        public bool Enclosed { get; set; }

    }
}