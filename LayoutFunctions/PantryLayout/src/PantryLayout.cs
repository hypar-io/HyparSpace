using Elements;
using Elements.Geometry;
using System.Collections.Generic;
using Elements.Components;
using System.Linq;
using System.IO;
using Newtonsoft.Json;
using System;
using Elements.Spatial;
using System.Diagnostics;
using LayoutFunctionCommon;

namespace PantryLayout
{
    public static class PantryLayout
    {
        /// <summary>
        /// The PantryLayout function.
        /// </summary>
        /// <param name="model">The input model.</param>
        /// <param name="input">The arguments to the execution.</param>
        /// <returns>A PantryLayoutOutputs instance containing computed results and the model with any new elements.</returns>
        public static PantryLayoutOutputs Execute(Dictionary<string, Model> inputModels, PantryLayoutInputs input)
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
            var outputModel = new Model();
            var configJson = File.ReadAllText("./PantryConfigurations.json");
            var configs = JsonConvert.DeserializeObject<SpaceConfiguration>(configJson);
            int totalCountableSeats = 0;

            foreach (var lvl in levels)
            {
                var corridors = lvl.Elements.OfType<CirculationSegment>();
                var corridorSegments = corridors.SelectMany(p => p.Profile.Segments());
                var meetingRmBoundaries = lvl.Elements.OfType<SpaceBoundary>().Where(z => z.Name == "Pantry");
                var levelVolume = levelVolumes.FirstOrDefault(l =>
                    (lvl.AdditionalProperties.TryGetValue("LevelVolumeId", out var levelVolumeId) &&
                        levelVolumeId as string == l.Id.ToString())) ??
                        levelVolumes.FirstOrDefault(l => l.Name == lvl.Name);

                foreach (var room in meetingRmBoundaries)
                {
                    var spaceBoundary = room.Boundary;
                    Line orientationGuideEdge = WallGeneration.FindEdgeAdjacentToSegments(
                        spaceBoundary.Perimeter.Segments(), corridorSegments, out var wallCandidates);

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
                            LayoutStrategies.SetLevelVolume(layout.instance, levelVolume?.Id);
                            outputModel.AddElement(layout.instance);
                            totalCountableSeats += layout.count;
                        }
                        else if (trimmedGeo.Count() > 0)
                        {
                            var largestTrimmedShape = trimmedGeo.OfType<Polygon>().OrderBy(s => s.Area()).Last();
                            var cinchedVertices = rect.Vertices.Select(v => largestTrimmedShape.Vertices.OrderBy(v2 => v2.DistanceTo(v)).First()).ToList();
                            var cinchedPoly = new Polygon(cinchedVertices);
                            // output.Model.AddElement(new ModelCurve(cinchedPoly, BuiltInMaterials.ZAxis, levelVolume.Transform));

                            var layout = InstantiateLayout(configs, width, depth, cinchedPoly, room.Transform);
                            LayoutStrategies.SetLevelVolume(layout.instance, levelVolume?.Id);
                            outputModel.AddElement(layout.instance);

                            totalCountableSeats += layout.count;
                            Console.WriteLine("🤷‍♂️ funny shape!!!");
                        }
                    }

                }
            }
            OverrideUtilities.InstancePositionOverrides(input.Overrides, outputModel);


            var output = new PantryLayoutOutputs(totalCountableSeats);
            output.Model = outputModel;
            return output;
        }
        
        private static (ComponentInstance instance, int count) InstantiateLayout(SpaceConfiguration configs, double width, double length, Polygon rectangle, Transform xform)
        {
            string[] countableSeats = new[] { "Steelcase - Seating - Nooi - Cafeteria Chair - Chair",
                                              "Steelcase - Seating - Nooi - Stool - Bar Height" };

            int countableSeatCount = 0;
            ContentConfiguration selectedConfig = null;
            var orderedKeys = configs.OrderByDescending(kvp => kvp.Value.CellBoundary.Depth * kvp.Value.CellBoundary.Width).Select(kvp => kvp.Key);
            foreach (var key in orderedKeys)
            {
                var config = configs[key];
                if (config.CellBoundary.Width < width && config.CellBoundary.Depth < length)
                {
                    foreach (var item in config.ContentItems)
                    {
                        foreach (var countableSeat in countableSeats)
                        {
                            if (item.ContentElement.Name.Contains(countableSeat))
                            {
                                countableSeatCount++;
                            }
                        }
                    }

                    selectedConfig = config;
                    break;
                }
            }
            if (selectedConfig == null)
            {
                return (null, 0);
            }
            var baseRectangle = Polygon.Rectangle(selectedConfig.CellBoundary.Min, selectedConfig.CellBoundary.Max);
            var rules = selectedConfig.Rules();

            var componentDefinition = new ComponentDefinition(rules, selectedConfig.Anchors());
            var instance = componentDefinition.Instantiate(ContentConfiguration.AnchorsFromRect(rectangle.TransformedPolygon(xform)));
            var allPlacedInstances = instance.Instances;
            return (instance, countableSeatCount);
        }
    }

}