using Elements;
using System;
using System.Linq;
using System.Collections.Generic;
using Elements.Geometry;
namespace Elements
{
    public partial class SpaceBoundary
    {
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
        public static void SetRequirements(IEnumerable<ProgramRequirement> reqs, List<string> warnings)
        {
            Requirements.Clear();

            var badProgramNames = new List<string>();

            foreach (var req in reqs)
            {
                try
                {
                    Requirements.Add(req.QualifiedProgramName, req);
                }
                catch
                {
                    badProgramNames.Add($"{req.QualifiedProgramName}");
                }
            }

            if (badProgramNames.Count > 0)
            {
                warnings.Add(@"There are duplicate Program Names in your Program Requirements.
                    Please ensure that all Program Names are unique if you want to use them in Workplace Metrics.");
                warnings.Add("Duplicate Program Names:");
                warnings.AddRange(badProgramNames);
            }

            foreach (var kvp in Requirements)
            {
                var color = kvp.Value.Color ?? Colors.Magenta;
                color.Alpha = 0.5;
            }
        }
    }
}