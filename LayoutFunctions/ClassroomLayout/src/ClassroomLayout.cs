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

namespace ClassroomLayout
{
    public static class ClassroomLayout
    {
        /// <summary>
        /// The ClassroomLayout function.
        /// </summary>
        /// <param name="model">The input model.</param>
        /// <param name="input">The arguments to the execution.</param>
        /// <returns>A ClassroomLayoutOutputs instance containing computed results and the model with any new elements.</returns>
        public static ClassroomLayoutOutputs Execute(Dictionary<string, Model> inputModels, ClassroomLayoutInputs input)
        {
            Elements.Serialization.glTF.GltfExtensions.UseReferencedContentExtension = true;
            var spacePlanningZones = inputModels["Space Planning Zones"];

            var levels = spacePlanningZones.AllElementsOfType<LevelElements>();
            if (inputModels.TryGetValue("Circulation", out var circModel))
            {
                var circSegments = circModel.AllElementsOfType<CirculationSegment>();
                foreach (var cs in circSegments)
                {
                    var matchingLevel = levels.FirstOrDefault(l => l.Level == cs.Level);
                    if (matchingLevel != null)
                    {
                        matchingLevel.Elements.Add(cs);
                    }
                }
            }
            var levelVolumes = LayoutStrategies.GetLevelVolumes<LevelVolume>(inputModels);
            if (inputModels.TryGetValue("Conceptual Mass", out var massModel))
            {
                levelVolumes = massModel.AllElementsOfType<LevelVolume>().ToList();
            }
            var output = new ClassroomLayoutOutputs();
            var configJson = File.ReadAllText("./ClassroomConfigurations.json");
            var configs = JsonConvert.DeserializeObject<SpaceConfiguration>(configJson);

            int totalCountableSeats = 0;
            int seatsAtDesk = 0;
            var deskConfig = configs["Desk"];
            string[] countableSeats = new[] { "Steelcase Turnstone - Shortcut X Base - Chair - Chair",
                                              "Steelcase Turnstone - Shortcut - Stool - Chair" };
            foreach (var item in deskConfig.ContentItems)
            {
                foreach (var countableSeat in countableSeats)
                {
                    if (item.ContentElement.Name.Contains(countableSeat))
                    {
                        seatsAtDesk++;
                    }
                }
            }

            foreach (var lvl in levels)
            {
                var corridors = lvl.Elements.OfType<CirculationSegment>();
                var corridorSegments = corridors.SelectMany(p => p.Profile.Segments());
                var meetingRmBoundaries = lvl.Elements.OfType<SpaceBoundary>().Where(z => z.Name == "Classroom");
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
                            var componentInstance = InstantiateLayout(configs, width, depth, rect, room.Transform);
                            LayoutStrategies.SetLevelVolume(componentInstance, levelVolume?.Id);
                            output.Model.AddElement(componentInstance);
                        }
                        else if (trimmedGeo.Count() > 0)
                        {
                            var largestTrimmedShape = trimmedGeo.OfType<Polygon>().OrderBy(s => s.Area()).Last();
                            var cinchedVertices = rect.Vertices.Select(v => largestTrimmedShape.Vertices.OrderBy(v2 => v2.DistanceTo(v)).First()).ToList();
                            var cinchedPoly = new Polygon(cinchedVertices);

                            var componentInstance = InstantiateLayout(configs, width, depth, cinchedPoly, room.Transform);
                            LayoutStrategies.SetLevelVolume(componentInstance, levelVolume?.Id);
                            output.Model.AddElement(componentInstance);
                        }
                        try
                        {
                            if (cell.U.Curve.Length() > 5.6)
                            {
                                cell.U.SplitAtOffset(2.3, false, true);
                                cell.V.SplitAtOffset(0.5, false, true);
                                cell.V.SplitAtOffset(0.5, true, true);
                                var classroomSide = cell[1, 1];
                                classroomSide.U.DivideByFixedLength(deskConfig.Width, FixedDivisionMode.RemainderAtBothEnds);
                                classroomSide.V.DivideByFixedLength(deskConfig.Depth, FixedDivisionMode.RemainderAtBothEnds);
                                foreach (var individualDesk in classroomSide.GetCells())
                                {
                                    var cellRect = individualDesk.GetCellGeometry() as Polygon;
                                    var trimmedShape = individualDesk.GetTrimmedCellGeometry().FirstOrDefault() as Polygon;
                                    if (trimmedShape == null)
                                    {
                                        continue;
                                    }
                                    if (trimmedShape.Area().ApproximatelyEquals(deskConfig.Width * deskConfig.Depth, 0.1))
                                    {
                                        totalCountableSeats += seatsAtDesk;
                                        foreach (var contentItem in deskConfig.ContentItems)
                                        {
                                            var instance = contentItem.ContentElement.CreateInstance(
                                                contentItem.Transform
                                                .Concatenated(orientationTransform)
                                                .Concatenated(new Transform(cellRect.Vertices[0]))
                                                .Concatenated(room.Transform),
                                                "Desk");
                                                
                                            LayoutStrategies.SetLevelVolume(instance, levelVolume?.Id);
                                            output.Model.AddElement(instance);
                                        }
                                    }
                                }
                            }
                        }
                        catch
                        {
                        }
                    }
                }
                var height = meetingRmBoundaries.FirstOrDefault()?.Height ?? 3;
                if (input.CreateWalls)
                {
                    output.Model.AddElement(new InteriorPartitionCandidate(Guid.NewGuid())
                    {
                        WallCandidateLines = wallCandidateLines,
                        Height = height,
                        LevelTransform = levelVolume?.Transform ?? new Transform()
                    });
                }
            }
            output.Model.AddElement(new WorkpointCount() { Count = totalCountableSeats, Type = "Classroom Seat" });
            output.TotalCountOfDeskSeats = totalCountableSeats;
            OverrideUtilities.InstancePositionOverrides(input.Overrides, output.Model);
            return output;
        }

        private static ComponentInstance InstantiateLayout(SpaceConfiguration configs, double width, double length, Polygon rectangle, Transform xform)
        {
            ContentConfiguration selectedConfig = null;
            var orderedKeys = new[] { "Classroom-A", "Classroom-B", "Classroom-C" };
            foreach (var key in orderedKeys)
            {
                var config = configs[key];
                if (config.CellBoundary.Width < width && config.CellBoundary.Depth < length)
                {
                    selectedConfig = config;
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
            return componentDefinition.Instantiate(ContentConfiguration.AnchorsFromRect(rectangle.TransformedPolygon(xform)));
        }
    }

}