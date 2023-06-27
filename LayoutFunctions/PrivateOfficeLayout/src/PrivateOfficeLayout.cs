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
        private static readonly List<ElementProxy<SpaceBoundary>> proxies = new List<ElementProxy<SpaceBoundary>>();

        private static readonly string SpaceBoundaryDependencyName = SpaceSettingsOverride.Dependency;

        /// <summary>
        /// The PrivateOfficeLayout function.
        /// </summary>
        /// <param name="model">The input model.</param>
        /// <param name="input">The arguments to the execution.</param>
        /// <returns>A PrivateOfficeLayoutOutputs instance containing computed results and the model with any new elements.</returns>
        public static PrivateOfficeLayoutOutputs Execute(Dictionary<string, Model> inputModels, PrivateOfficeLayoutInputs input)
        {
            Elements.Serialization.glTF.GltfExtensions.UseReferencedContentExtension = true;

            proxies.Clear();
            var spacePlanningZones = inputModels["Space Planning Zones"];
            var hasLevels = inputModels.TryGetValue("Levels", out var levelsModel);
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
            var output = new PrivateOfficeLayoutOutputs();
            var assmLoc = System.Reflection.Assembly.GetExecutingAssembly().Location;
            var dir = Path.GetDirectoryName(assmLoc);
            var configJson = File.ReadAllText(Path.Combine(dir, "PrivateOfficeConfigurations.json"));
            var configs = JsonConvert.DeserializeObject<SpaceConfiguration>(configJson);

            var wallMat = new Material("Drywall", new Color(0.9, 0.9, 0.9, 1.0), 0.01, 0.01);
            var glassMat = new Material("Glass", new Color(0.7, 0.7, 0.7, 0.3), 0.3, 0.6);
            var mullionMat = new Material("Storefront Mullions", new Color(0.5, 0.5, 0.5, 1.0));

            var overridesById = GetOverridesByBoundaryId(input, levels);
            var totalSeatsCount = 0;
            foreach (var lvl in levels)
            {
                var corridors = lvl.Elements.OfType<CirculationSegment>();
                var corridorSegments = corridors.SelectMany(p => p.Profile.Segments()).ToList();
                var privateOfficeBoundaries = lvl.Elements.OfType<SpaceBoundary>().Where(z => z.Name == "Private Office");
                var levelVolume = levelVolumes.FirstOrDefault(l =>
                    (lvl.AdditionalProperties.TryGetValue("LevelVolumeId", out var levelVolumeId) &&
                        levelVolumeId as string == l.Id.ToString())) ??
                        levelVolumes.FirstOrDefault(l => l.Name == lvl.Name);

                var wallCandidateLines = new List<(Line line, string type)>();
                foreach (var room in privateOfficeBoundaries)
                {
                    var seatsCount = 0;
                    var config = MatchApplicableOverride(overridesById, GetElementProxy(room, privateOfficeBoundaries.Proxies(SpaceBoundaryDependencyName)), input);
                    var privateOfficeRoomBoundaries = DivideBoundaryAlongVAxis(room, levelVolume, corridorSegments, wallCandidateLines, config);

                    foreach (var roomBoundary in privateOfficeRoomBoundaries)
                    {
                        WallGeneration.FindWallCandidates(roomBoundary, levelVolume?.Profile, corridorSegments.Union(wallCandidateLines.Where(w => w.type == "Glass-Edge").Select(w => w.line)), out Line orientationGuideEdge);

                        var relativeRoomTransform = room.Transform.Concatenated(levelVolume.Transform.Inverted());
                        var orientationTransform = new Transform(Vector3.Origin, orientationGuideEdge.Direction(), Vector3.ZAxis);
                        orientationTransform.Concatenate(relativeRoomTransform);
                        var boundaryCurves = new List<Polygon>();
                        boundaryCurves.Add(roomBoundary.Boundary.Perimeter.TransformedPolygon(relativeRoomTransform));
                        boundaryCurves.AddRange(roomBoundary.Boundary.Voids?.Select(v => v.TransformedPolygon(relativeRoomTransform)) ?? new List<Polygon>());

                        var grid = new Grid2d(boundaryCurves, orientationTransform);
                        if (config.Value.OfficeSizing.AutomateOfficeSubdivisions)
                        {
                            try
                            {
                                grid.U.DivideByApproximateLength(config.Value.OfficeSizing.OfficeSize, EvenDivisionMode.RoundDown);
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
                                var layout = InstantiateLayout(configs, width, depth, rect, levelVolume?.Transform ?? new Transform());
                                LayoutStrategies.SetLevelVolume(layout, levelVolume?.Id);
                                output.Model.AddElement(layout);
                                seatsCount++;
                            }
                            else if (trimmedGeo.Count() > 0)
                            {
                                var largestTrimmedShape = trimmedGeo.OfType<Polygon>().OrderBy(s => s.Area()).Last();
                                var cinchedVertices = rect.Vertices.Select(v => largestTrimmedShape.Vertices.OrderBy(v2 => v2.DistanceTo(v)).First()).ToList();
                                var cinchedPoly = new Polygon(cinchedVertices);
                                var areaRatio = cinchedPoly.Area() / rect.Area();
                                if (areaRatio > 0.7)
                                {
                                    var layout = InstantiateLayout(configs, width, depth, cinchedPoly, levelVolume?.Transform ?? new Transform());
                                    LayoutStrategies.SetLevelVolume(layout, levelVolume?.Id);
                                    output.Model.AddElement(layout);
                                    seatsCount++;
                                }
                            }
                        }

                        if (config.Value.CreateWalls)
                        {
                            wallCandidateLines.AddRange(WallGeneration.PartitionsAndGlazingCandidatesFromGrid(wallCandidateLines, grid, levelVolume?.Profile));
                        }
                    }

                    totalSeatsCount += seatsCount;
                    output.Model.AddElement(new SpaceMetric(room.Id, seatsCount, seatsCount, 0, 0));
                }

                var height = privateOfficeBoundaries.FirstOrDefault()?.Height ?? 3;
                output.Model.AddElement(new InteriorPartitionCandidate(Guid.NewGuid())
                {
                    WallCandidateLines = wallCandidateLines,
                    Height = height,
                    LevelTransform = levelVolume?.Transform ?? new Transform()
                });
            }
            output.PrivateOfficeCount = totalSeatsCount;
            OverrideUtilities.InstancePositionOverrides(input.Overrides, output.Model);
            output.Model.AddElements(proxies);
            return output;
        }

        private static IEnumerable<SpaceBoundary> DivideBoundaryAlongVAxis(SpaceBoundary room, LevelVolume levelVolume, List<Line> corridorSegments, List<(Line line, string type)> wallCandidateLines, SpaceSettingsOverride config)
        {
            var levelInvertedTransform = levelVolume.Transform.Inverted();
            if (config.Value.OfficeSizing.AutomateOfficeSubdivisions)
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
                if (config.Value.CreateWalls)
                {
                    wallCandidateLines.AddRange(initialWallCandidates.Select(c => (c.line.TransformedLine(levelInvertedTransform), c.type)));
                }
                var relativeRoomTransform = room.Transform.Concatenated(levelInvertedTransform);
                var relativeRoomTransformProjected = new Transform(0, 0, -relativeRoomTransform.Origin.Z);
                var orientationTransform = new Transform(Vector3.Origin, orientationGuideEdge.Direction(), Vector3.ZAxis);
                orientationTransform.Concatenate(relativeRoomTransform);
                var boundaryCurves = new List<Polygon>();
                boundaryCurves.Add(room.Boundary.Perimeter.TransformedPolygon(relativeRoomTransform));
                boundaryCurves.AddRange(room.Boundary.Voids?.Select(v => v.TransformedPolygon(relativeRoomTransform)) ?? new List<Polygon>());
                var tempGrid = new Grid2d(boundaryCurves, orientationTransform);
                try
                {
                    var vLength = tempGrid.V.Domain.Length;
                    if (vLength > 3 * config.Value.OfficeSizing.OfficeSize)
                    {
                        var officeCount = Math.Floor(vLength / config.Value.OfficeSizing.OfficeSize);
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
                                var profile = new Profile(cellBoundaries).Transformed(relativeRoomTransformProjected);
                                var spaceBoundary = new SpaceBoundary()
                                {
                                    Boundary = profile,
                                    Area = profile.Area(),
                                    Transform = room.Transform,
                                    Material = room.Material,
                                    Name = room.Name
                                };
                                spaceBoundary.ParentCentroid = room.IndividualCentroid;
                                returnCells.Add(spaceBoundary);
                            }
                            else
                            {
                                corridorSegments.AddRange(cellBoundaries.SelectMany(c => c.Segments()));
                            }
                        }
                        if (config.Value.CreateWalls)
                        {
                            wallCandidateLines.AddRange(WallGeneration.PartitionsAndGlazingCandidatesFromGrid(wallCandidateLines, tempGrid, levelVolume?.Profile));
                        }
                        return returnCells;
                    }
                    else if (vLength > 2 * config.Value.OfficeSizing.OfficeSize)
                    {
                        tempGrid.V.DivideByApproximateLength(config.Value.OfficeSizing.OfficeSize, EvenDivisionMode.RoundDown);
                        if (config.Value.CreateWalls)
                        {
                            wallCandidateLines.AddRange(WallGeneration.PartitionsAndGlazingCandidatesFromGrid(wallCandidateLines, tempGrid, levelVolume?.Profile));
                        }
                        return tempGrid.GetCells().Select(c =>
                        {
                            var profile = new Profile(c.GetTrimmedCellGeometry().OfType<Polygon>().ToList()).Transformed(relativeRoomTransformProjected);
                            var spaceBoundary = new SpaceBoundary()
                            {
                                Boundary = profile,
                                Area = profile.Area(),
                                Transform = room.Transform,
                                Material = room.Material,
                                Name = room.Name
                            };
                            spaceBoundary.ParentCentroid = room.IndividualCentroid;
                            return spaceBoundary;
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
            }
            else
            {
                if (config.Value.CreateWalls)
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
                    wallCandidateLines.AddRange(initialWallCandidates.Select(c => (c.line.TransformedLine(levelInvertedTransform), c.type)));
                }
                return new[] { room };
            }
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

        private static Dictionary<Guid, SpaceSettingsOverride> GetOverridesByBoundaryId(PrivateOfficeLayoutInputs input, IEnumerable<LevelElements> levels)
        {
            var overridesById = new Dictionary<Guid, SpaceSettingsOverride>();
            foreach (var spaceOverride in input.Overrides?.SpaceSettings ?? new List<SpaceSettingsOverride>())
            {
                var matchingBoundary =
                levels.SelectMany(l => l.Elements)
                    .OfType<SpaceBoundary>()
                    .OrderBy(ob => ob.ParentCentroid.Value
                    .DistanceTo(spaceOverride.Identity.ParentCentroid))
                    .First();

                if (overridesById.ContainsKey(matchingBoundary.Id))
                {
                    var mbCentroid = matchingBoundary.ParentCentroid.Value;
                    if (overridesById[matchingBoundary.Id].Identity.ParentCentroid.DistanceTo(mbCentroid) > spaceOverride.Identity.ParentCentroid.DistanceTo(mbCentroid))
                    {
                        overridesById[matchingBoundary.Id] = spaceOverride;
                    }
                }
                else
                {
                    overridesById.Add(matchingBoundary.Id, spaceOverride);
                }
            }

            return overridesById;
        }

        private static SpaceSettingsOverride MatchApplicableOverride(
            Dictionary<Guid, SpaceSettingsOverride> overridesById,
            ElementProxy<SpaceBoundary> boundaryProxy,
            PrivateOfficeLayoutInputs input)
        {
            var overrideName = SpaceSettingsOverride.Name;
            SpaceSettingsOverride config;

            // See if we already have matching override attached
            var existingOverrideId = boundaryProxy.OverrideIds<SpaceSettingsOverride>(overrideName).FirstOrDefault();
            if (existingOverrideId != null)
            {
                if (overridesById.TryGetValue(Guid.Parse(existingOverrideId), out config))
                {
                    return config;
                }
            }

            // Try to match from identity in configs dictionary. Use a default in case none found
            if (!overridesById.TryGetValue(boundaryProxy.ElementId, out config))
            {
                config = new SpaceSettingsOverride(
                            Guid.NewGuid().ToString(),
                            null,
                            new SpaceSettingsValue(
                                new SpaceSettingsValueOfficeSizing(
                                    input.OfficeSizing.AutomateOfficeSubdivisions,
                                    input.OfficeSizing.OfficeSize),
                                    input.CreateWalls
                            )
                    );
                overridesById.Add(boundaryProxy.ElementId, config);
            }

            // Attach the identity and values data to the proxy
            boundaryProxy.AddOverrideIdentity(overrideName, config.Id, config.Identity);
            boundaryProxy.AddOverrideValue(overrideName, config.Value);

            // Make sure proxies list has the proxy so that it will serialize in the model.
            if (!proxies.Contains(boundaryProxy))
            {
                proxies.Add(boundaryProxy);
            }

            return config;
        }

        private static ElementProxy<SpaceBoundary> GetElementProxy(SpaceBoundary spaceBoundary, IEnumerable<ElementProxy<SpaceBoundary>> allSpaceBoundaries)
        {
            return allSpaceBoundaries.Proxy(spaceBoundary) ?? spaceBoundary.Proxy(SpaceBoundaryDependencyName);
        }
    }
}