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

namespace MeetingRoomLayout
{

    public static class MeetingRoomLayout
    {
        private class MeetingLayoutGeneration : LayoutGeneration<LevelElements, LevelVolume, SpaceBoundary, CirculationSegment, SpaceSettingsOverride, SpaceSettingsValue>
        {
            private Dictionary<string, RoomTally> seatsTable = new();

            protected override SeatsCount CountSeats(LayoutInstantiated layout)
            {
                int seatsCount = 0;
                if (layout != null)
                {
                    if (configInfos.TryGetValue(layout.ConfigName, out var configInfo))
                    {
                        seatsCount = configInfo.capacity;
                    }

                    if (seatsTable.ContainsKey(layout.ConfigName))
                    {
                        seatsTable[layout.ConfigName].SeatsCount += seatsCount;
                    }
                    else
                    {
                        seatsTable[layout.ConfigName] = new RoomTally(layout.ConfigName, seatsCount);
                    }
                }
                return new SeatsCount(seatsCount, 0, 0, 0);
            }

            public override LayoutGenerationResult StandardLayoutOnAllLevels(string programTypeName, Dictionary<string, Model> inputModels, dynamic overrides, Func<SpaceSettingsOverride, Vector3> getCentroid, SpaceSettingsValue defaultValue, bool createWalls, string configurationsPath, string catalogPath = "catalog.json")
            {
                seatsTable = new Dictionary<string, RoomTally>();
                var result = base.StandardLayoutOnAllLevels(programTypeName, inputModels, (object)overrides, getCentroid, defaultValue, createWalls, configurationsPath, catalogPath);
                result.OutputModel.AddElements(seatsTable.Select(kvp => kvp.Value).OrderByDescending(a => a.SeatsCount));
                return result;
            }

            protected override IEnumerable<KeyValuePair<string, ContentConfiguration>> OrderConfigs(Dictionary<string, ContentConfiguration> configs)
            {
                return configs.OrderBy(i =>
                {
                    if (!configInfos.ContainsKey(i.Key))
                    {
                        return int.MaxValue;
                    }
                    return configInfos[i.Key].orderIndex;
                });
            }
        }

        /// <summary>
        /// The MeetingRoomLayout function.
        /// </summary>
        /// <param name="model">The input model.</param>
        /// <param name="input">The arguments to the execution.</param>
        /// <returns>A MeetingRoomLayoutOutputs instance containing computed results and the model with any new elements.</returns>
        public static MeetingRoomLayoutOutputs Execute(Dictionary<string, Model> inputModels, MeetingRoomLayoutInputs input)
        {
            Elements.Serialization.glTF.GltfExtensions.UseReferencedContentExtension = true;
            var layoutGeneration = new MeetingLayoutGeneration();
            var result = layoutGeneration.StandardLayoutOnAllLevels("Meeting Room", inputModels, input.Overrides, input.CreateWalls, (ov) => ov.Identity.ParentCentroid, new SpaceSettingsValue(false, false), false, "./ConferenceRoomConfigurations.json");
            var result = layoutGeneration.StandardLayoutOnAllLevels("Meeting Room", inputModels, input.Overrides, input.CreateWalls, "./ConferenceRoomConfigurations.json");
            var output = new MeetingRoomLayoutOutputs
            {
                Model = result.OutputModel,
                TotalSeatCount = result.SeatsCount
            };
            return output;
        }

        private static readonly Dictionary<string, (int capacity, int orderIndex)> configInfos = new()
        {
            {"22P", (22, 1)},
            {"20P", (20, 2)},
            {"14P", (14, 3)},
            {"13P", (13, 4)},
            {"8P", (8, 5)},
            {"6P-A", (6, 6)},
            {"6P-B", (6, 7)},
            {"4P-A", (4, 8)},
            {"4P-B", (4, 9)}
        };
    }

}