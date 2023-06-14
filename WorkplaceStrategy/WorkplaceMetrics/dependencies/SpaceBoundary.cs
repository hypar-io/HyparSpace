using Elements;
using System;
using System.Linq;
using System.Collections.Generic;
using Elements.Geometry;
namespace Elements
{
    public partial class SpaceBoundary
    {
        public bool IsCounted { get; set; }
        public Vector3? ParentCentroid { get; set; }

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
        public int SpaceCount { get; set; } = 1;
        public static void SetRequirements(IEnumerable<ProgramRequirement> reqs)
        {
            Requirements = reqs.ToDictionary(v => v.QualifiedProgramName, v => v);
            foreach (var kvp in Requirements)
            {
                var color = kvp.Value.Color;
                color.Alpha = 0.5;
            }
        }
    }
}