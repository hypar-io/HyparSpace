using Elements;
using Elements.Components;
using Elements.Geometry;
using Elements.Spatial;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LayoutFunctionCommon
{
    public class LayoutGenerationResult
    {
        public Model OutputModel { get; set; }
        public int SeatsCount { get; set; }
    }

    public class LayoutGeneration<TLevelElements, TLevelVolume, TSpaceBoundary, TCirculationSegment>
        where TLevelElements : Element, ILevelElements
        where TSpaceBoundary : ISpaceBoundary
        where TLevelVolume : GeometricElement, ILevelVolume
        where TCirculationSegment : Floor, ICirculationSegment
    {
        public virtual LayoutGenerationResult StandardLayoutOnAllLevels(string programTypeName,
                                              Dictionary<string, Model> inputModels,
                                              dynamic overrides,
                                              bool createWalls,
                                              string configurationsPath,
                                              string catalogPath = "catalog.json")
        {

            var outputModel = new Model();
            var totalSeats = 0;
            ContentCatalogRetrieval.SetCatalogFilePath(catalogPath);
            var spacePlanningZones = inputModels["Space Planning Zones"];
            var levels = GetLevels(inputModels, spacePlanningZones);
            var levelVolumes = GetLevelVolumes(inputModels);
            var configJson = configurationsPath != null ? File.ReadAllText(configurationsPath) : "{}";
            var configs = DeserializeConfigJson(configJson);
            foreach (var lvl in levels)
            {
                var corridors = lvl.Elements.Where(e => e is Floor).OfType<Floor>();
                var corridorSegments = corridors.SelectMany(p => p.Profile.Segments());
                var roomBoundaries = lvl.Elements.OfType<TSpaceBoundary>().Where(z => z.Name == programTypeName);
                var levelVolume = levelVolumes.FirstOrDefault(l =>
                    (lvl.AdditionalProperties.TryGetValue("LevelVolumeId", out var levelVolumeId) &&
                        levelVolumeId as string == l.Id.ToString())) ??
                        levelVolumes.FirstOrDefault(l => l.Name == lvl.Name);
                var wallCandidateLines = new List<(Line line, string type)>();
                foreach (var room in roomBoundaries)
                {
                    var seatsCount = 0;
                    var success = false;
                    var spaceBoundary = room.Boundary;
                    var wallCandidateOptions = WallGeneration.FindWallCandidateOptions(room, levelVolume?.Profile, corridorSegments);

                    foreach (var (OrientationGuideEdge, WallCandidates) in wallCandidateOptions)
                    {
                        var orientationTransform = new Transform(Vector3.Origin, OrientationGuideEdge.Direction(), Vector3.ZAxis);
                        var boundaryCurves = new List<Polygon>
                        {
                            spaceBoundary.Perimeter
                        };
                        boundaryCurves.AddRange(spaceBoundary.Voids ?? new List<Polygon>());

                        var grid = new Grid2d(boundaryCurves, orientationTransform);
                        foreach (var cell in grid.GetCells())
                        {
                            var layout = InstantiateLayoutByFit(configs, cell, room.Transform);
                            if (layout != null)
                            {
                                success = true;
                                SetLevelVolume(layout.Instance, levelVolume?.Id);

                                wallCandidateLines.AddRange(WallCandidates);
                                outputModel.AddElement(layout.Instance);
                                seatsCount += CountSeats(layout);
                            }
                            else if (configs.Count == 0)
                            {
                                success = true;
                                wallCandidateLines.AddRange(WallCandidates);
                            }
                        }

                        if (success)
                        {
                            break;
                        }
                    }

                    totalSeats += seatsCount;
                    outputModel.AddElement(new SpaceMetric(room.Id, seatsCount, 0, 0, 0));
                }

                double height = levelVolume?.Height ?? 3;
                Transform xform = levelVolume?.Transform ?? new Transform();

                if (createWalls)
                {
                    outputModel.AddElement(new InteriorPartitionCandidate(Guid.NewGuid())
                    {
                        WallCandidateLines = wallCandidateLines,
                        Height = height,
                        LevelTransform = xform,
                    });
                }
            }
            OverrideUtilities.InstancePositionOverrides(overrides, outputModel);

            return new LayoutGenerationResult
            {
                SeatsCount = totalSeats,
                OutputModel = outputModel
            };
        }

        /// <summary>
        /// Instantiate a space by finding the largest space that will fit from a SpaceConfiguration.
        /// </summary>
        /// <param name="configs">The configuration containing all possible space arrangements</param>
        /// <param name="width">The width of the space to fill</param>
        /// <param name="length">The length of the space to fill</param>
        /// <param name="rectangle">The more-or-less rectangular polygon to fill</param>
        /// <param name="xform">A transform to apply to the rectangle.</param>
        /// <returns></returns>
        public LayoutInstantiated InstantiateLayoutByFit(SpaceConfiguration configs, double width, double length, Polygon rectangle, Transform xform)
        {
            var selectedConfigPair = FindConfig(width, length, configs);
            if (!selectedConfigPair.HasValue)
            {
                return null;
            }
            var selectedConfig = selectedConfigPair.Value.Value;
            var selectedConfigName = selectedConfigPair.Value.Key;
            var rules = selectedConfig.Rules();

            var componentDefinition = new ComponentDefinition(rules, selectedConfig.Anchors());
            var instance = componentDefinition.Instantiate(ContentConfiguration.AnchorsFromRect(rectangle.TransformedPolygon(xform)));
            return new LayoutInstantiated() { Instance = instance, Config = selectedConfig, ConfigName = selectedConfigName };
        }

        /// <summary>
        /// Instantiate a space by finding the largest space that will fit a grid cell from a SpaceConfiguration.
        /// </summary>
        /// <param name="configs">The configuration containing all possible space arrangements.</param>
        /// <param name="width">The 2d grid cell to fill.</param>
        /// <param name="xform">A transform to apply to the rectangle.</param>
        /// <returns></returns>
        public LayoutInstantiated InstantiateLayoutByFit(SpaceConfiguration configs, Grid2d cell, Transform xform)
        {
            var rect = cell.GetCellGeometry() as Polygon;
            var segs = rect.Segments();
            var width = segs[0].Length();
            var depth = segs[1].Length();
            var trimmedGeo = cell.GetTrimmedCellGeometry();
            if (!cell.IsTrimmed() && trimmedGeo.Count() > 0)
            {
                return InstantiateLayoutByFit(configs, width, depth, rect, xform);
            }
            else if (trimmedGeo.Count() > 0)
            {
                var largestTrimmedShape = trimmedGeo.OfType<Polygon>().OrderBy(s => s.Area()).Last();
                try
                {
                    if (largestTrimmedShape.Vertices.Count < 8)
                    {
                        // LIR does a better job if there are more vertices to work with.
                        var vertices = new List<Vector3>();
                        foreach (var segment in largestTrimmedShape.Segments())
                        {
                            vertices.Add(segment.Start);
                            vertices.Add(segment.Mid());
                        }
                        largestTrimmedShape = new Polygon(vertices);
                    }
                    // TODO: don't use XY — find two (or more) best guess axes
                    // from the convex hull or something. I get weird results
                    // from LIR for trianglish shapes that aren't XY aligned on
                    // any edge.

                    // XY aligned
                    Elements.LIR.LargestInteriorRectangle.CalculateLargestInteriorRectangle(largestTrimmedShape, out var bstBounds1);
                    // Dominant-Axis aligned
                    var longestEdge = largestTrimmedShape.Segments().OrderByDescending(s => s.Length()).First();
                    var transformToEdge = new Transform(longestEdge.Start, longestEdge.Direction(), Vector3.ZAxis);
                    var transformFromEdge = transformToEdge.Inverted();
                    var largestTrimmedShapeAligned = largestTrimmedShape.TransformedPolygon(transformFromEdge);
                    Elements.LIR.LargestInteriorRectangle.CalculateLargestInteriorRectangle(largestTrimmedShapeAligned, out var bstBounds2);
                    var largestInteriorRect = bstBounds1.area > bstBounds2.area ? bstBounds1.Polygon : bstBounds2.Polygon.TransformedPolygon(transformToEdge);
                    var widthSeg = largestInteriorRect.Segments().OrderBy(s => s.Direction().Dot(segs[0].Direction())).Last();
                    var depthSeg = largestInteriorRect.Segments().OrderBy(s => s.Direction().Dot(segs[1].Direction())).Last();
                    width = widthSeg.Length();
                    depth = depthSeg.Length();
                    var reconstructedRect = new Polygon(
                        widthSeg.Start,
                        widthSeg.End,
                        widthSeg.End + depthSeg.Direction() * depth,
                        widthSeg.Start + depthSeg.Direction() * depth
                    );
                    return InstantiateLayoutByFit(configs, width, depth, reconstructedRect, xform);
                }
                catch
                {
                    // largest interior rectangle failed. Just proceed.
                }
                var cinchedPoly = largestTrimmedShape;
                if (largestTrimmedShape.Vertices.Count() > 4)
                {
                    var cinchedVertices = rect.Vertices.Select(v => largestTrimmedShape.Vertices.OrderBy(v2 => v2.DistanceTo(v)).First()).ToList();
                    cinchedPoly = new Polygon(cinchedVertices);
                }
                return InstantiateLayoutByFit(configs, width, depth, cinchedPoly, xform);
            }
            return null;
        }

        protected virtual SpaceConfiguration DeserializeConfigJson(string configJson)
        {
            return JsonConvert.DeserializeObject<SpaceConfiguration>(configJson);
        }

        protected virtual KeyValuePair<string, ContentConfiguration>? FindConfig(double width, double length, SpaceConfiguration configs)
        {
            var orderedConfigs = configs.OrderByDescending(kvp => kvp.Value.CellBoundary.Depth * kvp.Value.CellBoundary.Width);
            KeyValuePair<string, ContentConfiguration>? selectedConfigPair = null;
            foreach (var configPair in orderedConfigs)
            {
                if (configPair.Value.CellBoundary.Width < width && configPair.Value.CellBoundary.Depth < length)
                {
                    selectedConfigPair = configPair;
                    break;
                }
            }

            return selectedConfigPair;
        }

        protected virtual IEnumerable<TLevelElements> GetLevels(Dictionary<string, Model> inputModels, Model spacePlanningZones)
        {
            var levels = spacePlanningZones.AllElementsAssignableFromType<TLevelElements>();
            if (inputModels.TryGetValue("Circulation", out var circModel))
            {
                var circSegments = circModel.AllElementsAssignableFromType<TCirculationSegment>();
                foreach (var cs in circSegments)
                {
                    var matchingLevel = levels.FirstOrDefault(l => l.Level == cs.Level);
                    matchingLevel?.Elements.Add(cs);
                }
            }

            return levels;
        }

        protected virtual int CountSeats(LayoutInstantiated layoutInstantiated)
        {
            return 0;
        }

        private static List<TLevelVolume> GetLevelVolumes(Dictionary<string, Model> inputModels)
        {
            var levelVolumes = new List<TLevelVolume>();
            if (inputModels.TryGetValue("Levels", out var levelsModel))
            {
                levelVolumes.AddRange(levelsModel.AllElementsAssignableFromType<TLevelVolume>());
            }
            if (inputModels.TryGetValue("Conceptual Mass", out var massModel))
            {
                levelVolumes.AddRange(massModel.AllElementsAssignableFromType<TLevelVolume>());
            }
            return levelVolumes;
        }

        private static void SetLevelVolume(ComponentInstance componentInstance, Guid? levelVolumeId)
        {
            if (componentInstance != null)
            {
                foreach (var instance in componentInstance.Instances)
                {
                    if (instance != null)
                    {
                        instance.AdditionalProperties["Level"] = levelVolumeId;
                    }
                }
            }
        }
    }
}