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
    public static class LayoutStrategies
    {
        /// <summary>
        /// Instantiate a space by finding the largest space that will fit from a SpaceConfiguration.
        /// </summary>
        /// <param name="configs">The configuration containing all possible space arrangements</param>
        /// <param name="width">The width of the space to fill</param>
        /// <param name="length">The length of the space to fill</param>
        /// <param name="rectangle">The more-or-less rectangular polygon to fill</param>
        /// <param name="xform">A transform to apply to the rectangle.</param>
        /// <returns></returns>
        public static LayoutInstantiated InstantiateLayoutByFit(SpaceConfiguration configs, double width, double length, Polygon rectangle, Transform xform)
        {
            LayoutInstantiated layoutInstantiated = new LayoutInstantiated();
            var orderedKeys = configs.OrderByDescending(kvp => kvp.Value.CellBoundary.Depth * kvp.Value.CellBoundary.Width).Select(kvp => kvp.Key);
            foreach (var key in orderedKeys)
            {
                var config = configs[key];
                if (config.CellBoundary.Width < width && config.CellBoundary.Depth < length)
                {
                    layoutInstantiated.Config = config;
                    layoutInstantiated.ConfigName = key;
                    break;
                }
            }
            if (layoutInstantiated.Config == null)
            {
                return null;
            }
            var baseRectangle = Polygon.Rectangle(layoutInstantiated.Config.CellBoundary.Min, layoutInstantiated.Config.CellBoundary.Max);
            var rules = layoutInstantiated.Config.Rules();

            var componentDefinition = new ComponentDefinition(rules, layoutInstantiated.Config.Anchors());
            layoutInstantiated.Instance = componentDefinition.Instantiate(ContentConfiguration.AnchorsFromRect(rectangle.TransformedPolygon(xform)));
            return layoutInstantiated;
        }

        /// <summary>
        /// Instantiate a space by finding the largest space that will fit a grid cell from a SpaceConfiguration.
        /// </summary>
        /// <param name="configs">The configuration containing all possible space arrangements.</param>
        /// <param name="width">The 2d grid cell to fill.</param>
        /// <param name="xform">A transform to apply to the rectangle.</param>
        /// <returns></returns>
        public static LayoutInstantiated InstantiateLayoutByFit(SpaceConfiguration configs, Grid2d cell, Transform xform)
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

        public static List<TLevelVolume> GetLevelVolumes<TLevelVolume>(Dictionary<string, Model> inputModels) where TLevelVolume : Element
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

        public static void StandardLayoutOnAllLevels<TLevelElements, TLevelVolume, TSpaceBoundary, TCirculationSegment, TOverride, TSpaceSettingsOverrideValueType>(
            string programTypeName,
            Dictionary<string, Model> inputModels,
            dynamic overrides,
            Model outputModel,
            bool createWalls,
            string configurationsPath,
            string catalogPath = "catalog.json",
            Func<LayoutInstantiated, int> countSeats = null,
            Func<TOverride, Vector3> getCentroid = null,
            TSpaceSettingsOverrideValueType defaultValue = default,
            List<ElementProxy<TSpaceBoundary>> proxies = default
            )
            where TLevelElements : Element, ILevelElements
            where TSpaceBoundary : Element, ISpaceBoundary
            where TLevelVolume : GeometricElement, ILevelVolume
            where TCirculationSegment : Floor, ICirculationSegment
            where TOverride : IOverride
            where TSpaceSettingsOverrideValueType : ISpaceSettingsOverrideValue
        {
            ContentCatalogRetrieval.SetCatalogFilePath(catalogPath);
            var spacePlanningZones = inputModels["Space Planning Zones"];
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
            var levelVolumes = GetLevelVolumes<TLevelVolume>(inputModels);
            var configJson = configurationsPath != null ? File.ReadAllText(configurationsPath) : "{}";
            var configs = JsonConvert.DeserializeObject<SpaceConfiguration>(configJson);
            FlippedConfigurations.Init(configs);

            var allSpaceBoundaries = spacePlanningZones.AllElementsAssignableFromType<TSpaceBoundary>().Where(z => z.Name == programTypeName).ToList();
            var overridesBySpaceBoundaryId = 
                getCentroid != null ? 
                OverrideUtilities.GetOverridesBySpaceBoundaryId<TOverride, ISpaceBoundary, ILevelElements>(overrides?.SpaceSettings, getCentroid, levels) : 
                new Dictionary<Guid, IOverride>();
                
            foreach (var lvl in levels)
            {
                var corridors = lvl.Elements.Where(e => e is Floor).OfType<Floor>();
                var corridorSegments = corridors.SelectMany(p => p.Profile.Segments());
                var roomBoundaries = lvl.Elements.OfType<TSpaceBoundary>().Where(z => z.Name == programTypeName);
                foreach (var rm in roomBoundaries)
                {
                    allSpaceBoundaries.Remove(rm);
                }
                var levelVolume = levelVolumes.FirstOrDefault(l =>
                    lvl.AdditionalProperties.TryGetValue("LevelVolumeId", out var levelVolumeId) &&
                        levelVolumeId as string == l.Id.ToString()) ??
                        levelVolumes.FirstOrDefault(l => l.Name == lvl.Name);
                var wallCandidateLines = new List<(Line line, string type)>();
                foreach (var room in roomBoundaries)
                {
                    var spaceSettingsValue = 
                        defaultValue != null && proxies != null ? 
                        OverrideUtilities.MatchApplicableOverride(
                            overridesBySpaceBoundaryId,
                            OverrideUtilities.GetSpaceBoundaryProxy(room, roomBoundaries.Proxies(OverrideUtilities.SpaceBoundaryOverrideDependencyName)),
                            defaultValue,
                            proxies).Value : 
                        default;
                    ProcessRoom<TLevelVolume, TSpaceBoundary, TSpaceSettingsOverrideValueType>(room, outputModel, countSeats, configs, spaceSettingsValue, corridorSegments, levelVolume, wallCandidateLines);
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
            foreach (var room in allSpaceBoundaries)
            {
                var spaceSettingsValue = 
                    defaultValue != null && proxies != null ? 
                    OverrideUtilities.MatchApplicableOverride(
                        overridesBySpaceBoundaryId,
                        OverrideUtilities.GetSpaceBoundaryProxy(room, allSpaceBoundaries.Proxies(OverrideUtilities.SpaceBoundaryOverrideDependencyName)),
                        defaultValue,
                        proxies).Value : 
                    default;
                ProcessRoom<TLevelVolume, TSpaceBoundary, TSpaceSettingsOverrideValueType>(room, outputModel, countSeats, configs, spaceSettingsValue);
            }
            OverrideUtilities.InstancePositionOverrides(overrides, outputModel);
        }

        private static void ProcessRoom<TLevelVolume, TSpaceBoundary, TSpaceSettingsOverrideValueType>(
                TSpaceBoundary room,
                Model outputModel,
                Func<LayoutInstantiated, int> countSeats,
                SpaceConfiguration configs,
                TSpaceSettingsOverrideValueType spaceSettingsValue,
                IEnumerable<Line> corridorSegments = null,
                TLevelVolume levelVolume = null,
                List<(Line line, string type)> wallCandidateLines = null
            )
            where TLevelVolume : GeometricElement, ILevelVolume
            where TSpaceBoundary : Element, ISpaceBoundary
            where TSpaceSettingsOverrideValueType : ISpaceSettingsOverrideValue
        {
            corridorSegments ??= Enumerable.Empty<Line>();
            wallCandidateLines ??= new List<(Line line, string type)>();
            var seatsCount = 0;
            var success = false;
            var spaceBoundary = room.Boundary;
            var wallCandidateOptions = WallGeneration.FindWallCandidateOptions(room, levelVolume?.Profile, corridorSegments);
            var selectedConfigs = spaceSettingsValue != null && spaceSettingsValue is ISpaceSettingsOverrideFlipValue spaceSettingsFlipValue ?
                FlippedConfigurations.GetConfigs(spaceSettingsFlipValue.PrimaryAxisFlipLayout, spaceSettingsFlipValue.SecondaryAxisFlipLayout) :
                configs;

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
                    var layout = InstantiateLayoutByFit(selectedConfigs, cell, room.Transform);
                    if (layout != null)
                    {
                        success = true;
                        SetLevelVolume(layout.Instance, levelVolume?.Id);

                        wallCandidateLines.AddRange(WallCandidates);
                        outputModel.AddElement(layout.Instance);

                        if (countSeats != null)
                        {
                            seatsCount += countSeats(layout);
                        }
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

            if (countSeats != null)
            {
                outputModel.AddElement(new SpaceMetric(room.Id, seatsCount, 0, 0, 0));
            }
        }

        public static SearchablePointCollection<Profile> GetColumnProfiles(ColumnAvoidanceStrategy avoidanceStrategy, Dictionary<string, Model> inputModels)
        {
            // Get column locations from model
            List<(Vector3, Profile)> modelColumnLocations = new List<(Vector3, Profile)>();

            if (avoidanceStrategy != ColumnAvoidanceStrategy.None)
            {
                foreach (var source in _columnSources)
                {
                    if (inputModels.ContainsKey(source))
                    {
                        var sourceData = inputModels[source];
                        modelColumnLocations.AddRange(GetColumnLocations(sourceData));
                    }
                }
            }
            return new SearchablePointCollection<Profile>(modelColumnLocations);
        }

        public static void DeskLayoutOnAllLevels<TLevelElements, TSpaceBoundary>(ColumnAvoidanceStrategy avoidanceStrat)
        {

        }

        private static readonly string[] _columnSources = new[] { "Columns", "Structure" };

        public static IEnumerable<(Vector3, Profile)> GetColumnLocations(Model m)
        {
            if (m == null) { throw new Exception("Model provided was null."); }
            foreach (var ge in m.AllElementsOfType<Column>())
            {
                if (!ge.IsElementDefinition)
                {
                    yield return (ge.Location, ge.Profile.Transformed(new Transform(ge.Location)));
                }
                else
                {
                    Vector3 geOrigin = ge.Location;
                    foreach (var e in m.AllElementsOfType<ElementInstance>().Where(e => e.BaseDefinition == ge))
                    {
                        yield return (e.Transform.OfPoint(geOrigin), ge.Profile.Transformed(e.Transform));
                    }
                }
            }
            yield break;
        }

        public static Transform GetOrientationTransform(Profile spaceBoundary, IEnumerable<Line> corridorSegments, double rotation)
        {
            Line orientationGuideEdge = WallGeneration.FindEdgeAdjacentToSegments(spaceBoundary.Perimeter.Segments(), corridorSegments, out _);
            var dir = orientationGuideEdge.Direction();
            if (rotation != 0)
            {
                var gridRotation = new Transform();
                gridRotation.Rotate(Vector3.ZAxis, rotation);
                dir = gridRotation.OfVector(dir);
            }
            return new Transform(Vector3.Origin, dir, Vector3.ZAxis);
        }

        public static List<Grid2d> GetValidGrids(
            Profile spaceBoundary,
            Transform orientationTransform,
            SearchablePointCollection<Profile> columnSearchTree,
            string avoidanceStrategyName)
        {

            var boundaryCurves = new List<Polygon>();
            boundaryCurves.Add(spaceBoundary.Perimeter);
            boundaryCurves.AddRange(spaceBoundary.Voids ?? new List<Polygon>());
            Grid2d mainGrid;
            try
            {
                mainGrid = new Grid2d(boundaryCurves, orientationTransform);
            }
            catch
            {
                Console.WriteLine("Something went wrong creating a grid.");
                return new List<Grid2d>();
            }

            var validGrids = new List<Grid2d>() { mainGrid };

            if (columnSearchTree.Count > 0 && avoidanceStrategyName == "Adaptive Grid")
            {
                // Split grid by column locations
                double columnMaxWidth = 0;
                foreach (var p in columnSearchTree.FindWithinBounds(spaceBoundary.Perimeter.Bounds(), 0, 2))
                {
                    bool contains = spaceBoundary.Perimeter.Contains(p);
                    if (!contains) { continue; }
                    var columnProfile = columnSearchTree.GetElementsAtPoint(p).First();
                    var profileBounds = columnProfile.Perimeter.Bounds();
                    var flattenedMin = new Vector3(profileBounds.Min.X, profileBounds.Min.Y, 0);
                    var flattenedMax = new Vector3(profileBounds.Max.X, profileBounds.Max.Y, 0);
                    columnMaxWidth = Math.Max(columnMaxWidth, flattenedMin.DistanceTo(flattenedMax));
                    mainGrid.U.SplitAtPoints(new[] { flattenedMin, flattenedMax });
                }
                // Extract valid cells
                // Add tolerance to max width
                columnMaxWidth *= 1.05;

                validGrids =
                    mainGrid.Cells.SelectMany(
                        cl => cl.Where(
                            c => c.U.Domain.Length > columnMaxWidth &&
                                c.V.Domain.Length > columnMaxWidth)
                                ).ToList();

            }
            return validGrids;
        }

        public static (List<Element> desks, int count, List<Profile> collabProfiles) LayoutDesksInGrid(
            Grid2d grid,
            GeometricElement spaceBoundary,
            ContentConfiguration selectedConfig,
            double aisleWidth,
            double backToBackWidth,
            IEnumerable<string> doubleDeskTypes,
            Dictionary<string, int> desksPerConfig,
            string deskTypeName,
            double collabDensity,
            string avoidanceStrategy,
            SearchablePointCollection<Profile> columns,
            Transform orientationTransform,
            GeometricElement customDesk = null)
        {
            List<Element> desks = new List<Element>();
            int deskCount = 0;

            // divide a fake grid to see how many desks we can fit — we'll use the count to rejigger the aisle locations.
            var vDim = grid.V.Domain.Length;
            var tempGrid = new Grid1d(vDim);
            tempGrid.DivideByPattern(
                new[] {
                                ("Desk", selectedConfig.Width),
                                ("Desk", selectedConfig.Width),
                                ("Desk", selectedConfig.Width),
                                ("Desk", selectedConfig.Width),
                                ("Aisle", aisleWidth)
            }, PatternMode.Cycle,
            FixedDivisionMode.RemainderAtBothEnds);
            var numDesks = tempGrid.Cells.Count(c => c.Type == "Desk");

            var pattern = new[] {
                                ("Desk", selectedConfig.Width),
                                ("Desk", selectedConfig.Width),
                                ("Desk", selectedConfig.Width),
                                ("Desk", selectedConfig.Width),
                                ("Aisle", aisleWidth)
            };
            // don't leave a stray column of few desks at the end. 5 desks = 3/2, 6 desks = 3/3, 7 desks = 4/3
            if (numDesks > 4 && numDesks < 7)
            {
                pattern = new[] {
                                ("Desk", selectedConfig.Width),
                                ("Desk", selectedConfig.Width),
                                ("Desk", selectedConfig.Width),
                                ("Aisle", aisleWidth)
                };
            }

            // Divide by pattern
            grid.V.DivideByPattern(
                pattern,
                PatternMode.Cycle,
                FixedDivisionMode.RemainderAtBothEnds);

            var mainVPattern = new[] {
                                ("Aisle", backToBackWidth),
                                ("Forward", selectedConfig.Depth),
                                ("Backward", selectedConfig.Depth)
                            };

            var nonMirroredVPattern = new[] {
                                ("Forward", selectedConfig.Depth),
                                ("Aisle", backToBackWidth)
                            };

            var chosenDeskAislePattern = doubleDeskTypes.Contains(deskTypeName) ? nonMirroredVPattern : mainVPattern;

            grid.U.DivideByPattern(
                chosenDeskAislePattern,
                PatternMode.Cycle,
                FixedDivisionMode.RemainderAtBothEnds);

            // Insert interstitial collab spaces
            if (collabDensity > 0.0)
            {
                var spaceEveryURows = 4;
                var numDesksToConsume = 1;

                if (collabDensity >= 0.3)
                {
                    spaceEveryURows = 3;
                }
                if (collabDensity >= 0.5)
                {
                    numDesksToConsume = 2;
                }
                if (collabDensity >= 0.7)
                {
                    spaceEveryURows = 2;
                }
                if (collabDensity >= 0.8)
                {
                    numDesksToConsume = 3;
                }
                if (collabDensity >= 0.9)
                {
                    numDesksToConsume = 4;
                }

                var colCounter = 0;
                for (int j = 0; j < grid.V.Cells.Count; j++)
                {
                    var colType = grid.V[j].Type;
                    var rowCounter = 0;
                    for (int i = 0; i < grid.U.Cells.Count; i++)
                    {
                        var rowType = grid.U[i].Type;
                        var cell = grid[i, j];
                        if (
                            rowCounter % spaceEveryURows == 0 &&
                            (rowType == "Forward" || rowType == "Backward") &&
                            colType == "Desk" &&
                            colCounter < numDesksToConsume
                            )
                        {
                            cell.Type = "Collab Space";
                        }
                        if (rowType == "Aisle")
                        {
                            rowCounter++;
                        }
                    }
                    if (colType == "Desk")
                    {
                        colCounter++;
                    }
                    else if (colType == "Aisle")
                    {
                        colCounter = 0;
                    }
                }
            }

            var placedDesksByUV = new SortedDictionary<int, SortedDictionary<int, List<ElementInstance>>>();

            for (int u = 0; u < grid.U.Cells.Count; u++)
            {
                placedDesksByUV.Add(u, new SortedDictionary<int, List<ElementInstance>>());
                for (int v = 0; v < grid.V.Cells.Count; v++)
                {
                    var cell = grid[u, v];
                    try
                    {
                        if ((cell.Type?.Contains("Desk") ?? true) && !isTrimmed(cell))
                        {
                            var cellGeo = cell.GetCellGeometry() as Polygon;
                            var cellBounds = cellGeo.Bounds();

                            if (avoidanceStrategy == "Cull")
                            {
                                // Get closest columns from cell location
                                var nearbyColumns = columns.FindWithinBounds(cellBounds, 0.3, 2).ToList();
                                var columnProfilesCollection =
                                    nearbyColumns.Select(c => columns.GetElementsAtPoint(c))
                                                .Select(e => e.FirstOrDefault());
                                if (
                                    nearbyColumns.Any(
                                        c => cellGeo.Contains(c)) ||
                                        columnProfilesCollection.Any(cp => cp != null && cp.Perimeter.Intersects(cellGeo)))
                                {
                                    continue;
                                }
                            }

                            if ((cell.Type?.Contains("Backward") ?? false) || (cell.Type?.Contains("Forward") ?? false))
                            {
                                var transform = (cell.Type.Contains("Backward")
                                ?
                                // Backward
                                orientationTransform
                                    .Concatenated(new Transform(Vector3.Origin, -90))
                                    .Concatenated(new Transform(cellGeo.Vertices[3]))
                                    .Concatenated(spaceBoundary.Transform)
                                :
                                // Forward
                                orientationTransform
                                    .Concatenated(new Transform(Vector3.Origin, 90))
                                    .Concatenated(new Transform(cellGeo.Vertices[1]))
                                    .Concatenated(spaceBoundary.Transform));

                                var deskElements = customDesk != null
                                ?
                                    new List<ElementInstance>() { customDesk.CreateInstance(transform, null) }
                                    :
                                    selectedConfig.Instantiate(transform);

                                desks.AddRange(deskElements);
                                placedDesksByUV[u].Add(v, deskElements);
                                var countPerDesk = 1;
                                if (desksPerConfig.ContainsKey(deskTypeName))
                                {
                                    countPerDesk = desksPerConfig[deskTypeName];
                                }
                                deskCount += countPerDesk;
                                cell.Type += $"({u}, {v})";
                            }
                            else
                            {
                                cell.Type = "NO DIRECTION";
                                Console.WriteLine(
                                    "Desk placement was skipped because a desk " +
                                    "direction wasn't provided for this cell.");
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.Message);
                        Console.WriteLine(e.StackTrace);
                    }
                }
            }

            var collabSpaceCells = grid.GetCells()
                .Where(c => !c.IsTrimmed() && c.Type?.Contains("Collab Space") == true)
                .Select(c => new Profile(c.GetCellGeometry() as Polygon));

            var collabProfiles = Profile.UnionAll(collabSpaceCells);
            // foreach (var profile in union)
            // {
            //     var sb = SpaceBoundary.Make(profile, "Open Collaboration", spaceBoundary.Transform.Concatenated(new Transform(0, 0, -0.03)), 3, profile.Perimeter.Centroid(), profile.Perimeter.Centroid());
            //     sb.Representation = new Representation(new[] { new Lamina(profile.Perimeter, false) });
            //     sb.AdditionalProperties.Add("Parent Level Id", lvl.Id);
            //     output.Model.AddElement(sb);
            // }
            return (desks, deskCount, collabProfiles);
        }

        private static bool isTrimmed(Grid2d cell)
        {
            var geo = cell.GetTrimmedCellGeometry().OfType<Polygon>();
            if (geo.Count() != 1)
            {
                return true;
            }
            if (Math.Abs(geo.First().Area() - (cell.GetCellGeometry() as Polygon).Area()) < 0.01)
            {
                return false;
            }
            return true;
        }

        public static void SetLevelVolume(ElementInstance elementInstance, Guid? levelVolumeId)
        {
            if (elementInstance != null)
            {
                elementInstance.AdditionalProperties["Level"] = levelVolumeId;
            }
        }

        public static void SetLevelVolume(ComponentInstance componentInstance, Guid? levelVolumeId)
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