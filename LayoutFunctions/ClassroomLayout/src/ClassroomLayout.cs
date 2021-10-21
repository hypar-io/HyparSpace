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
            var spacePlanningZones = inputModels["Space Planning Zones"];
            inputModels.TryGetValue("Levels", out var levelsModel);
            var levels = spacePlanningZones.AllElementsOfType<LevelElements>();
            var levelVolumes = levelsModel?.AllElementsOfType<LevelVolume>() ?? new List<LevelVolume>();
            var output = new ClassroomLayoutOutputs();
            var configJson = File.ReadAllText("./ClassroomConfigurations.json");
            var configs = JsonConvert.DeserializeObject<SpaceConfiguration>(configJson);

            var wallMat = new Material("Drywall", new Color(0.9, 0.9, 0.9, 1.0), 0.01, 0.01);
            var glassMat = new Material("Glass", new Color(0.7, 0.7, 0.7, 0.3), 0.3, 0.6);
            var mullionMat = new Material("Storefront Mullions", new Color(0.5, 0.5, 0.5, 1.0));

            foreach (var lvl in levels)
            {
                var corridors = lvl.Elements.OfType<Floor>();
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
                    wallCandidateLines.AddRange(WallGeneration.FindWallCandidates(room, levelVolume?.Profile, corridorSegments, out Line orientationGuideEdge));
                    var orientationTransform = new Transform(Vector3.Origin, orientationGuideEdge.Direction(), Vector3.ZAxis);
                    var boundaryCurves = new List<Polygon>();
                    boundaryCurves.Add(spaceBoundary.Perimeter);
                    boundaryCurves.AddRange(spaceBoundary.Voids ?? new List<Polygon>());

                    var grid = new Grid2d(boundaryCurves, orientationTransform);
                    var deskConfig = configs["Desk"];
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
                                        foreach (var contentItem in deskConfig.ContentItems)
                                        {
                                            var instance = contentItem.ContentElement.CreateInstance(
                                                contentItem.Transform
                                                .Concatenated(orientationTransform)
                                                .Concatenated(new Transform(cellRect.Vertices[0]))
                                                .Concatenated(room.Transform),
                                                "Desk");
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
                if (levelVolume == null)
                {
                    // if we didn't get a level volume, make a fake one.
                    levelVolume = new LevelVolume() { Height = 3.0, };
                }
                if (input.CreateWalls)
                {
                    WallGeneration.GenerateWalls(output.Model, wallCandidateLines, levelVolume.Height, levelVolume.Transform);
                }
            }
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
            var instance = componentDefinition.Instantiate(ContentConfiguration.AnchorsFromRect(rectangle.TransformedPolygon(xform)));
            return instance;
        }
    }

}