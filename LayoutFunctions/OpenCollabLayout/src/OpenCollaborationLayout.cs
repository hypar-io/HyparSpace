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
            private Dictionary<string, (ContentConfiguration config, int usedCount)> configsWithUsedCount = new();

            protected override SeatsCount CountSeats(LayoutInstantiated layout)
            {
                if (layout.Config is ConfigurationWithCounts configWithCounts)
                {
                    return new SeatsCount(configWithCounts.SeatCount, 0, 0, configWithCounts.SeatCount);
                }

                return default;
            }

            protected override (ConfigInfo? configInfo, List<(Line Line, string Type)> wallCandidates) SelectTheBestOfPossibleConfigs(List<(ConfigInfo configInfo, List<(Line Line, string Type)> wallCandidates)> possibleConfigs)
            {
                var configsThatFitWell = new List<(ConfigInfo configInfo, int usedCount, List<(Line Line, string Type)> wallCandidates)>();
                var orderedConfigPairs = configsWithUsedCount.OrderByDescending(kvp => kvp.Value.config.CellBoundary.Depth * kvp.Value.config.CellBoundary.Width);
                possibleConfigs = possibleConfigs.DistinctBy(pc => pc.configInfo.ConfigName).ToList();
                foreach (var (configInfo, wallCandidates) in possibleConfigs)
                {
                    var width = configInfo.Config.Width;
                    var length = configInfo.Config.Depth;
                    foreach (var configPair in orderedConfigPairs)
                    {
                        var configName = configPair.Key;
                        var config = configPair.Value.config;
                        // if it fits
                        if (config.CellBoundary.Width <= width && config.CellBoundary.Depth <= length)
                        {
                            if (configsThatFitWell.Count == 0)
                            {
                                configsThatFitWell.Add((new ConfigInfo(configName, config, configInfo.Rectangle), configPair.Value.usedCount, wallCandidates));
                            }
                            else
                            {
                                // check if there's another config that's roughly the same size
                                if (config.CellBoundary.Width.ApproximatelyEquals(configInfo.Config.CellBoundary.Width, 1.0)
                                    && config.CellBoundary.Depth.ApproximatelyEquals(configInfo.Config.CellBoundary.Depth, 1.0))
                                {
                                    configsThatFitWell.Add((new ConfigInfo(configName, config, configInfo.Rectangle), configPair.Value.usedCount, wallCandidates));
                                }

                            }
                        }
                    }
                }
                // shouldn't happen
                if (configsThatFitWell.Count == 0)
                {
                    return possibleConfigs.First();
                }

                var selectedConfig = configsThatFitWell
                    .OrderBy(c => c.usedCount)
                    .ThenByDescending(kvp => kvp.configInfo.Config.CellBoundary.Depth * kvp.configInfo.Config.CellBoundary.Width)
                    .First();
                configsWithUsedCount[selectedConfig.configInfo.ConfigName] = (selectedConfig.configInfo.Config, selectedConfig.usedCount + 1);
                return (selectedConfig.configInfo, selectedConfig.wallCandidates);
            }

            protected override SpaceConfiguration DeserializeConfigJson(string configJson)
            {
                var spaceConfiguration = new SpaceConfiguration();
                var dictWithCounts = JsonConvert.DeserializeObject<Dictionary<string, ConfigurationWithCounts>>(configJson);
                foreach (var pair in dictWithCounts)
                {
                    spaceConfiguration.Add(pair.Key, pair.Value);
                }

                configsWithUsedCount = spaceConfiguration.ToDictionary(kvp => kvp.Key, kvp => (kvp.Value, 0));
                return spaceConfiguration;
            }

            public override LayoutGenerationResult StandardLayoutOnAllLevels(string programTypeName, Dictionary<string, Model> inputModels, dynamic overrides, bool createWalls, string configurationsPath, string catalogPath = "catalog.json")
            {
                var result = base.StandardLayoutOnAllLevels(programTypeName, inputModels, (object)overrides, createWalls, configurationsPath, catalogPath);
                return result;
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