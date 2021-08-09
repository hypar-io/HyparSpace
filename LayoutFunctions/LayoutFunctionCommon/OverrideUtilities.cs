using System;
using Elements;
using Elements.Geometry;
using System.Linq;

namespace LayoutFunctionCommon
{
    public class OverrideUtilities
    {
        public static void InstancePositionOverrides(dynamic overrides, Model model)
        {
            var allElementInstances = model.AllElementsOfType<ElementInstance>().ToList();
            if (allElementInstances.Count() == 0)
            {
                return;
            }
            foreach (var e in allElementInstances)
            {
                e.AdditionalProperties["OriginalLocation"] = e.Transform.Origin;
            }
            if (overrides != null)
            {
                foreach (var positionOverride in overrides.FurnitureLocations)
                {
                    var matchingElement = allElementInstances.OrderBy(el => el.Transform.Origin.DistanceTo(positionOverride.Identity.OriginalLocation)).First();
                    matchingElement.Transform.Matrix = positionOverride.Value.Transform.Matrix;
                }
            }
        }
    }
}
