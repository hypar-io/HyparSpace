using Elements;
using System;
using System.Linq;
using System.Collections.Generic;
using Elements.Geometry;
using Newtonsoft.Json;
using Elements.Geometry.Solids;
using Flooring;

namespace Elements
{
    // This portion of the FlooringRegion class is yours to edit with your own element behaviors.
    public partial class FlooringRegion
    {

        // Access properties of the element and construct a representation.
        public override void UpdateRepresentations()
        {
            this.Representation = new Lamina(this.Boundary);
            this.Transform.Move(0, 0, 0.001);
        }

        public void UpdateMaterial()
        {
            this.Material = this.Type?.Material ?? new Material(this.Type?.Name ?? "Unknown Flooring Type", Colors.Magenta);
            this.ModifyVertexAttributes = (vs) =>
            {
                vs.uv = new UV(vs.position.X / (this.Type?.TextureSize ?? 1.0), vs.position.Y / (this.Type?.TextureSize ?? 1.0));
                return vs;
            };
        }

        /// <summary>
        /// Construct a new instance of the element.
        /// </summary>
        /// <param name="add">User input at add time.</param>
        public FlooringRegion(FloorTreatmentsOverrideAddition add, IEnumerable<FlooringType> allTypes)
        {
            // Optionally customize this method.
            this.SetAllProperties(add, allTypes);
            this.UpdateMaterial();
        }

        public FlooringRegion(Floor floor, FlooringType flooringType)
        {
            this.Boundary = floor.Profile;
            this.Type = flooringType;
            this.UpdateMaterial();
            this.AddId = floor.AdditionalProperties["Add Id"] + "-floor";
        }

        /// <summary>
        /// Update the element on a subsequent change.
        /// </summary>
        /// <param name="edit">User input at edit time.</param>
        public void Update(FloorTreatmentsOverride edit, IEnumerable<FlooringType> allTypes)
        {
            // Optionally customize this method.
            this.SetAllProperties(edit, allTypes);
            this.UpdateMaterial();
        }
    }
}