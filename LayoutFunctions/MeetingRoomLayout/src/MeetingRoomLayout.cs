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
        private class LayoutInstantiated
        {
            public ComponentInstance Instance { get; set; }
            public RoomTally Tally { get; set; }
        };

        /// <summary>
        /// The MeetingRoomLayout function.
        /// </summary>
        /// <param name="model">The input model.</param>
        /// <param name="input">The arguments to the execution.</param>
        /// <returns>A MeetingRoomLayoutOutputs instance containing computed results and the model with any new elements.</returns>
        public static MeetingRoomLayoutOutputs Execute(Dictionary<string, Model> inputModels, MeetingRoomLayoutInputs input)
        {
            var spacePlanningZones = inputModels["Space Planning Zones"];
            inputModels.TryGetValue("Levels", out var levelsModel);
            var levels = spacePlanningZones.AllElementsOfType<LevelElements>();
            var levelVolumes = levelsModel?.AllElementsOfType<LevelVolume>() ?? new List<LevelVolume>();
            var configJson = File.ReadAllText("./ConferenceRoomConfigurations.json");
            var configs = JsonConvert.DeserializeObject<SpaceConfiguration>(configJson);

            var outputModel = new Model();
            int totalSeats = 0;
            var seatsTable = new Dictionary<string, RoomTally>();
            foreach (var lvl in levels)
            {
                var corridors = lvl.Elements.OfType<Floor>();
                var corridorSegments = corridors.SelectMany(p => p.Profile.Segments());
                var meetingRmBoundaries = lvl.Elements.OfType<SpaceBoundary>().Where(z => z.Name == "Meeting Room");
                var levelVolume = levelVolumes.FirstOrDefault(l =>
                    (lvl.AdditionalProperties.TryGetValue("LevelVolumeId", out var levelVolumeId) &&
                        levelVolumeId as string == l.Id.ToString())) ??
                        levelVolumes.FirstOrDefault(l => l.Name == lvl.Name);

                var wallCandidateLines = new List<(Line line, string type)>();
                foreach (var room in meetingRmBoundaries)
                {
                    var spaceBoundary = room.Boundary;
                    var levelInvertedTransform = levelVolume.Transform.Inverted();
                    var roomWallCandidatesLines = WallGeneration.FindWallCandidates(room, levelVolume?.Profile, corridorSegments, out Line orientationGuideEdge)
                        .Select(c => (c.line.TransformedLine(levelInvertedTransform), c.type));
                    wallCandidateLines.AddRange(roomWallCandidatesLines);

                    var orientationTransform = new Transform(Vector3.Origin, orientationGuideEdge.Direction(), Vector3.ZAxis);
                    var boundaryCurves = new List<Polygon>();
                    boundaryCurves.Add(spaceBoundary.Perimeter);
                    boundaryCurves.AddRange(spaceBoundary.Voids ?? new List<Polygon>());

                    var grid = new Grid2d(boundaryCurves, orientationTransform);
                    foreach (var cell in grid.GetCells())
                    {
                        var rect = cell.GetCellGeometry() as Polygon;
                        var segs = rect.Segments();
                        var width = segs[0].Length();
                        var depth = segs[1].Length();
                        var trimmedGeo = cell.GetTrimmedCellGeometry();
                        if (!cell.IsTrimmed() && trimmedGeo.Count() > 0)
                        {
                            var layout = InstantiateLayout(configs, width, depth, rect, room.Transform);
                            totalSeats += AddInstantiatedLayout(layout, outputModel, seatsTable);
                        }
                        else if (trimmedGeo.Count() > 0)
                        {
                            var largestTrimmedShape = trimmedGeo.OfType<Polygon>().OrderBy(s => s.Area()).Last();
                            var cinchedVertices = rect.Vertices.Select(v => largestTrimmedShape.Vertices.OrderBy(v2 => v2.DistanceTo(v)).First()).ToList();
                            var cinchedPoly = new Polygon(cinchedVertices);
                            var layout = InstantiateLayout(configs, width, depth, cinchedPoly, room.Transform);
                            totalSeats += AddInstantiatedLayout(layout, outputModel, seatsTable);
                        }
                    }

                }

                var height = meetingRmBoundaries.FirstOrDefault()?.Height ?? 3;
                if (input.CreateWalls)
                {
                    outputModel.AddElement(new InteriorPartitionCandidate(Guid.NewGuid())
                    {
                        WallCandidateLines = wallCandidateLines,
                        Height = height,
                        LevelTransform = levelVolume?.Transform ?? new Transform()
                    });
                }
            }
            OverrideUtilities.InstancePositionOverrides(input.Overrides, outputModel);

            outputModel.AddElement(new WorkpointCount(totalSeats, "Meeting Room Seat"));
            outputModel.AddElements(seatsTable.Select(kvp => kvp.Value).OrderByDescending(a => a.SeatsCount));

            var output = new MeetingRoomLayoutOutputs(totalSeats);
            output.Model = outputModel;
            return output;
        }

        private static int AddInstantiatedLayout(LayoutInstantiated layout, Model model, Dictionary<string, RoomTally> seatsTable)
        {
            if (layout == null)
            {
                return 0;
            }

            model.AddElement(layout.Instance);
            int seatsCount = 0;
            if (layout.Tally != null)
            {
                if (seatsTable.ContainsKey(layout.Tally.RoomType))
                {
                    seatsTable[layout.Tally.RoomType].SeatsCount += layout.Tally.SeatsCount;
                }
                else
                {
                    seatsTable[layout.Tally.RoomType] = new RoomTally(layout.Tally.RoomType, layout.Tally.SeatsCount);
                }
                seatsCount = layout.Tally.SeatsCount;
            }

            return seatsCount;
        }

        private static LayoutInstantiated InstantiateLayout(SpaceConfiguration configs, double width, double length, Polygon rectangle, Transform xform)
        {
            ContentConfiguration selectedConfig = null;
            var result = new LayoutInstantiated();
            for (int i = 0; i < orderedKeys.Length; ++i)
            {
                var config = configs[orderedKeys[i]];
                if (config.CellBoundary.Width < width && config.CellBoundary.Depth < length)
                {
                    selectedConfig = config;
                    result.Tally = new RoomTally(orderedKeys[i], orderedKeysCapacity[i]);
                    break;
                }
            }
            if (selectedConfig == null)
            {
                return null;
            }
            var baseRectangle = Polygon.Rectangle(selectedConfig.CellBoundary.Min, selectedConfig.CellBoundary.Max);
            var rules = selectedConfig.Rules();

            var componentDefinition = new ComponentDefinition(rules, selectedConfig.Anchors());
            result.Instance = componentDefinition.Instantiate(ContentConfiguration.AnchorsFromRect(rectangle.TransformedPolygon(xform)));
            return result;
        }

        private static string[] orderedKeys = new[] { "22P", "20P", "14P", "13P", "8P", "6P-A", "6P-B", "4P-A", "4P-B" };
        private static int[] orderedKeysCapacity = new[] { 22, 20, 14, 13, 8, 6, 6, 4, 4 };
    }

}