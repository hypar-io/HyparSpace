using Flooring;
using Newtonsoft.Json;
namespace Elements
{
    public partial class FlooringType
    {
        [JsonProperty("Add Id")]
        public string AddId { get; set; }

        /// <summary>
        /// Determine whether the provided identity is a match for this object. Auto-generated from the schema.
        /// ⚠️ Do not edit this method: it will be overwritten automatically next
        /// time you run 'hypar init'.
        /// </summary>
        public bool Match(FloorTypesIdentity identity)
        {
            return identity.AddId == this.AddId;
        }

        /// <summary>
        /// Set all properties of the element. Auto-generated from the schema.
        /// ⚠️ Do not edit this method: it will be overwritten automatically next
        /// time you run 'hypar init'.
        /// </summary>
        public void SetAllProperties(FloorTypesOverrideAddition add)
        {
            // Identity
            this.AddId = add.Id;
            // Properties
            this.Thickness = add.Value.Thickness;
            this.Color = add.Value.Color;
            this.Name = add.Value.Name;

        }

        /// <summary>
        /// Set all properties of the element. Auto-generated from the schema.
        /// ⚠️ Do not edit this method: it will be overwritten automatically next
        /// time you run 'hypar init'.
        /// </summary>
        public void SetAllProperties(FloorTypesOverride edit)
        {
            // Properties
            this.Thickness = edit.Value.Thickness;
            this.Color = edit.Value.Color;
            this.Name = edit.Value.Name;
        }
        
        public static List<FlooringType> CreateElements(Overrides overrides, IEnumerable<FlooringType> existingElements = null)
        {
            return overrides.FloorTypes.CreateElements(
                overrides.Additions.FloorTypes,
                overrides.Removals.FloorTypes,
                (add) => new FlooringType(add),
                (elem, identity) => elem.Match(identity),
                (elem, edit) => { elem.Update(edit); return elem; },
                existingElements
            );
        } 
    }
}