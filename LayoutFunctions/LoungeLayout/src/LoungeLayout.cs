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
        // Map between the layout and the number of seats it lays out
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
            LayoutStrategies.StandardLayoutOnAllLevels<LevelElements, LevelVolume, SpaceBoundary, CirculationSegment>("Lounge", inputModels, input.Overrides, output.Model, false, "./LoungeConfigurations.json", default, CountSeats);

            return output;
        }

        private static int CountSeats(LayoutInstantiated layout)
        {
            return layout != null && _configSeats.TryGetValue(layout.ConfigName, out var seatsCount) && seatsCount > 0 ? seatsCount : 0;
        }
    }

}