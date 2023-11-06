using Flooring;
using Newtonsoft.Json;
namespace Elements
{
    public partial class FlooringRegion
    {
        [JsonProperty("Add Id")]
        public string AddId { get; set; }

        /// <summary>
        /// Determine whether the provided identity is a match for this object. Auto-generated from the schema.
        /// ⚠️ Do not edit this method: it will be overwritten automatically next
        /// time you run 'hypar init'.
        /// </summary>
        public bool Match(FloorTreatmentsIdentity identity)
        {
            return identity.AddId == this.AddId;
        }

        /// <summary>
        /// Set all properties of the element. Auto-generated from the schema.
        /// ⚠️ Do not edit this method: it will be overwritten automatically next
        /// time you run 'hypar init'.
        /// </summary>
        public void SetAllProperties(FloorTreatmentsOverrideAddition add, IEnumerable<FlooringType> allTypes)
        {
            // Identity
            this.AddId = add.Id;
            // Properties
            this.Type = allTypes.FirstOrDefault((t) => t.Id == add.Value.Type.Id) ?? allTypes.FirstOrDefault((t) => t.Name == add.Value.Type.Name);
            this.Boundary = add.Value.Boundary;

        }

        /// <summary>
        /// Set all properties of the element. Auto-generated from the schema.
        /// ⚠️ Do not edit this method: it will be overwritten automatically next
        /// time you run 'hypar init'.
        /// </summary>
        public void SetAllProperties(FloorTreatmentsOverride edit, IEnumerable<FlooringType> allTypes)
        {
            // Identity
            this.AddId = edit.Id;
            // Properties
            this.Type = allTypes.FirstOrDefault((t) => t.Id == edit.Value.Type.Id) ?? allTypes.FirstOrDefault((t) => t.Name == edit.Value.Type.Name);
            this.Boundary = edit.Value.Boundary;

        }

        public static List<FlooringRegion> CreateElements(Overrides overrides, IEnumerable<FlooringType> flooringTypes, IEnumerable<FlooringRegion> existingElements = null)
        {
            return overrides.FloorTreatments.CreateElements(
                overrides.Additions.FloorTreatments,
                overrides.Removals.FloorTreatments,
                (add) => new FlooringRegion(add, flooringTypes),
                (elem, identity) => elem.Match(identity),
                (elem, edit) => { elem.Update(edit, flooringTypes); return elem; },
                existingElements
            );
        }
    }
}