using Elements;
using Elements.Geometry;
using System.Collections.Generic;
using Elements.Components;
using System.Linq;
using System.IO;
using Newtonsoft.Json;
using System;
using Elements.Spatial;
using LayoutFunctionCommon;

namespace LoungeLayout
{
    public static class LoungeLayout
    {
        /// <summary>
        /// Map between the layout and the number of seats it lays out
        /// </summary>
        private static Dictionary<string, int> _configSeats = new Dictionary<string, int>()
        {
            ["Configuration A"] = 9,
            ["Configuration B"] = 4,
            ["Configuration C"] = 36,
            ["Configuration D"] = 8,
            ["Configuration E"] = 16,
            ["Configuration F"] = 13,
            ["Configuration G"] = 16,
            ["Configuration H"] = 9,
            ["Configuration I"] = 4,
            ["Configuration J"] = 18,
            ["Configuration K"] = 6,
            ["Configuration L"] = 3,
            ["Configuration M"] = 2,
        };

        private static readonly List<ElementProxy<SpaceBoundary>> proxies = new List<ElementProxy<SpaceBoundary>>();

        /// <summary>
        /// The LoungeLayout function.
        /// </summary>
        /// <param name="model">The input model.</param>
        /// <param name="input">The arguments to the execution.</param>
        /// <returns>A LoungeLayoutOutputs instance containing computed results and the model with any new elements.</returns>
        public static LoungeLayoutOutputs Execute(Dictionary<string, Model> inputModels, LoungeLayoutInputs input)
        {
            Elements.Serialization.glTF.GltfExtensions.UseReferencedContentExtension = true;
            var output = new LoungeLayoutOutputs();

            Func<IEnumerable<LevelElements>, Dictionary<Guid, ISpaceSettingsOverride<SpaceSettingsValue>>> overridesBySpaceBoundaryId = (levels) => {
                return OverrideUtilities.GetOverridesBySpaceBoundaryId<ISpaceSettingsOverride<SpaceSettingsValue>, SpaceBoundary, LevelElements>(input.Overrides?.SpaceSettings.Select(s => s as ISpaceSettingsOverride<SpaceSettingsValue>).ToList(), (ov) => (ov as SpaceSettingsOverride).Identity.ParentCentroid, levels);
            };

            Func<Dictionary<Guid, ISpaceSettingsOverride<SpaceSettingsValue>>, SpaceBoundary, IEnumerable<SpaceBoundary>, SpaceSettingsValue> spaceSettingsValue = (overridesId, room, roomBoundaries) => {
                return OverrideUtilities.MatchApplicableOverride<SpaceBoundary, SpaceSettingsOverride, SpaceSettingsValue>(
                            overridesId.ToDictionary(id => id.Key, id => id.Value as SpaceSettingsOverride),
                            OverrideUtilities.GetSpaceBoundaryProxy(room, roomBoundaries.Proxies(OverrideUtilities.SpaceBoundaryOverrideDependencyName)),
                            new SpaceSettingsValue(false, false),
                            proxies).Value;
            };

            LayoutStrategies.StandardLayoutOnAllLevels<LevelElements, LevelVolume, SpaceBoundary, CirculationSegment, SpaceSettingsValue>("Lounge", inputModels, input.Overrides, output.Model, false, "./LoungeConfigurations.json", default, CountSeats, overridesBySpaceBoundaryId, spaceSettingsValue);
            
            output.Model.AddElements(proxies);
            return output;
        }

        private static int CountSeats(LayoutInstantiated layout)
        {
            return layout != null && _configSeats.TryGetValue(layout.ConfigName, out var seatsCount) && seatsCount > 0 ? seatsCount : 0;
        }
    }

}