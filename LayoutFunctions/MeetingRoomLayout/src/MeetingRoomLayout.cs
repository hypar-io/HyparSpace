using Elements;
using Elements.Geometry;
using System.Collections.Generic;
using Elements.Components;
using System.Linq;
using System.IO;
using Newtonsoft.Json;
using System;
using Elements.Spatial;

namespace MeetingRoomLayout
{
    public static class MeetingRoomLayout
    {
        /// <summary>
        /// The MeetingRoomLayout function.
        /// </summary>
        /// <param name="model">The input model.</param>
        /// <param name="input">The arguments to the execution.</param>
        /// <returns>A MeetingRoomLayoutOutputs instance containing computed results and the model with any new elements.</returns>
        public static MeetingRoomLayoutOutputs Execute(Dictionary<string, Model> inputModels, MeetingRoomLayoutInputs input)
        {
            var spacePlanningZones = inputModels["Space Planning Zones"];
            var levelsModel = inputModels["Levels"];
            var levels = spacePlanningZones.AllElementsOfType<LevelElements>();
            var levelVolumes = levelsModel.AllElementsOfType<LevelVolume>();
            var output = new MeetingRoomLayoutOutputs();
            var configJson = File.ReadAllText("./ConferenceRoomConfigurations.json");
            var configs = JsonConvert.DeserializeObject<SpaceConfiguration>(configJson);

            var wallMat = new Material("Drywall", new Color(0.9, 0.9, 0.9, 1.0), 0.01, 0.01);
            var glassMat = new Material("Glass", new Color(0.7, 0.7, 0.7, 0.3), 0.3, 0.6);
            var mullionMat = new Material("Storefront Mullions", new Color(0.5, 0.5, 0.5, 1.0));

            foreach (var lvl in levels)
            {
                var corridors = lvl.Elements.OfType<Floor>();
                var corridorSegments = corridors.SelectMany(p => p.Profile.Segments());
                var meetingRmBoundaries = lvl.Elements.OfType<SpaceBoundary>().Where(z => z.Name == "Meeting Room");
                var levelVolume = levelVolumes.First(l => l.Name == lvl.Name);
                var wallCandidateLines = new List<(Line line, string type)>();
                foreach (var room in meetingRmBoundaries)
                {
                    var spaceBoundary = room.Boundary;
                    Line orientationGuideEdge = FindEdgeAdjacentToSegments(spaceBoundary.Perimeter.Segments(), corridorSegments, out var wallCandidates);
                    wallCandidateLines.Add((orientationGuideEdge, "Glass"));
                    var exteriorWalls = FindEdgeAdjacentToSegments(wallCandidates, levelVolume.Profile.Segments(), out var solidWalls, 0.6);
                    wallCandidateLines.AddRange(solidWalls.Select(s => (s, "Solid")));
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
                            output.Model.AddElement(InstantiateLayout(configs, width, depth, rect, room.Transform));
                        }
                        else if (trimmedGeo.Count() > 0)
                        {
                            var largestTrimmedShape = trimmedGeo.OfType<Polygon>().OrderBy(s => s.Area()).Last();
                            var cinchedVertices = rect.Vertices.Select(v => largestTrimmedShape.Vertices.OrderBy(v2 => v2.DistanceTo(v)).First()).ToList();
                            var cinchedPoly = new Polygon(cinchedVertices);
                            output.Model.AddElement(InstantiateLayout(configs, width, depth, cinchedPoly, room.Transform));
                        }
                    }

                }
                var mullionSize = 0.07;
                var doorWidth = 0.9;
                var doorHeight = 2.1;
                var sideLightWidth = 0.4;
                var totalStorefrontHeight = Math.Min(2.7, levelVolume.Height);
                var mullion = new StandardWall(new Line(new Vector3(-mullionSize / 2, 0, 0), new Vector3(mullionSize / 2, 0, 0)), mullionSize, totalStorefrontHeight, mullionMat);
                mullion.IsElementDefinition = true;
                if (input.CreateWalls)
                {
                    foreach (var wallCandidate in wallCandidateLines)
                    {
                        if (wallCandidate.type == "Solid")
                        {
                            output.Model.AddElement(new StandardWall(wallCandidate.line, 0.2, levelVolume.Height, wallMat, levelVolume.Transform));
                        }
                        else if (wallCandidate.type == "Glass")
                        {
                            var grid = new Grid1d(wallCandidate.line);
                            grid.SplitAtOffsets(new[] { sideLightWidth, sideLightWidth + doorWidth });
                            grid[2].DivideByApproximateLength(2);
                            var separators = grid.GetCellSeparators(true);
                            var beam = new Beam(wallCandidate.line, Polygon.Rectangle(mullionSize, mullionSize), mullionMat, 0, 0, 0, isElementDefinition: true);
                            output.Model.AddElement(beam.CreateInstance(levelVolume.Transform, "Base Mullion"));
                            output.Model.AddElement(beam.CreateInstance(levelVolume.Transform.Concatenated(new Transform(0, 0, doorHeight)), "Base Mullion"));
                            output.Model.AddElement(beam.CreateInstance(levelVolume.Transform.Concatenated(new Transform(0, 0, totalStorefrontHeight)), "Base Mullion"));
                            foreach (var separator in separators)
                            {
                                // var line = new Line(separator, separator + new Vector3(0, 0, levelVolume.Height));
                                // output.Model.AddElement(new ModelCurve(line, BuiltInMaterials.XAxis, levelVolume.Transform));
                                var instance = mullion.CreateInstance(new Transform(separator, wallCandidate.line.Direction(), Vector3.ZAxis, 0).Concatenated(levelVolume.Transform), "Mullion");
                                output.Model.AddElement(instance);
                            }
                            output.Model.AddElement(new StandardWall(wallCandidate.line, 0.05, totalStorefrontHeight, glassMat, levelVolume.Transform));
                            var headerHeight = levelVolume.Height - totalStorefrontHeight;
                            if (headerHeight > 0.01)
                            {
                                output.Model.AddElement(new StandardWall(wallCandidate.line, 0.2, headerHeight, wallMat, levelVolume.Transform.Concatenated(new Transform(0, 0, totalStorefrontHeight))));
                            }
                        }
                    }
                }
            }
            InstancePositionOverrides(input.Overrides, output.Model);
            return output;
        }

        private static void InstancePositionOverrides(Overrides overrides, Model model)
        {
            var allElementInstances = model.AllElementsOfType<ElementInstance>();
            var pointTranslations = allElementInstances.Select(ei => ei.Transform.Origin).Distinct().Select(t => new PointTranslation(t, t, new Transform(), null, null, false, Guid.NewGuid(), null)).ToList();
            if (overrides != null)
            {
                foreach (var positionOverride in overrides.FurnitureLocations)
                {
                    var thisOriginalLocation = positionOverride.Identity.OriginalLocation;
                    var thisPt = positionOverride.Value.Location;
                    thisPt.Z = thisOriginalLocation.Z;
                    var nearInstances = allElementInstances.Where(ei => ei.Transform.Origin.DistanceTo(thisOriginalLocation) < 0.01);
                    nearInstances.ToList().ForEach(ni => ni.Transform.Concatenate(new Transform(thisPt.X - ni.Transform.Origin.X, thisPt.Y - ni.Transform.Origin.Y, 0)));
                    // should only be one
                    var nearTranslations = pointTranslations.Where(pt => pt.OriginalLocation.DistanceTo(thisOriginalLocation) < 0.01);
                    nearTranslations.ToList().ForEach(nt => nt.Location = thisPt);
                }

            }
            model.AddElements(pointTranslations);
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
        private static ComponentInstance InstantiateLayout(SpaceConfiguration configs, double width, double length, Polygon rectangle, Transform xform)
        {
            ContentConfiguration selectedConfig = null;
            var orderedKeys = new[] { "22P", "20P", "14P", "13P", "8P", "6P-A", "6P-B", "4P-A", "4P-B" };
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
            var instance = componentDefinition.Instantiate(ContentConfiguration.AnchorsFromRect(rectangle.TransformedPolygon(xform)));
            return instance;
        }
    }

}