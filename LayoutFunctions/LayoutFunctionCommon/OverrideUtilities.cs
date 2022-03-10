using System;
using Elements;
using Elements.Geometry;
using System.Linq;
using System.Collections.Generic;

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
                e.AdditionalProperties["gltfLocation"] = (e.BaseDefinition as ContentElement)?.GltfLocation;
            }
            if (overrides != null && overrides.FurnitureLocations != null)
            {
                foreach (var positionOverride in overrides.FurnitureLocations)
                {
                    IEnumerable<ElementInstance> elementInstances = allElementInstances;
                    if (positionOverride.Identity.GltfLocation != null)
                    {
                        elementInstances = allElementInstances
                            .Where(el => el.BaseDefinition is ContentElement contentElement
                                         && contentElement.GltfLocation.Equals(positionOverride.Identity.GltfLocation));
                    }
                    var matchingElement = elementInstances.OrderBy(el => el.Transform.Origin.DistanceTo(positionOverride.Identity.OriginalLocation)).FirstOrDefault();
                    if (matchingElement == null)
                    {
                        continue;
                    }
                    try
                    {
                        matchingElement.Transform.Matrix = positionOverride.Value.Transform.Matrix;
                        Identity.AddOverrideIdentity(matchingElement, positionOverride);
                    }
                    catch
                    {
                        Console.WriteLine("failed to apply an override.");
                    }
                }
            }
        }
    }
}
