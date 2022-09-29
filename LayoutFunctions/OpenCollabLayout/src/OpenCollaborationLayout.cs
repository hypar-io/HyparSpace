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
            varietyCounter = 0;
            int totalCountableSeats = 0;
            var spacePlanningZones = inputModels["Space Planning Zones"];
            inputModels.TryGetValue("Levels", out var levelsModel);
            var hasOpenOffice = inputModels.TryGetValue("Open Office Layout", out var openOfficeModel);

            var levels = spacePlanningZones.AllElementsOfType<LevelElements>();
            var levelVolumes = levelsModel?.AllElementsOfType<LevelVolume>() ?? new List<LevelVolume>();
            var output = new OpenCollaborationLayoutOutputs();
            var configJson = File.ReadAllText("./OpenCollaborationConfigurations.json");
            var configs = JsonConvert.DeserializeObject<SpaceConfiguration>(configJson);

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
                var corridors = lvl.Elements.OfType<Floor>();
                var corridorSegments = corridors.SelectMany(p => p.Profile.Segments());
                var meetingRmBoundaries = lvl.Elements.OfType<SpaceBoundary>().Where(z => z.Name == "Open Collaboration");
                // var levelVolume = levelVolumes.FirstOrDefault(l =>
                // (lvl.AdditionalProperties.TryGetValue("LevelVolumeId", out var levelVolumeId) &&
                //     levelVolumeId as string == l.Id.ToString()) ||
                // l.Name == lvl.Name);

                foreach (var room in meetingRmBoundaries)
                {
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
                        if (!cell.IsTrimmed() && trimmedGeo.Count() > 0)
                        {
                            var layout = InstantiateLayout(configs, width, depth, rect, room.Transform);
                            output.Model.AddElement(layout.instance);
                            totalCountableSeats += layout.count;
                        }
                        else if (trimmedGeo.Count() > 0)
                        {
                            var largestTrimmedShape = trimmedGeo.OfType<Polygon>().OrderBy(s => s.Area()).Last();
                            var cinchedVertices = rect.Vertices.Select(v => largestTrimmedShape.Vertices.OrderBy(v2 => v2.DistanceTo(v)).First()).ToList();
                            var cinchedPoly = new Polygon(cinchedVertices);
                            // output.Model.AddElement(new ModelCurve(cinchedPoly, BuiltInMaterials.ZAxis, levelVolume.Transform));
                            try
                            {
                                var layout = InstantiateLayout(configs, width, depth, cinchedPoly, room.Transform);
                                output.Model.AddElement(layout.instance);
                                totalCountableSeats += layout.count;
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine("Failed to instantiate config\n" + e.ToString());
                            }
                            Console.WriteLine("🤷‍♂️ funny shape!!!");
                        }
                    }
                }
            }
            output.Model.AddElement(new WorkpointCount() { Count = totalCountableSeats, Type = "Collaboration seat" });
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
                var midpt = edge.PointAt(0.5);
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
        private static (ComponentInstance instance, int count) InstantiateLayout(SpaceConfiguration configs, double width, double length, Polygon rectangle, Transform xform)
        {
            var orderedKeys = configs.OrderByDescending(kvp => kvp.Value.CellBoundary.Depth * kvp.Value.CellBoundary.Width).Select(kvp => kvp.Key);
            var configsThatFitWell = new List<ContentConfiguration>();
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

            string[] countableOneSeaters = new[] {
                "https://hypar-content-catalogs.s3-us-west-2.amazonaws.com/ede8c007-c57e-4b2f-bea1-92d4f9a400c0/Mattiazzi-Branca-BarStool-BarStool-NaturalAsh.glb",
                "https://hypar-content-catalogs.s3-us-west-2.amazonaws.com/2290ea5e-98aa-429d-8fab-1f260458bf57/Steelcase+Turnstone+-+Simple+-+Stool+-+Seat+with+Cushion.glb",
                "https://hypar-content-catalogs.s3-us-west-2.amazonaws.com/ede8c007-c57e-4b2f-bea1-92d4f9a400c0/Orangebox_Seating_Stool_Cubb_Bar-WireBase.glb",
                "https://hypar-content-catalogs.s3-us-west-2.amazonaws.com/2290ea5e-98aa-429d-8fab-1f260458bf57/Steelcase+-+Seating+-+QiVi+428+Series+-+Collaborative+Chairs+-+With+Arm+-+Upholstered+Seat.glb",
                "https://hypar-content-catalogs.s3-us-west-2.amazonaws.com/ede8c007-c57e-4b2f-bea1-92d4f9a400c0/Viccarbe-Brix-Armchair-WideRight-LeftTable.glb",
                "https://hypar-content-catalogs.s3-us-west-2.amazonaws.com/ede8c007-c57e-4b2f-bea1-92d4f9a400c0/Viccarbe-Brix-Armchair-WideLeft-RightTable.glb",
                "https://hypar-content-catalogs.s3-us-west-2.amazonaws.com/ede8c007-c57e-4b2f-bea1-92d4f9a400c0/Steelcase-Seating-Series1-Stool-Stool.glb"
            };
            string[] countableTwoSeaters = new[] {
                "https://hypar-content-catalogs.s3-us-west-2.amazonaws.com/5e796702-15a4-47bb-bbfa-1dfa3f6db835/Steelcase+-+Seating+-+Sylvi+-+Lounge+-+Rectangular+-+66W.glb",
                "https://hypar-content-catalogs.s3-us-west-2.amazonaws.com/ede8c007-c57e-4b2f-bea1-92d4f9a400c0/SteelcaseCoalesse-Lagunitas-Seating-Chaise-TwoSeat-LowBackScreen-LeftCornerCushion.glb",
                "https://hypar-content-catalogs.s3-us-west-2.amazonaws.com/ede8c007-c57e-4b2f-bea1-92d4f9a400c0/SteelcaseCoalesse-Lagunitas-Seating-Chaise-TwoSeat-HighBackScreen-LeftCornerCushion.glb",
            };
            countableSeatCount += CountConfigSeats(selectedConfig, countableOneSeaters, 1);
            countableSeatCount += CountConfigSeats(selectedConfig, countableTwoSeaters, 2);

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