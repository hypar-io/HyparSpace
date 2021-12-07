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

namespace PrivateOfficeLayout
{
    public static class PrivateOfficeLayout
    {
        /// <summary>
        /// The PrivateOfficeLayout function.
        /// </summary>
        /// <param name="model">The input model.</param>
        /// <param name="input">The arguments to the execution.</param>
        /// <returns>A PrivateOfficeLayoutOutputs instance containing computed results and the model with any new elements.</returns>
        public static PrivateOfficeLayoutOutputs Execute(Dictionary<string, Model> inputModels, PrivateOfficeLayoutInputs input)
        {
            var spacePlanningZones = inputModels["Space Planning Zones"];
            var hasLevels = inputModels.TryGetValue("Levels", out var levelsModel);
            var levels = spacePlanningZones.AllElementsOfType<LevelElements>();
            var levelVolumes = levelsModel?.AllElementsOfType<LevelVolume>() ?? new List<LevelVolume>();
            var output = new PrivateOfficeLayoutOutputs();
            var assmLoc = System.Reflection.Assembly.GetExecutingAssembly().Location;
            var dir = Path.GetDirectoryName(assmLoc);
            var configJson = File.ReadAllText(Path.Combine(dir, "PrivateOfficeConfigurations.json"));
            var configs = JsonConvert.DeserializeObject<SpaceConfiguration>(configJson);

            var wallMat = new Material("Drywall", new Color(0.9, 0.9, 0.9, 1.0), 0.01, 0.01);
            var glassMat = new Material("Glass", new Color(0.7, 0.7, 0.7, 0.3), 0.3, 0.6);
            var mullionMat = new Material("Storefront Mullions", new Color(0.5, 0.5, 0.5, 1.0));

            foreach (var lvl in levels)
            {
                var corridors = lvl.Elements.OfType<Floor>();
                var corridorSegments = corridors.SelectMany(p => p.Profile.Segments()).ToList();
                var meetingRmBoundaries = lvl.Elements.OfType<SpaceBoundary>().Where(z => z.Name == "Private Office");
                var levelVolume = levelVolumes.FirstOrDefault(l =>
                    (lvl.AdditionalProperties.TryGetValue("LevelVolumeId", out var levelVolumeId) &&
                        levelVolumeId as string == l.Id.ToString())) ??
                        levelVolumes.FirstOrDefault(l => l.Name == lvl.Name);


                var wallCandidateLines = new List<(Line line, string type)>();
                if (input.OfficeSizing.AutomateOfficeSubdivisions)
                {
                    meetingRmBoundaries = meetingRmBoundaries.SelectMany((room) =>
                    {
                        var initialWallCandidates = WallGeneration.FindWallCandidates(room, levelVolume?.Profile, corridorSegments, out var orientationGuideEdge)
                                  .Select(w =>
                                  {
                                      if (w.type == "Glass")
                                      {
                                          w.type = "Glass-Edge";
                                      }
                                      return w;
                                  });
                        wallCandidateLines.AddRange(initialWallCandidates);
                        var roomTransformProjected = room.Transform.Concatenated(new Transform(0, 0, -room.Transform.Origin.Z));
                        var orientationTransform = new Transform(Vector3.Origin, orientationGuideEdge.Direction(), Vector3.ZAxis);
                        var boundaryCurves = new List<Polygon>();
                        boundaryCurves.Add(room.Boundary.Perimeter.TransformedPolygon(roomTransformProjected));
                        boundaryCurves.AddRange(room.Boundary.Voids?.Select(v => v.TransformedPolygon(roomTransformProjected)) ?? new List<Polygon>());
                        var tempGrid = new Grid2d(boundaryCurves, orientationTransform);
                        try
                        {
                            var vLength = tempGrid.V.Domain.Length;
                            if (vLength > 3 * input.OfficeSizing.OfficeSize)
                            {
                                var officeCount = Math.Floor(vLength / input.OfficeSizing.OfficeSize);
                                var corridorCount = Math.Floor(officeCount / 2 - 0.1); // 1 corridor for 3, 4 offices, 2 corridors for 5, 6 offices, etc
                                var idealOfficeSize = ((vLength - (corridorCount * 1.5)) / officeCount) * .99;
                                Console.WriteLine($"🤪 officeCount: {officeCount}, corridorCount: {corridorCount}, idealOfficeSize: {idealOfficeSize}");
                                tempGrid.V.DivideByPattern(new[] { ("Office", idealOfficeSize), ("Office", idealOfficeSize), ("Corridor", 1.5) });

                                List<SpaceBoundary> returnCells = new List<SpaceBoundary>();
                                foreach (var cell in tempGrid.GetCells())
                                {
                                    var cellBoundaries = cell.GetTrimmedCellGeometry()?.OfType<Polygon>().ToList() ?? new List<Polygon>();
                                    if (cellBoundaries.Count < 0)
                                    {
                                        continue;
                                    }
                                    if (cell.Type != null && cell.Type.Contains("Office"))
                                    {
                                        var profile = new Profile(cellBoundaries);
                                        returnCells.Add(new SpaceBoundary()
                                        {
                                            Boundary = profile,
                                            Area = profile.Area(),
                                            Transform = room.Transform,
                                            Material = room.Material,
                                            Name = room.Name
                                        });
                                    }
                                    else
                                    {
                                        corridorSegments.AddRange(cellBoundaries.SelectMany(c => c.Segments()));
                                    }
                                }
                                wallCandidateLines.AddRange(WallGeneration.PartitionsAndGlazingCandidatesFromGrid(wallCandidateLines, tempGrid, levelVolume?.Profile));
                                return returnCells;
                            }
                            else if (vLength > 2 * input.OfficeSizing.OfficeSize)
                            {
                                tempGrid.V.DivideByApproximateLength(input.OfficeSizing.OfficeSize, EvenDivisionMode.RoundDown);
                                wallCandidateLines.AddRange(WallGeneration.PartitionsAndGlazingCandidatesFromGrid(wallCandidateLines, tempGrid, levelVolume?.Profile));
                                return tempGrid.GetCells().Select(c =>
                                {
                                    var profile = new Profile(c.GetTrimmedCellGeometry().OfType<Polygon>().ToList());
                                    return new SpaceBoundary()
                                    {
                                        Boundary = profile,
                                        Area = profile.Area(),
                                        Transform = room.Transform,
                                        Material = room.Material,
                                        Name = room.Name
                                    };
                                }
                                );
                            }
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine("⚠️ exception while trying to subdivide office depth direction");
                            Console.WriteLine(e.Message);
                            Console.WriteLine(e.StackTrace);
                        }
                        return new[] { room };
                    });
                }

                foreach (var room in meetingRmBoundaries)
                {
                    var roomTransform = room.Transform.Concatenated(new Transform(0, 0, -room.Transform.Origin.Z));
                    var spaceBoundary = new Profile(room.Boundary.Perimeter.TransformedPolygon(roomTransform), room.Boundary.Voids?.Select(v => v.TransformedPolygon(roomTransform)).ToList() ?? new List<Polygon>(), Guid.NewGuid(), null);
                    WallGeneration.FindWallCandidates(room, levelVolume?.Profile, corridorSegments.Union(wallCandidateLines.Where(w => w.type == "Glass-Edge").Select(w => w.line)), out Line orientationGuideEdge);
                    var orientationTransform = new Transform(Vector3.Origin, orientationGuideEdge.Direction(), Vector3.ZAxis);
                    var boundaryCurves = new List<Polygon>();
                    boundaryCurves.Add(spaceBoundary.Perimeter);
                    boundaryCurves.AddRange(spaceBoundary.Voids ?? new List<Polygon>());

                    var grid = new Grid2d(boundaryCurves, orientationTransform);
                    if (input.OfficeSizing.AutomateOfficeSubdivisions)
                    {
                        try
                        {
                            grid.U.DivideByApproximateLength(input.OfficeSizing.OfficeSize, EvenDivisionMode.RoundDown);
                        }
                        catch
                        {
                            // continue
                        }
                    }
                    foreach (var cell in grid.GetCells())
                    {
                        var rect = cell.GetCellGeometry() as Polygon;
                        var segs = rect.Segments();
                        var width = segs[0].Length();
                        var depth = segs[1].Length();
                        var trimmedGeo = cell.GetTrimmedCellGeometry();
                        if (!cell.IsTrimmed() && trimmedGeo.Count() > 0)
                        {
                            output.Model.AddElement(InstantiateLayout(configs, width, depth, rect, levelVolume?.Transform ?? new Transform()));
                        }
                        else if (trimmedGeo.Count() > 0)
                        {
                            var largestTrimmedShape = trimmedGeo.OfType<Polygon>().OrderBy(s => s.Area()).Last();
                            var cinchedVertices = rect.Vertices.Select(v => largestTrimmedShape.Vertices.OrderBy(v2 => v2.DistanceTo(v)).First()).ToList();
                            var cinchedPoly = new Polygon(cinchedVertices);
                            var areaRatio = cinchedPoly.Area() / rect.Area();
                            if (areaRatio > 0.7)
                            {
                                output.Model.AddElement(InstantiateLayout(configs, width, depth, cinchedPoly, levelVolume?.Transform ?? new Transform()));
                            }
                            // else
                            // {
                            //     output.Model.AddElement(new Panel(cinchedPoly, BuiltInMaterials.XAxis, levelVolume.Transform));
                            // }
                        }
                    }
                    wallCandidateLines.AddRange(WallGeneration.PartitionsAndGlazingCandidatesFromGrid(wallCandidateLines, grid, levelVolume?.Profile));
                }
                if (levelVolume == null)
                {
                    // if we didn't get a level volume, make a fake one.
                    levelVolume = new LevelVolume() { Height = 4 };
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
            return instance;
        }
    }

}