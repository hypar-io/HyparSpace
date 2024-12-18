//----------------------
// <auto-generated>
//     Generated using the NJsonSchema v10.1.21.0 (Newtonsoft.Json v13.0.0.0) (http://NJsonSchema.org)
// </auto-generated>
//----------------------
using Elements;
using Elements.GeoJSON;
using Elements.Geometry;
using Elements.Geometry.Solids;
using Elements.Spatial;
using Elements.Validators;
using Elements.Serialization.JSON;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using Line = Elements.Geometry.Line;
using Polygon = Elements.Geometry.Polygon;

namespace Elements
{
    #pragma warning disable // Disable all warnings

    /// <summary>Represents a mapping between discrete values and colors</summary>
    [JsonConverter(typeof(Elements.Serialization.JSON.JsonInheritanceConverter), "discriminator")]
    [System.CodeDom.Compiler.GeneratedCode("NJsonSchema", "10.1.21.0 (Newtonsoft.Json v13.0.0.0)")]
    public partial class ColorScheme : Element
    {
        [JsonConstructor]
        public ColorScheme(Mapping @mapping, string @propertyName, System.Guid @id = default, string @name = null)
            : base(id, name)
        {
            this.Mapping = @mapping;
            this.PropertyName = @propertyName;
            }
        
        
        // Empty constructor
        public ColorScheme()
            : base()
        {
        }
    
        [JsonProperty("Mapping", Required = Newtonsoft.Json.Required.DisallowNull, NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
        public Mapping Mapping { get; set; }
    
        /// <summary>The property name this color scheme applies to</summary>
        [JsonProperty("Property Name", Required = Newtonsoft.Json.Required.DisallowNull, NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
        public string PropertyName { get; set; }
    
    
    }
}