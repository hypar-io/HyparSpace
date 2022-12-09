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
                    // we use a cutoff so this override doesn't accidentally
                    // apply to some other random element from a different
                    // space. It would be better / more reliable if we could use an "add id" of
                    // the space boundary these were created from. 
                    var matchingElement = elementInstances
                        .Where(el => el.Transform.Origin.DistanceTo(positionOverride.Identity.OriginalLocation) < 2.0)
                        .OrderBy(el => el.Transform.Origin.DistanceTo(positionOverride.Identity.OriginalLocation)).FirstOrDefault();
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
