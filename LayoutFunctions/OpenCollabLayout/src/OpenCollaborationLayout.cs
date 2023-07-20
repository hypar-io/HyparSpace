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
        /// <summary>
        /// The OpenCollaborationLayout function.
        /// </summary>
        /// <param name="model">The input model.</param>
        /// <param name="input">The arguments to the execution.</param>
        /// <returns>A OpenCollaborationLayoutOutputs instance containing computed results and the model with any new elements.</returns>
        public static OpenCollaborationLayoutOutputs Execute(Dictionary<string, Model> inputModels, OpenCollaborationLayoutInputs input)
        {
            Elements.Serialization.glTF.GltfExtensions.UseReferencedContentExtension = true;
            varietyCounter = 0;
            int totalCountableSeats = 0;
            var spacePlanningZones = inputModels["Space Planning Zones"];

            var hasOpenOffice = inputModels.TryGetValue("Open Office Layout", out var openOfficeModel);

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
            var output = new OpenCollaborationLayoutOutputs();
            var configJson = File.ReadAllText("./OpenCollaborationConfigurations.json");
            var configs = JsonConvert.DeserializeObject<Dictionary<string, ConfigurationWithCounts>>(configJson);

            if (hasOpenOffice)
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

            foreach (var lvl in levels)
            {
                var corridors = lvl.Elements.OfType<CirculationSegment>();
                var corridorSegments = corridors.SelectMany(p => p.Profile.Segments());
                var meetingRmBoundaries = lvl.Elements.OfType<SpaceBoundary>().Where(z => z.Name == "Open Collaboration");
                var levelVolume = levelVolumes.FirstOrDefault(l =>
                    (lvl.AdditionalProperties.TryGetValue("LevelVolumeId", out var levelVolumeId) &&
                        levelVolumeId as string == l.Id.ToString())) ??
                        levelVolumes.FirstOrDefault(l => l.Name == lvl.Name);

                foreach (var room in meetingRmBoundaries)
                {
                    var seatsCount = 0;
                    var spaceBoundary = room.Boundary;
                    Line orientationGuideEdge = FindEdgeAdjacentToSegments(spaceBoundary.Perimeter.Segments(), corridorSegments, out var wallCandidates);
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
                        if (!cell.IsTrimmed() && trimmedGeo.Length > 0)
                        {
                            var (instance, count) = InstantiateLayout(configs, width, depth, rect, room.Transform);
                            LayoutStrategies.SetLevelVolume(instance, levelVolume?.Id);
                            output.Model.AddElement(instance);
                            seatsCount += count;
                        }
                        else if (trimmedGeo.Length > 0)
                        {
                            var largestTrimmedShape = trimmedGeo.OfType<Polygon>().OrderBy(s => s.Area()).Last();
                            var cinchedVertices = rect.Vertices.Select(v => largestTrimmedShape.Vertices.OrderBy(v2 => v2.DistanceTo(v)).First()).ToList();
                            var cinchedPoly = new Polygon(cinchedVertices);
                            // output.Model.AddElement(new ModelCurve(cinchedPoly, BuiltInMaterials.ZAxis, levelVolume.Transform));
                            try
                            {
                                var (instance, count) = InstantiateLayout(configs, width, depth, cinchedPoly, room.Transform);
                                LayoutStrategies.SetLevelVolume(instance, levelVolume?.Id);
                                output.Model.AddElement(instance);
                                seatsCount += count;
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine("Failed to instantiate config\n" + e.ToString());
                            }
                            Console.WriteLine("🤷‍♂️ funny shape!!!");
                        }
                    }
                    
                    totalCountableSeats += seatsCount;
                    output.Model.AddElement(new SpaceMetric(room.Id, seatsCount, 0, 0, seatsCount));
                }
            }
            output.CollaborationSeats = totalCountableSeats;
            OverrideUtilities.InstancePositionOverrides(input.Overrides, output.Model);
            return output;
        }

        private static Line FindEdgeAdjacentToSegments(IEnumerable<Line> edgesToClassify, IEnumerable<Line> corridorSegments, out IEnumerable<Line> otherSegments, double maxDist = 0)
        {
            var minDist = double.MaxValue;
            var minSeg = edgesToClassify.First();
            var allEdges = edgesToClassify.ToList();
            var selectedIndex = 0;
            for (int i = 0; i < allEdges.Count; i++)
            {
                var edge = allEdges[i];
                var midpt = edge.Mid();
                foreach (var seg in corridorSegments)
                {
                    var dist = midpt.DistanceTo(seg);
                    // if two segments are basically the same distance to the corridor segment,
                    // prefer the longer one.
                    if (Math.Abs(dist - minDist) < 0.1)
                    {
                        minDist = dist;
                        if (minSeg.Length() < edge.Length())
                        {
                            minSeg = edge;
                            selectedIndex = i;
                        }
                    }
                    else if (dist < minDist)
                    {
                        minDist = dist;
                        minSeg = edge;
                        selectedIndex = i;
                    }
                }
            }
            if (maxDist != 0)
            {
                if (minDist < maxDist)
                {

                    otherSegments = Enumerable.Range(0, allEdges.Count).Except(new[] { selectedIndex }).Select(i => allEdges[i]);
                    return minSeg;
                }
                else
                {
                    Console.WriteLine($"no matches: {minDist}");
                    otherSegments = allEdges;
                    return null;
                }
            }
            otherSegments = Enumerable.Range(0, allEdges.Count).Except(new[] { selectedIndex }).Select(i => allEdges[i]);
            return minSeg;
        }

        private static int varietyCounter = 0;
        private static (ComponentInstance instance, int count) InstantiateLayout(Dictionary<string, ConfigurationWithCounts> configs, double width, double length, Polygon rectangle, Transform xform)
        {
            var orderedKeys = configs.OrderByDescending(kvp => kvp.Value.CellBoundary.Depth * kvp.Value.CellBoundary.Width).Select(kvp => kvp.Key);
            var configsThatFitWell = new List<ConfigurationWithCounts>();
            int countableSeatCount = 0;
            foreach (var key in orderedKeys)
            {
                var config = configs[key];
                // if it fits
                if (config.CellBoundary.Width < width && config.CellBoundary.Depth < length)
                {
                    if (configsThatFitWell.Count == 0)
                    {
                        configsThatFitWell.Add(config);
                    }
                    else
                    {
                        var firstFittingConfig = configsThatFitWell.First();
                        // check if there's another config that's roughly the same size
                        if (config.CellBoundary.Width.ApproximatelyEquals(firstFittingConfig.CellBoundary.Width, 1.0) && config.CellBoundary.Depth.ApproximatelyEquals(firstFittingConfig.CellBoundary.Depth, 1.0))
                        {
                            configsThatFitWell.Add(config);
                        }
                    }
                }
            }
            if (configsThatFitWell.Count == 0)
            {
                return (null, 0);
            }
            var selectedConfig = configsThatFitWell[varietyCounter % configsThatFitWell.Count];

            countableSeatCount = selectedConfig.SeatCount;

            var baseRectangle = Polygon.Rectangle(selectedConfig.CellBoundary.Min, selectedConfig.CellBoundary.Max);

            var rules = selectedConfig.Rules();
            varietyCounter++;
            var componentDefinition = new ComponentDefinition(rules, selectedConfig.Anchors());
            var instance = componentDefinition.Instantiate(ContentConfiguration.AnchorsFromRect(rectangle.TransformedPolygon(xform)));
            var allPlacedInstances = instance.Instances;
            return (instance, countableSeatCount);
        }

        private static int CountConfigSeats(ContentConfiguration config, string[] countableSeaters, int seatsPerSeater)
        {
            int countableSeatCount = 0;
            foreach (var item in config.ContentItems)
            {
                foreach (var countableSeat in countableSeaters)
                {
                    if (item.ContentElement.GltfLocation.Contains(countableSeat))
                    {
                        countableSeatCount += seatsPerSeater;
                        break;
                    }
                }
            }
            return countableSeatCount;
        }
    }
}