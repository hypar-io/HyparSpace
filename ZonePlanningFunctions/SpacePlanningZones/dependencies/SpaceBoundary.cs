using System.Collections.Generic;
using Elements.Geometry;
using System;
using Elements.Geometry.Solids;
using System.Linq;

namespace Elements
{
    public partial class SpaceBoundary
    {
        public static void SetRequirements(IEnumerable<ProgramRequirement> reqs)
        {
            Requirements = reqs.ToDictionary(v => v.ProgramName, v => v);
            foreach (var kvp in Requirements)
            {
                var color = kvp.Value.Color;
                color.Alpha = 0.5;
                MaterialDict[kvp.Key] = new Material(kvp.Value.ProgramName, color);
            }
        }

        /// <summary>
        /// Static properties can persist across executions! need to reset to defaults w/ every execution.
        /// </summary>
        public static void Reset()
        {
            Requirements.Clear();
            MaterialDict = new Dictionary<string, Material>(materialDefaults);
        }

        public static Dictionary<string, ProgramRequirement> Requirements { get; private set; } = new Dictionary<string, ProgramRequirement>();

        private static Dictionary<string, Material> materialDefaults = new Dictionary<string, Material> {
            {"unspecified", new Material("Unspecified Space Type", new Color(0.8, 0.8, 0.8, 0.3))},
            {"unrecognized", new Material("Unspecified Space Type", new Color(0.8, 0.8, 0.2, 0.3))},
            {"Circulation", new Material("Circulation", new Color(0.996,0.965,0.863,0.5))}, //✅
            {"Open Office", new Material("Open Office", new Color(0.435,0.627,0.745,0.5))}, //✅  https://hypar-content-catalogs.s3-us-west-2.amazonaws.com/35cb4053-4d39-47ef-9673-2dccdae1433b/SteelcaseOpenOffice-35cb4053-4d39-47ef-9673-2dccdae1433b.json
            {"Private Office", new Material("Private Office", new Color(0.122,0.271,0.361,0.5))}, //✅ https://hypar-content-catalogs.s3-us-west-2.amazonaws.com/69be76de-aaa1-4097-be0c-a97eb44d62e6/Private+Office-69be76de-aaa1-4097-be0c-a97eb44d62e6.json
            {"Lounge", new Material("Lounge", new Color(1.000,0.584,0.196,0.5))}, //✅ https://hypar-content-catalogs.s3-us-west-2.amazonaws.com/52df2dc8-3107-43c9-8a9f-e4b745baca1c/Steelcase-Lounge-52df2dc8-3107-43c9-8a9f-e4b745baca1c.json
            {"Classroom", new Material("Classroom", new Color(0.796,0.914,0.796,0.5))}, //✅ https://hypar-content-catalogs.s3-us-west-2.amazonaws.com/b23810e9-f565-4845-9b08-d6beb6223bea/Classroom-b23810e9-f565-4845-9b08-d6beb6223bea.json 
            {"Pantry", new Material("Pantry", new Color(0.5,0.714,0.745,0.5))}, //✅ https://hypar-content-catalogs.s3-us-west-2.amazonaws.com/599d1640-2584-42f7-8de1-e988267c360a/Pantry-599d1640-2584-42f7-8de1-e988267c360a.json
            {"Meeting Room", new Material("Meeting Room", new Color(0.380,0.816,0.608,0.5))}, //✅ https://hypar-content-catalogs.s3-us-west-2.amazonaws.com/251d637c-c570-43bd-ab33-f59f337506bb/Catalog-251d637c-c570-43bd-ab33-f59f337506bb.json
            {"Phone Booth", new Material("Phone Booth", new Color(0.976,0.788,0.129,0.5))},  //✅ https://hypar-content-catalogs.s3-us-west-2.amazonaws.com/deacf056-2d7e-4396-8bdf-f30d581f2747/Phone+Booths-deacf056-2d7e-4396-8bdf-f30d581f2747.json
            {"Support", new Material("Support", new Color(0.447,0.498,0.573,0.5))},
            {"Reception", new Material("Reception", new Color(0.576,0.463,0.753,0.5))}, //✅ https://hypar-content-catalogs.s3-us-west-2.amazonaws.com/8762e4ec-7ddd-49b1-bcca-3f303f69f453/Reception-8762e4ec-7ddd-49b1-bcca-3f303f69f453.json 
            {"Data Hall", new Material("Data Hall", new Color(0.46,0.46,0.48,0.5))}
        };
        public static Dictionary<string, Material> MaterialDict { get; private set; } = new Dictionary<string, Material>(materialDefaults);

        public string ProgramName { get; set; }
        private static Random random = new Random(4);
        public static SpaceBoundary Make(Profile profile, string displayName, Transform xform, double height, Vector3? parentCentroid = null, Vector3? individualCentroid = null)
        {
            MaterialDict.TryGetValue(displayName ?? "unspecified", out var material);
            var representation = new Representation(new[] { new Extrude(profile, height, Vector3.ZAxis, false) });
            var name = Requirements.TryGetValue(displayName, out var fullReq) ? fullReq.HyparSpaceType : displayName;
            var sb = new SpaceBoundary(profile, new List<Polygon> { profile.Perimeter }, xform, material ?? MaterialDict["unrecognized"], representation, false, Guid.NewGuid(), name);
            sb.ProgramName = displayName;
            sb.AdditionalProperties.Add("ParentCentroid", parentCentroid ?? xform.OfPoint(profile.Perimeter.Centroid()));
            sb.AdditionalProperties.Add("IndividualCentroid", individualCentroid ?? xform.OfPoint(profile.Perimeter.Centroid()));
            return sb;
        }

        public void SetProgram(string displayName)
        {
            if (!MaterialDict.TryGetValue(displayName ?? "unrecognized", out var material))
            {
                var color = random.NextColor();
                color.Alpha = 0.5;
                MaterialDict[displayName] = new Material(displayName, color);
                material = MaterialDict[displayName];
            }
            this.Material = material;
            this.ProgramName = displayName;
            this.Name = Requirements.TryGetValue(displayName, out var fullReq) ? fullReq.HyparSpaceType : displayName;
        }
    }
}