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
        private class MeetingLayoutGeneration : LayoutGeneration<LevelElements, LevelVolume, SpaceBoundary, CirculationSegment>
        {
            private Dictionary<string, RoomTally> seatsTable = new();

            protected override int CountSeats(LayoutInstantiated layout)
            {
                int seatsCount = 0;
                if (layout != null)
                {
                    configCapacity.TryGetValue(layout.ConfigName, out seatsCount);
                    if (seatsTable.ContainsKey(layout.ConfigName))
                    {
                        seatsTable[layout.ConfigName].SeatsCount += seatsCount;
                    }
                    else
                    {
                        seatsTable[layout.ConfigName] = new RoomTally(layout.ConfigName, seatsCount);
                    }
                }
                return seatsCount;
            }

            public override LayoutGenerationResult StandardLayoutOnAllLevels(string programTypeName, Dictionary<string, Model> inputModels, dynamic overrides, bool createWalls, string configurationsPath, string catalogPath = "catalog.json")
            {
                seatsTable = new Dictionary<string, RoomTally>();
                var result = base.StandardLayoutOnAllLevels(programTypeName, inputModels, (object)overrides, createWalls, configurationsPath, catalogPath);
                result.OutputModel.AddElements(seatsTable.Select(kvp => kvp.Value).OrderByDescending(a => a.SeatsCount));
                return result;
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
            var result = layoutGeneration.StandardLayoutOnAllLevels("Meeting Room", inputModels, input.Overrides, false, "./ConferenceRoomConfigurations.json");
            var output = new MeetingRoomLayoutOutputs
            {
                Model = result.OutputModel,
                TotalSeatCount = result.SeatsCount
            };
            return output;
        }

        private static readonly Dictionary<string, int> configCapacity = new()
        {
            {"22P", 22},
            {"20P", 20},
            {"14P", 14},
            {"13P", 13},
            {"8P", 8},
            {"6P-A", 6},
            {"6P-B", 6},
            {"4P-A", 4},
            {"4P-B", 4}
        };
    }

}