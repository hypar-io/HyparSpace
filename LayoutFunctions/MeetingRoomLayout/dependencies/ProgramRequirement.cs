using System;
using Newtonsoft.Json;

namespace Elements
{
    public partial class ProgramRequirement : IProgramRequirement
    {
        [JsonProperty("Qualified Program Name")]
        public string QualifiedProgramName => String.IsNullOrWhiteSpace(this.ProgramGroup) ? this.ProgramName : $"{this.ProgramGroup} - {this.ProgramName}";
        public int CountPlaced { get; set; }
    }
}