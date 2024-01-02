using System;
using Newtonsoft.Json;

namespace Elements
{
    public partial class ProgramRequirement : IProgramRequirement
    {
        [JsonProperty("Layout Type")]
        public LayoutType LayoutType { get; set; }
        public Guid? SpaceConfig { get; set; }

        public Guid? Catalog { get; set; }

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
    }
}