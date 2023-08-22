using System;
using Elements;
using System.Linq;
using System.Collections.Generic;
using Elements.Components;
using Elements.Geometry;

namespace LayoutFunctionCommon
{
    public class OverrideUtilities
    {
        public static readonly string SpaceBoundaryOverrideDependency = "Space Planning Zones";
        public static readonly string SpaceBoundaryOverrideName = "Space Settings";

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

        public static Dictionary<Guid, TOverride> GetOverridesBySpaceBoundaryId<TOverride, TSpaceBoundary, TLevelElements>(
            IList<TOverride> overrides, 
            Func<TOverride, Vector3> getCentroid, 
            IEnumerable<TLevelElements> levels) where TOverride : IOverride where TSpaceBoundary : ISpaceBoundary where TLevelElements : ILevelElements
        {
            var overridesBySpaceBoundaryId = new Dictionary<Guid, TOverride>();

            if (getCentroid == null || levels == null)
            {
                return overridesBySpaceBoundaryId;
            }

            foreach (var spaceOverride in overrides ?? new List<TOverride>())
            {
                var matchingBoundary =
                levels.SelectMany(l => l.Elements)
                    .OfType<TSpaceBoundary>()
                    .OrderBy(ob => ob.ParentCentroid.Value
                    .DistanceTo(getCentroid(spaceOverride)))
                    .FirstOrDefault();
                if (matchingBoundary == null)
                {
                    continue;
                }

                if (overridesBySpaceBoundaryId.ContainsKey(matchingBoundary.Id))
                {
                    var mbCentroid = matchingBoundary.ParentCentroid.Value;
                    if (getCentroid(overridesBySpaceBoundaryId[matchingBoundary.Id]).DistanceTo(mbCentroid) > getCentroid(spaceOverride).DistanceTo(mbCentroid))
                    {
                        overridesBySpaceBoundaryId[matchingBoundary.Id] = spaceOverride;
                    }
                }
                else
                {
                    overridesBySpaceBoundaryId.Add(matchingBoundary.Id, spaceOverride);
                }
            }

            return overridesBySpaceBoundaryId;
        }

        public static ElementProxy<TSpaceBoundary> GetSpaceBoundaryProxy<TSpaceBoundary>(
            TSpaceBoundary spaceBoundary,
            IEnumerable<ElementProxy<TSpaceBoundary>> allSpaceBoundaries = null,
            Dictionary<string, dynamic> parameters = null) where TSpaceBoundary : Element, ISpaceBoundary
        {
            var proxy = allSpaceBoundaries?.Proxy(spaceBoundary) ?? spaceBoundary.Proxy(SpaceBoundaryOverrideDependency);
            if (parameters != null)
            {
                foreach (var parameter in parameters)
                {
                    proxy.AdditionalProperties.Add(parameter.Key, parameter.Value);
                }
            }
            return proxy;
        }
        
        // public static ElementProxy<TSpaceBoundary> CreateSettingsProxy<TSpaceBoundary>(double collabSpaceDensity, double gridRotation, double aisleWidth, TSpaceBoundary ob, string deskType) where TSpaceBoundary : Element, ISpaceBoundary
        // {
        //     var proxy = ob.Proxy("Space Settings");
        //     proxy.AdditionalProperties.Add("Desk Type", deskType);
        //     proxy.AdditionalProperties.Add("Integrated Collaboration Space Density", collabSpaceDensity);
        //     proxy.AdditionalProperties.Add("Aisle Width", aisleWidth);
        //     proxy.AdditionalProperties.Add("Grid Rotation", gridRotation);
        //     return proxy;
        // }
        
        public static TSpaceSettingsOverride MatchApplicableOverride<TSpaceBoundary, TSpaceSettingsOverride, TSpaceSettingsOverrideValueType>(
            Dictionary<Guid, TSpaceSettingsOverride> overridesById,
            ElementProxy<TSpaceBoundary> boundaryProxy,
            TSpaceSettingsOverrideValueType defaultValue,
            List<ElementProxy<TSpaceBoundary>> proxies
            ) 
            where TSpaceSettingsOverrideValueType : ISpaceSettingsOverrideValue 
            where TSpaceBoundary : Element, ISpaceBoundary 
            where TSpaceSettingsOverride : ISpaceSettingsOverride<TSpaceSettingsOverrideValueType>, IOverride, new()
        {
            var overrideName = SpaceBoundaryOverrideName;
            TSpaceSettingsOverride config;

            // See if we already have matching override attached
            var existingOverrideId = boundaryProxy.OverrideIds<TSpaceSettingsOverride>(overrideName).FirstOrDefault();
            if (existingOverrideId != null)
            {
                if (overridesById.TryGetValue(Guid.Parse(existingOverrideId), out config))
                {
                    return config;
                }
            }

            // Try to match from identity in configs dictionary. Use a default in case none found
            if (!overridesById.TryGetValue(boundaryProxy.ElementId, out config))
            {
                config = new TSpaceSettingsOverride()
                {
                    Id = Guid.NewGuid().ToString(),
                    Value = defaultValue
                };
                overridesById.Add(boundaryProxy.ElementId, config);
            }

            // Attach the identity and values data to the proxy
            boundaryProxy.AddOverrideIdentity(overrideName, config.Id, config.GetIdentity());
            boundaryProxy.AddOverrideValue(overrideName, config.Value);

            // Make sure proxies list has the proxy so that it will serialize in the model.
            if (!proxies.Contains(boundaryProxy))
            {
                proxies.Add(boundaryProxy);
            }

            return config;
        }

        public static (
            ContentConfiguration selectedConfig,
            double rotation,
            double collabDensity,
            double aisleWidth,
            double backToBackWidth,
            string deskTypeName
        ) GetSpaceSettings<TSpaceBoundary, TSpaceSettingsOverride, TSpaceSettingsOverrideValueType>(
            TSpaceBoundary ob,
            ContentConfiguration defaultConfig,
            double rotation,
            double collabDensity,
            double aisleWidth,
            double backToBackWidth,
            string deskType,
            Dictionary<Guid, TSpaceSettingsOverride> overridesById,
            SpaceConfiguration configs,
            ElementProxy<TSpaceBoundary> proxy,
            Func<TSpaceSettingsOverride, ContentConfiguration> createCustomDesk = null
            ) 
            where TSpaceSettingsOverrideValueType : ISpaceSettingsOverrideOpenOfficeValue 
            where TSpaceBoundary : Element, ISpaceBoundary 
            where TSpaceSettingsOverride : ISpaceSettingsOverride<TSpaceSettingsOverrideValueType>, IOverride
        {
            var selectedConfig = defaultConfig;
            var deskTypeName = deskType;
            if (overridesById.ContainsKey(ob.Id))
            {
                var spaceOverride = overridesById[ob.Id];
                if (createCustomDesk != null)
                {
                    selectedConfig = createCustomDesk(spaceOverride);
                }
                if (createCustomDesk == null || selectedConfig == null)
                {
                    selectedConfig = configs[spaceOverride.Value.GetDeskType];
                }
                Identity.AddOverrideIdentity(proxy, spaceOverride);
                Identity.AddOverrideValue(proxy, "Space Settings", spaceOverride.Value);
                rotation = spaceOverride.Value.GridRotation;
                collabDensity = spaceOverride.Value.IntegratedCollaborationSpaceDensity;
                aisleWidth = double.IsNaN(spaceOverride.Value.AisleWidth) ? aisleWidth : spaceOverride.Value.AisleWidth;
                backToBackWidth = double.IsNaN(spaceOverride.Value.BackToBackWidth) ? backToBackWidth : spaceOverride.Value.BackToBackWidth;
                deskTypeName = spaceOverride.Value.GetDeskType;
            }
            return (selectedConfig, rotation, collabDensity, aisleWidth, backToBackWidth, deskTypeName);
        }
    }
}
