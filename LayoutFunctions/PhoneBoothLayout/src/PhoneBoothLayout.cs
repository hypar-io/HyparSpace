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

namespace PhoneBoothLayout
{
    public static class PhoneBoothLayout
    {
        private static readonly List<ElementProxy<SpaceBoundary>> proxies = new List<ElementProxy<SpaceBoundary>>();

        private static readonly string SpaceBoundaryDependencyName = SpaceSettingsOverride.Dependency;

        /// <summary>
        /// The PhoneBoothLayout function.
        /// </summary>
        /// <param name="model">The input model.</param>
        /// <param name="input">The arguments to the execution.</param>
        /// <returns>A PhoneBoothLayoutOutputs instance containing computed results and the model with any new elements.</returns>
        public static PhoneBoothLayoutOutputs Execute(Dictionary<string, Model> inputModels, PhoneBoothLayoutInputs input)
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
            var output = new PhoneBoothLayoutOutputs();
            var configJson = File.ReadAllText("./PhoneBoothConfigurations.json");
            var configs = JsonConvert.DeserializeObject<SpaceConfiguration>(configJson);
            Configurations.Init(configs);

            var wallMat = new Material("Drywall", new Color(0.9, 0.9, 0.9, 1.0), 0.01, 0.01);
            var glassMat = new Material("Glass", new Color(0.7, 0.7, 0.7, 0.3), 0.3, 0.6);
            var mullionMat = new Material("Storefront Mullions", new Color(0.5, 0.5, 0.5, 1.0));

            var overridesBySpaceBoundaryId = LayoutStrategies.GetOverridesBySpaceBoundaryId<SpaceSettingsOverride, SpaceBoundary, LevelElements>(input.Overrides?.SpaceSettings, (ov) => ov.Identity.ParentCentroid, levels);
            int totalBoothCount = 0;
            foreach (var lvl in levels)
            {
                var corridors = lvl.Elements.OfType<CirculationSegment>();
                var corridorSegments = corridors.SelectMany(p => p.Profile.Segments());
                var meetingRmBoundaries = lvl.Elements.OfType<SpaceBoundary>().Where(z => z.Name == "Phone Booth");
                var levelVolume = levelVolumes.FirstOrDefault(l =>
                    lvl.AdditionalProperties.TryGetValue("LevelVolumeId", out var levelVolumeId) &&
                        levelVolumeId as string == l.Id.ToString()) ??
                        levelVolumes.FirstOrDefault(l => l.Name == lvl.Name);


                var wallCandidateLines = new List<(Line line, string type)>();
                foreach (var room in meetingRmBoundaries)
                {
                    var seatsCount = 0;
                    var config = OverrideUtilities.MatchApplicableOverride(
                        overridesBySpaceBoundaryId,
                        OverrideUtilities.GetSpaceBoundaryProxy(room, meetingRmBoundaries.Proxies(SpaceBoundaryDependencyName)),
                        new SpaceSettingsValue(false, false),
                        proxies);

                    var initialWallCandidates = WallGeneration.FindWallCandidates(room, levelVolume?.Profile, corridorSegments, out var orientationGuideEdge)
                                  .Select(w =>
                                  {
                                      if (w.type == "Glass")
                                      {
                                          w.type = "Glass-Edge";
                                      }
                                      return w;
                                  });
                    var levelInvertedTransform = levelVolume.Transform.Inverted();
                    wallCandidateLines.AddRange(initialWallCandidates.Select(c => (c.line.TransformedLine(levelInvertedTransform), c.type)));

                    var relativeRoomTransform = room.Transform.Concatenated(levelInvertedTransform);
                    var orientationTransform = new Transform(Vector3.Origin, orientationGuideEdge.Direction(), Vector3.ZAxis);
                    orientationTransform.Concatenate(relativeRoomTransform);

                    var boundaryCurves = new List<Polygon>();
                    boundaryCurves.Add(room.Boundary.Perimeter.TransformedPolygon(relativeRoomTransform));
                    boundaryCurves.AddRange(room.Boundary.Voids?.Select(v => v.TransformedPolygon(relativeRoomTransform)) ?? new List<Polygon>());

                    var grid = new Grid2d(boundaryCurves, orientationTransform);
                    grid.U.DivideByApproximateLength(input.MinimumSize, EvenDivisionMode.RoundDown);

                    foreach (var cell in grid.GetCells())
                    {
                        var rect = cell.GetCellGeometry() as Polygon;
                        var segs = rect.Segments();
                        var width = segs[0].Length();
                        var depth = segs[1].Length();
                        var trimmedGeo = cell.GetTrimmedCellGeometry();
                        var (selectedConfigs, configsTransform) = Configurations.GetConfigs(rect.Centroid(), config.Value.PrimaryAxisFlipLayout, config.Value.SecondaryAxisFlipLayout);
                        if (!cell.IsTrimmed() && trimmedGeo.Count() > 0)
                        {
                            var layout = InstantiateLayout(selectedConfigs, width, depth, rect, (levelVolume?.Transform ?? new Transform()).Concatenated(configsTransform));
                            if (layout != null)
                            {
                                LayoutStrategies.SetLevelVolume(layout, levelVolume?.Id);
                                output.Model.AddElement(layout);
                                seatsCount++;
                            }
                        }
                        else if (trimmedGeo.Count() > 0)
                        {
                            var largestTrimmedShape = trimmedGeo.OfType<Polygon>().OrderBy(s => s.Area()).Last();

                            var cinchedVertices = rect.Vertices.Select(v => largestTrimmedShape.Vertices.OrderBy(v2 => v2.DistanceTo(v)).First()).ToList();
                            var cinchedPoly = new Polygon(cinchedVertices);
                            var layout = InstantiateLayout(selectedConfigs, width, depth, cinchedPoly, (levelVolume?.Transform ?? new Transform()).Concatenated(configsTransform));
                            if (layout != null)
                            {
                                LayoutStrategies.SetLevelVolume(layout, levelVolume?.Id);
                                output.Model.AddElement(layout);
                                seatsCount++;
                            }
                        }

                    }
                    wallCandidateLines.AddRange(WallGeneration.PartitionsAndGlazingCandidatesFromGrid(wallCandidateLines, grid, levelVolume?.Profile));
                    
                    totalBoothCount += seatsCount;
                    output.Model.AddElement(new SpaceMetric(room.Id, seatsCount, 0, 0, 0));
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
            output.PhoneBooths = totalBoothCount;
            OverrideUtilities.InstancePositionOverrides(input.Overrides, output.Model);
            output.Model.AddElements(proxies);
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
        private static Random rand = new Random(4);
        private static ComponentInstance InstantiateLayout(SpaceConfiguration configs, double width, double length, Polygon rectangle, Transform xform)
        {
            ContentConfiguration selectedConfig = null;
            var orderedKeys = configs.OrderBy(k => rand.NextDouble()).Select(k => k.Key);
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