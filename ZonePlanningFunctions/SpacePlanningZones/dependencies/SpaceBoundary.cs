using System.Collections.Generic;
using Elements.Geometry;
using System;
using Elements.Geometry.Solids;
using System.Linq;
using Newtonsoft.Json;

namespace Elements
{
    public partial class SpaceBoundary
    {

        public List<Line> AdjacentCorridorEdges { get; set; } = null;

        [JsonProperty("Program Group")]
        public string ProgramGroup { get; set; }
        public Line AlignmentEdge { get; set; } = null;
        public double AvailableLength { get; set; } = 0;
        public Transform ToAlignmentEdge = null;
        public Transform FromAlignmentEdge = null;

        public Vector3? IndividualCentroid { get; set; } = null;
        public Vector3? ParentCentroid { get; set; } = null;

        public bool AutoPlaced { get; set; } = false;

        public int CountPlaced { get; set; } = 0;

        public int SpaceCount { get; set; } = 1;

        [Newtonsoft.Json.JsonIgnore]
        public LevelElements Level { get; set; }

        [Newtonsoft.Json.JsonIgnore]
        public ProgramRequirement FulfilledProgramRequirement = null;
        public static void SetRequirements(IEnumerable<ProgramRequirement> reqs)
        {
            Requirements = reqs.ToDictionary(v => v.QualifiedProgramName, v => v);
            foreach (var kvp in Requirements)
            {
                var color = kvp.Value.Color;
                color.Alpha = 0.5;
                MaterialDict[kvp.Key] = new Material(kvp.Value.ProgramName, color, specularFactor, glossinessFactor);
            }
        }



        /// <summary>
        /// Static properties can persist across executions! need to reset to defaults w/ every execution.
        /// </summary>
        public static void Reset()
        {
            random = new Random(11);
            Requirements.Clear();
            MaterialDict = new Dictionary<string, Material>(materialDefaults);
        }

        public List<ProgramRequirement> CollectedSpaces { get; set; } = new List<ProgramRequirement>();

        public void Collect(ProgramRequirement req)
        {
            this.AvailableLength -= req.Width.Value;
            req.CountPlaced++;
            this.CollectedSpaces.Add(req);
        }

        public List<SpaceBoundary> ResolveCollected()
        {
            var newSpaces = new List<SpaceBoundary>();
            if (this.CollectedSpaces.Count > 0)
            {
                var runningX = 0.0;
                var bonus = 0.0;
                if (this.AvailableLength < 3)
                {
                    bonus = this.AvailableLength / this.CollectedSpaces.Count;
                }
                foreach (var space in this.CollectedSpaces)
                {
                    var depth = space.Depth.Value * 1.5; // overhang our spaces, since we'll trim them out later
                    var width = space.Width.Value + bonus;
                    if (Math.Abs(this.Depth.Value - depth) < 3)
                    {
                        depth = this.Depth.Value;
                    }
                    Profile idealRect = Polygon.Rectangle(new Vector3(runningX, 0, 0), new Vector3(width + runningX, depth)).TransformedPolygon(this.FromAlignmentEdge);
                    try
                    {
                        var intersection = Profile.Intersection(new[] { idealRect }, new[] { this.Boundary });
                        var largest = intersection.OrderBy(p => p.Area()).LastOrDefault();
                        if (largest != null)
                        {
                            idealRect = largest;
                        }
                    }
                    catch
                    {

                    }
                    var edge = new Line(new Vector3(runningX, 0, 0), new Vector3(width + runningX, 0)).TransformedLine(this.FromAlignmentEdge);
                    var newSb = SpaceBoundary.Make(idealRect, space.ProgramName, this.Transform, this.Representation.SolidOperations.OfType<Extrude>().First().Height, (Vector3)this.ParentCentroid, (Vector3)this.IndividualCentroid, this.AdjacentCorridorEdges);
                    newSb.AlignmentEdge = edge;
                    newSb.Level = Level;
                    runningX += width;
                    newSpaces.Add(newSb);
                }
                if (this.AvailableLength >= 3)
                {
                    Profile idealRect = Polygon.Rectangle(new Vector3(runningX, 0, 0), new Vector3(this.AvailableLength + runningX, this.Depth.Value)).TransformedPolygon(this.FromAlignmentEdge);
                    try
                    {
                        var intersection = Profile.Intersection(new[] { idealRect }, new[] { this.Boundary });
                        var largest = intersection.OrderBy(p => p.Area()).LastOrDefault();
                        if (largest != null)
                        {
                            idealRect = largest;
                        }
                    }
                    catch
                    {

                    }
                    var edge = new Line(new Vector3(runningX, 0, 0), new Vector3(this.AvailableLength + runningX, 0)).TransformedLine(this.FromAlignmentEdge);
                    var newSb = SpaceBoundary.Make(idealRect, this.ProgramName, this.Transform, this.Representation.SolidOperations.OfType<Extrude>().First().Height, (Vector3)this.ParentCentroid, (Vector3)this.IndividualCentroid, this.AdjacentCorridorEdges);
                    newSb.Level = Level;
                    newSb.CalculateDimensions();
                    newSpaces.Add(newSb);
                }
            }
            return newSpaces;
        }
        public static bool TryGetRequirementsMatch(string nameToFind, out ProgramRequirement fullRequirement)
        {
            if (Requirements.TryGetValue(nameToFind, out fullRequirement))
            {
                return true;
            }
            else
            {
                var keyMatch = Requirements.Keys.FirstOrDefault(k => k.EndsWith($" - {nameToFind}"));
                if (keyMatch != null)
                {
                    fullRequirement = Requirements[keyMatch];
                    return true;
                }
            }
            return false;
        }
        public static Dictionary<string, ProgramRequirement> Requirements { get; private set; } = new Dictionary<string, ProgramRequirement>();
        private static double specularFactor = 0.00;
        private static double glossinessFactor = 0.00;
        private static Dictionary<string, Material> materialDefaults = new Dictionary<string, Material> {
            {"unspecified", new Material("Unspecified Space Type", new Color(0.8, 0.8, 0.8, 0.3), specularFactor, glossinessFactor)},
            {"unrecognized", new Material("Unspecified Space Type", new Color(0.8, 0.8, 0.2, 0.3), specularFactor, glossinessFactor)},
            {"Circulation", new Material("Circulation", new Color(0.996,0.965,0.863,0.5), specularFactor, glossinessFactor)}, //✅
            {"Open Office", new Material("Open Office", new Color(0.435,0.627,0.745,0.5), specularFactor, glossinessFactor)}, //✅  https://hypar-content-catalogs.s3-us-west-2.amazonaws.com/35cb4053-4d39-47ef-9673-2dccdae1433b/SteelcaseOpenOffice-35cb4053-4d39-47ef-9673-2dccdae1433b.json
            {"Private Office", new Material("Private Office", new Color(0.122,0.271,0.361,0.5), specularFactor, glossinessFactor)}, //✅ https://hypar-content-catalogs.s3-us-west-2.amazonaws.com/69be76de-aaa1-4097-be0c-a97eb44d62e6/Private+Office-69be76de-aaa1-4097-be0c-a97eb44d62e6.json
            {"Lounge", new Material("Lounge", new Color(1.000,0.584,0.196,0.5), specularFactor, glossinessFactor)}, //✅ https://hypar-content-catalogs.s3-us-west-2.amazonaws.com/52df2dc8-3107-43c9-8a9f-e4b745baca1c/Steelcase-Lounge-52df2dc8-3107-43c9-8a9f-e4b745baca1c.json
            {"Classroom", new Material("Classroom", new Color(0.796,0.914,0.796,0.5), specularFactor, glossinessFactor)}, //✅ https://hypar-content-catalogs.s3-us-west-2.amazonaws.com/b23810e9-f565-4845-9b08-d6beb6223bea/Classroom-b23810e9-f565-4845-9b08-d6beb6223bea.json 
            {"Pantry", new Material("Pantry", new Color(0.5,0.714,0.745,0.5), specularFactor, glossinessFactor)}, //✅ https://hypar-content-catalogs.s3-us-west-2.amazonaws.com/599d1640-2584-42f7-8de1-e988267c360a/Pantry-599d1640-2584-42f7-8de1-e988267c360a.json
            {"Meeting Room", new Material("Meeting Room", new Color(0.380,0.816,0.608,0.5), specularFactor, glossinessFactor)}, //✅ https://hypar-content-catalogs.s3-us-west-2.amazonaws.com/251d637c-c570-43bd-ab33-f59f337506bb/Catalog-251d637c-c570-43bd-ab33-f59f337506bb.json
            {"Phone Booth", new Material("Phone Booth", new Color(0.976,0.788,0.129,0.5), specularFactor, glossinessFactor)},  //✅ https://hypar-content-catalogs.s3-us-west-2.amazonaws.com/deacf056-2d7e-4396-8bdf-f30d581f2747/Phone+Booths-deacf056-2d7e-4396-8bdf-f30d581f2747.json
            {"Support", new Material("Support", new Color(0.447,0.498,0.573,0.5), specularFactor, glossinessFactor)},
            {"Reception", new Material("Reception", new Color(0.576,0.463,0.753,0.5), specularFactor, glossinessFactor)}, //✅ https://hypar-content-catalogs.s3-us-west-2.amazonaws.com/8762e4ec-7ddd-49b1-bcca-3f303f69f453/Reception-8762e4ec-7ddd-49b1-bcca-3f303f69f453.json 
            {"Open Collaboration", new Material("Open Collaboration", new Color(209.0/255, 224.0/255, 178.0/255, 0.5), specularFactor, glossinessFactor)},
            {"Data Hall", new Material("Data Hall", new Color(0.46,0.46,0.48,0.5), specularFactor, glossinessFactor)}
        };
        public static Dictionary<string, Material> MaterialDict { get; private set; } = new Dictionary<string, Material>(materialDefaults);

        [JsonProperty("Program Type")]
        public string ProgramName { get; set; }
        private static Random random = new Random(11);
        public static SpaceBoundary Make(Profile profile, string displayName, Transform xform, double height, Vector3? parentCentroid = null, Vector3? individualCentroid = null, IEnumerable<Line> corridorSegments = null)
        {
            if (profile.Perimeter.IsClockWise())
            {
                profile = profile.Reversed();
            }
            MaterialDict.TryGetValue(displayName ?? "unspecified", out var material);
            var representation = new Representation(new[] { new Extrude(profile, height, Vector3.ZAxis, false) });
            var hasReqMatch = TryGetRequirementsMatch(displayName, out var fullReq);
            var name = hasReqMatch ? fullReq.HyparSpaceType : displayName;
            var sb = new SpaceBoundary(profile, new List<Polygon> { profile.Perimeter }, profile.Area(), null, null, xform, material ?? MaterialDict["unrecognized"], representation, false, Guid.NewGuid(), name);
            if (hasReqMatch)
            {
                fullReq.CountPlaced++;
                sb.FulfilledProgramRequirement = fullReq;
                sb.ProgramGroup = fullReq.ProgramGroup;
            }
            sb.ProgramName = displayName;
            sb.ParentCentroid = parentCentroid ?? xform.OfPoint(profile.Perimeter.Centroid());
            sb.IndividualCentroid = individualCentroid ?? xform.OfPoint(profile.Perimeter.Centroid());

            if (corridorSegments != null)
            {
                sb.AdjacentCorridorEdges = WallGeneration.FindAllEdgesAdjacentToSegments(profile.Perimeter.Segments(), corridorSegments, out var otherSegments);
            }
            return sb;
        }

        public void Remove()
        {
            if (this.Level?.Elements != null && this.Level.Elements.Contains(this))
            {
                this.Level.Elements.Remove(this);
            }
            if (this.FulfilledProgramRequirement != null)
            {
                this.FulfilledProgramRequirement.CountPlaced--;
            }
        }
        public void SetProgram(string displayName)
        {
            if (!MaterialDict.TryGetValue(displayName ?? "unrecognized", out var material))
            {
                var color = random.NextColor();
                color.Alpha = 0.5;
                MaterialDict[displayName] = new Material(displayName, color, specularFactor: 0.0, glossinessFactor: 0.0);
                material = MaterialDict[displayName];
            }
            this.Material = material;
            this.ProgramName = displayName;
            if (this.FulfilledProgramRequirement != null)
            {
                this.FulfilledProgramRequirement.CountPlaced--;
            }
            var hasReqMatch = TryGetRequirementsMatch(displayName, out var fullReq);
            this.Name = hasReqMatch ? fullReq.HyparSpaceType : displayName;
            if (hasReqMatch)
            {
                fullReq.CountPlaced++;
                this.FulfilledProgramRequirement = fullReq;
            }

        }

        public void CalculateDimensions()
        {
            var adjacentEdges = AdjacentCorridorEdges;
            Line alignmentEdge = null;
            // if we're not adjacent to a corridor, use the longest edge
            if (adjacentEdges == null || adjacentEdges.Count() == 0)
            {
                alignmentEdge = Boundary.Perimeter.Segments().OrderBy(s => s.Length()).Last();
            }
            else
            {
                // otherwise use the longest edge that is adjacent to a corridor
                alignmentEdge = adjacentEdges.OrderBy(e => e.Length()).Last();
            }
            var alignmentVector = alignmentEdge.Direction();
            var alignmentTransform = new Transform(alignmentEdge.Start, alignmentVector, Vector3.ZAxis);
            var inverse = new Transform(alignmentTransform);
            inverse.Invert();
            var transformedProfile = Boundary.Perimeter.TransformedPolygon(inverse);
            var bbox = new BBox3(transformedProfile);
            var length = bbox.Max.X - bbox.Min.X;
            var depth = bbox.Max.Y - bbox.Min.Y;
            Length = length;
            Depth = depth;
            AvailableLength = length;
            inverse.Concatenate(new Transform(-bbox.Min.X, 0, 0));
            ToAlignmentEdge = new Transform(inverse);
            FromAlignmentEdge = new Transform(inverse);
            FromAlignmentEdge.Invert();
            AlignmentEdge = new Line(new Vector3(0, 0), new Vector3(length, 0)).TransformedLine(FromAlignmentEdge);
        }

        public void SetLevelProperties(LevelVolume volume)
        {
            this.AdditionalProperties["Building Name"] = volume.BuildingName;
            this.AdditionalProperties["Level Name"] = volume.Name;
        }
    }
}