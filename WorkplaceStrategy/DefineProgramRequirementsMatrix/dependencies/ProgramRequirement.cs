using System;
using Newtonsoft.Json;

namespace Elements
{
    public partial class ProgramRequirement : Element
    {
        [JsonProperty("Qualified Program Name")]
        public string QualifiedProgramName => String.IsNullOrEmpty(this.ProgramGroup) ? this.ProgramName : $"{this.ProgramGroup} - {this.ProgramName}";
    }
}