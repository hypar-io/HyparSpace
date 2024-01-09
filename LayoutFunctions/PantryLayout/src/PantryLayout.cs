using Elements;
using Elements.Geometry;
using System.Collections.Generic;
using Elements.Components;
using System.Linq;
using System.IO;
using Newtonsoft.Json;
using System;
using Elements.Spatial;
using System.Diagnostics;
using LayoutFunctionCommon;

namespace PantryLayout
{
    public static class PantryLayout
    {
        private class PantryLayoutGeneration : LayoutGeneration<LevelElements, LevelVolume, SpaceBoundary, CirculationSegment>
        {
            private static readonly string[] countableSeats = new[]
            {
                "Steelcase - Seating - Nooi - Cafeteria Chair - Chair",
                "Steelcase - Seating - Nooi - Stool - Bar Height",
                "Steelcase Turnstone - Shortcut X Base - Chair - Chair",
                "Steelcase Turnstone - Shortcut X Base - Stool - Chair"
            };

            protected override SeatsCount CountSeats(LayoutInstantiated layout)
            {
                int countableSeatCount = 0;
                foreach (var item in layout.Config.ContentItems)
                {
                    foreach (var countableSeat in countableSeats)
                    {
                        if (item.ContentElement.Name.Contains(countableSeat))
                        {
                            countableSeatCount++;
                        }
                    }
                }

                return new SeatsCount(countableSeatCount, 0, 0, 0);
            }
        }

        /// <summary>
        /// The PantryLayout function.
        /// </summary>
        /// <param name="model">The input model.</param>
        /// <param name="input">The arguments to the execution.</param>
        /// <returns>A PantryLayoutOutputs instance containing computed results and the model with any new elements.</returns>
        public static PantryLayoutOutputs Execute(Dictionary<string, Model> inputModels, PantryLayoutInputs input)
        {
            Elements.Serialization.glTF.GltfExtensions.UseReferencedContentExtension = true;

            var layoutGeneration = new PantryLayoutGeneration();

            string configJsonPath = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "./PantryConfigurations.json");
            SpaceConfiguration configs = ContentManagement.GetSpaceConfiguration<ProgramRequirement>(inputModels, configJsonPath, "Pantry");

            var result = layoutGeneration.StandardLayoutOnAllLevels("Pantry", inputModels, input.Overrides, false, configs);
            var output = new PantryLayoutOutputs
            {
                Model = result.OutputModel,
                TotalCafeChairsCount = result.SeatsCount
            };
            return output;
        }
    }
}