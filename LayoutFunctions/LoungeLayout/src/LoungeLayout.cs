using Elements;
using Elements.Geometry;
using System.Collections.Generic;
using Elements.Components;
using System.Linq;
using System.IO;
using Newtonsoft.Json;
using System;
using Elements.Spatial;

namespace LoungeLayout
{
    public static class LoungeLayout
    {
        /// <summary>
        /// The LoungeLayout function.
        /// </summary>
        /// <param name="model">The input model.</param>
        /// <param name="input">The arguments to the execution.</param>
        /// <returns>A LoungeLayoutOutputs instance containing computed results and the model with any new elements.</returns>
        public static LoungeLayoutOutputs Execute(Dictionary<string, Model> inputModels, LoungeLayoutInputs input)
        {
            var spacePlanningZones = inputModels["Space Planning Zones"];
            var levelsModel = inputModels["Levels"];
            var levels = spacePlanningZones.AllElementsOfType<LevelElements>();
            var levelVolumes = levelsModel.AllElementsOfType<LevelVolume>();
            var output = new LoungeLayoutOutputs();
            var configJson = File.ReadAllText("./LoungeConfigurations.json");
            var configs = JsonConvert.DeserializeObject<SpaceConfiguration>(configJson);

            foreach (var lvl in levels)
            {
                var corridors = lvl.Elements.OfType<Floor>();
                var corridorSegments = corridors.SelectMany(p => p.Profile.Segments());
                var meetingRmBoundaries = lvl.Elements.OfType<SpaceBoundary>().Where(z => z.Name == "Lounge");
                var levelVolume = levelVolumes.First(l => l.Name == lvl.Name);
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
                            output.Model.AddElement(InstantiateLayout(configs, width, depth, rect, room.Transform));
                        }
                        else if (trimmedGeo.Count() > 0)
                        {
                            var largestTrimmedShape = trimmedGeo.OfType<Polygon>().OrderBy(s => s.Area()).Last();
                            var cinchedVertices = rect.Vertices.Select(v => largestTrimmedShape.Vertices.OrderBy(v2 => v2.DistanceTo(v)).First()).ToList();
                            var cinchedPoly = new Polygon(cinchedVertices);
                            // output.Model.AddElement(new ModelCurve(cinchedPoly, BuiltInMaterials.ZAxis, levelVolume.Transform));
                            output.Model.AddElement(InstantiateLayout(configs, width, depth, cinchedPoly, room.Transform));
                            Console.WriteLine("🤷‍♂️ funny shape!!!");
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
            var orderedKeys = configs.OrderByDescending(kvp => kvp.Value.CellBoundary.Depth * kvp.Value.CellBoundary.Width).Select(kvp => kvp.Key);
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
            var allPlacedInstances = instance.Instances;
            // foreach (var item in allPlacedInstances)
            // {
            //     if (item is ElementInstance ei && !rectangle.Contains(ei.Transform.Origin))
            //     {

            //     }
            // }
            return instance;
        }
    }

}