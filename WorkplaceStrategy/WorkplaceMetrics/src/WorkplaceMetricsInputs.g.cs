// This code was generated by Hypar.
// Edits to this code will be overwritten the next time you run 'hypar init'.
// DO NOT EDIT THIS FILE.

using Elements;
using Elements.GeoJSON;
using Elements.Geometry;
using Elements.Geometry.Solids;
using Elements.Validators;
using Elements.Serialization.JSON;
using Hypar.Functions;
using Hypar.Functions.Execution;
using Hypar.Functions.Execution.AWS;
using System;
using System.Collections.Generic;
using System.Linq;
using Line = Elements.Geometry.Line;
using Polygon = Elements.Geometry.Polygon;

namespace WorkplaceMetrics
{
    #pragma warning disable // Disable all warnings

    [System.CodeDom.Compiler.GeneratedCode("NJsonSchema", "10.1.21.0 (Newtonsoft.Json v12.0.0.0)")]
    
    public  class WorkplaceMetricsInputs : S3Args
    
    {
        [Newtonsoft.Json.JsonConstructor]
        
        public WorkplaceMetricsInputs(WorkplaceMetricsInputsCalculationMode @calculationMode, int @totalHeadcount, double @deskSharingRatio, IList<Polygon> @uSFExclusions, string bucketName, string uploadsBucket, Dictionary<string, string> modelInputKeys, string gltfKey, string elementsKey, string ifcKey):
        base(bucketName, uploadsBucket, modelInputKeys, gltfKey, elementsKey, ifcKey)
        {
            var validator = Validator.Instance.GetFirstValidatorForType<WorkplaceMetricsInputs>();
            if(validator != null)
            {
                validator.PreConstruct(new object[]{ @calculationMode, @totalHeadcount, @deskSharingRatio, @uSFExclusions});
            }
        
            this.CalculationMode = @calculationMode;
            this.TotalHeadcount = @totalHeadcount;
            this.DeskSharingRatio = @deskSharingRatio;
            this.USFExclusions = @uSFExclusions;
        
            if(validator != null)
            {
                validator.PostConstruct(this);
            }
        }
    
        [Newtonsoft.Json.JsonProperty("Calculation Mode", Required = Newtonsoft.Json.Required.DisallowNull, NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
        [Newtonsoft.Json.JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
        public WorkplaceMetricsInputsCalculationMode CalculationMode { get; set; } = WorkplaceMetricsInputsCalculationMode.Fixed_Headcount;
    
        /// <summary>How many people will occupy this workspace?</summary>
        [Newtonsoft.Json.JsonProperty("Total Headcount", Required = Newtonsoft.Json.Required.DisallowNull, NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
        [System.ComponentModel.DataAnnotations.Range(0, int.MaxValue)]
        public int TotalHeadcount { get; set; }
    
        /// <summary>What is the assumed sharing ratio: How many people for every desk? A value of 1 means one desk for every person; A value of 2 means there's only one desk for every two people.</summary>
        [Newtonsoft.Json.JsonProperty("Desk Sharing Ratio", Required = Newtonsoft.Json.Required.DisallowNull, NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
        [System.ComponentModel.DataAnnotations.Range(1D, double.MaxValue)]
        public double DeskSharingRatio { get; set; } = 1D;
    
        /// <summary>Draw regions around areas intended to be excluded from USF calculation. This typically includes elevator shafts and stairwells for a full floor lease.</summary>
        [Newtonsoft.Json.JsonProperty("USF Exclusions", Required = Newtonsoft.Json.Required.DisallowNull, NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
        public IList<Polygon> USFExclusions { get; set; }
    
    
    }
    
    [System.CodeDom.Compiler.GeneratedCode("NJsonSchema", "10.1.21.0 (Newtonsoft.Json v12.0.0.0)")]
    public enum WorkplaceMetricsInputsCalculationMode
    {
        [System.Runtime.Serialization.EnumMember(Value = @"Fixed Headcount")]
        Fixed_Headcount = 0,
    
        [System.Runtime.Serialization.EnumMember(Value = @"Fixed Sharing Ratio")]
        Fixed_Sharing_Ratio = 1,
    
    }
}