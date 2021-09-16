using System;
using Newtonsoft.Json;

namespace Elements
{
    public partial class ProgramRequirement
    {
        public int CountPlaced { get; set; }
        public int RemainingToPlace
        {
            get
            {
                return this.SpaceCount - this.CountPlaced;
            }
        }

        [JsonProperty("Qualified Program Name")]
        public string QualifiedProgramName => String.IsNullOrWhiteSpace(this.ProgramGroup) ? this.ProgramName : $"{this.ProgramGroup} - {this.ProgramName}";
    }
}