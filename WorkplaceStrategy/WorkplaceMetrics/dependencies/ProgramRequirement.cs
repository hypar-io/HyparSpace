using Elements;
using System;
using System.Linq;
using System.Collections.Generic;
using Elements.Geometry;
using Newtonsoft.Json;

namespace Elements
{
    public partial class ProgramRequirement
    {
        public double GetAreaPerSpace()
        {
            if (this.AreaPerSpace != 0)
            {
                return this.AreaPerSpace;
            }
            if (this.Width != null && this.Depth != null && this.Width != 0 && this.Depth != 0)
            {
                return this.Width.Value * this.Depth.Value;
            }
            return 0;
        }

        [JsonProperty("Qualified Program Name")]
        public string QualifiedProgramName => String.IsNullOrWhiteSpace(this.ProgramGroup) ? this.ProgramName : $"{this.ProgramGroup} - {this.ProgramName}";

    }
}