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

namespace OpenCollaborationLayout
{
    public static class OpenCollaborationLayout
    {
        private class OpenCollaborationLayoutGeneration : LayoutGeneration<LevelElements, LevelVolume, SpaceBoundary, CirculationSegment>
        {
            private int varietyCounter = 0;

            protected override int CountSeats(LayoutInstantiated layout)
            {
                if (layout.Config is ConfigurationWithCounts configWithCounts)
                {
                    return configWithCounts.SeatCount;
                }

                return 0;
            }

            protected override SpaceConfiguration DeserializeConfigJson(string configJson)
            {
                var spaceConfiguration = new SpaceConfiguration();
                var dictWithCounts = JsonConvert.DeserializeObject<Dictionary<string, ConfigurationWithCounts>>(configJson);
                foreach (var pair in dictWithCounts)
                {
                    spaceConfiguration.Add(pair.Key, pair.Value);
                }
                return spaceConfiguration;
            }

            public override LayoutGenerationResult StandardLayoutOnAllLevels(string programTypeName, Dictionary<string, Model> inputModels, dynamic overrides, bool createWalls, string configurationsPath, string catalogPath = "catalog.json")
            {
                varietyCounter = 0;
                var result = base.StandardLayoutOnAllLevels(programTypeName, inputModels, (object)overrides, createWalls, configurationsPath, catalogPath);
                result.OutputModel.AddElement(new WorkpointCount() { Count = result.SeatsCount, Type = "Collaboration seat" });
                return result;
            }

            protected override KeyValuePair<string, ContentConfiguration>? FindConfig(double width, double length, SpaceConfiguration configs)
            {
                var orderedConfigPairs = configs.OrderByDescending(kvp => kvp.Value.CellBoundary.Depth * kvp.Value.CellBoundary.Width);
                var configsThatFitWell = new List<KeyValuePair<string, ContentConfiguration>>();
                foreach (var configPair in orderedConfigPairs)
                {
                    var config = configPair.Value;
                    // if it fits
                    if (config.CellBoundary.Width < width && config.CellBoundary.Depth < length)
                    {
                        if (configsThatFitWell.Count == 0)
                        {
                            configsThatFitWell.Add(configPair);
                        }
                        else
                        {
                            var firstFittingConfig = configsThatFitWell.First().Value;
                            // check if there's another config that's roughly the same size
                            if (config.CellBoundary.Width.ApproximatelyEquals(firstFittingConfig.CellBoundary.Width, 1.0) && config.CellBoundary.Depth.ApproximatelyEquals(firstFittingConfig.CellBoundary.Depth, 1.0))
                            {
                                configsThatFitWell.Add(configPair);
                            }
                        }
                    }
                }
                if (configsThatFitWell.Count == 0)
                {
                    return null;
                }
                var selectedConfig = configsThatFitWell[varietyCounter % configsThatFitWell.Count];
                varietyCounter++;
                return selectedConfig;
            }

            protected override IEnumerable<LevelElements> GetLevels(Dictionary<string, Model> inputModels, Model spacePlanningZones)
            {
                var levels = base.GetLevels(inputModels, spacePlanningZones);
                if (inputModels.TryGetValue("Open Office Layout", out var openOfficeModel))
                {
                    foreach (var sb in openOfficeModel.AllElementsOfType<SpaceBoundary>())
                    {
                        if (sb.AdditionalProperties.TryGetValue("Parent Level Id", out var lvlId))
                        {
                            var matchingLevel = levels.FirstOrDefault(l => l.Id.ToString() == lvlId as string);
                            matchingLevel?.Elements.Add(sb);
                        }
                    }
                }
                return levels;
            }
        }

        /// <summary>
        /// The OpenCollaborationLayout function.
        /// </summary>
        /// <param name="model">The input model.</param>
        /// <param name="input">The arguments to the execution.</param>
        /// <returns>A OpenCollaborationLayoutOutputs instance containing computed results and the model with any new elements.</returns>
        public static OpenCollaborationLayoutOutputs Execute(Dictionary<string, Model> inputModels, OpenCollaborationLayoutInputs input)
        {
            Elements.Serialization.glTF.GltfExtensions.UseReferencedContentExtension = true;
            var layoutGeneration = new OpenCollaborationLayoutGeneration();
            var result = layoutGeneration.StandardLayoutOnAllLevels("Open Collaboration", inputModels, input.Overrides, false, "./OpenCollaborationConfigurations.json");
            var output = new OpenCollaborationLayoutOutputs
            {
                Model = result.OutputModel,
                CollaborationSeats = result.SeatsCount
            };
            return output;
        }
    }
}