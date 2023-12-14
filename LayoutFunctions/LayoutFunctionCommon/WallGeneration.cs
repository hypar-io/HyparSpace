using System;
using System.Collections.Generic;
using System.Linq;
using Elements;
using Elements.Geometry;
using Elements.Spatial;

namespace LayoutFunctionCommon
{
    public static class WallGeneration
    {
        private const string wallCandidatePropertyName = "Wall Candidate";
        private static double mullionSize = 0.07;
        private static double doorWidth = 0.9;
        private static double doorHeight = 2.1;
        private static double doorThickness = 4 * 0.0254;
        private static double sideLightWidth = 0.4;

        private static Dictionary<string, int> interiorPartitionTypePriority = new Dictionary<string, int>()
        {
            {"Solid", 3},
            {"Partition", 2},
            {"Glass", 1}
        };

        private static Material wallMat = new Material("Drywall", new Color(0.9, 0.9, 0.9, 1.0), 0.01, 0.01);
        private static Material glassMat = new Material("Glass", new Color(0.7, 0.7, 0.7, 0.3), 0.3, 0.6);
        private static Material mullionMat = new Material("Storefront Mullions", new Color(0.5, 0.5, 0.5, 1.0));
        public static List<RoomEdge> FindWallCandidates(ISpaceBoundary room, Profile levelProfile, IEnumerable<Line> corridorSegments, out RoomEdge orientationGuideEdge, IEnumerable<string> wallTypeFilter = null)
        {
            var spaceBoundary = room.Boundary;
            var wallCandidateLines = new List<RoomEdge>();
            var thicknesses = spaceBoundary.GetEdgeThickness();
            orientationGuideEdge = FindPrimaryAccessEdge(spaceBoundary.Perimeter.Segments().Select((s, i) => new RoomEdge
            {
                Line = s.TransformedLine(room.Transform),
                Thickness = thicknesses?.ElementAtOrDefault(i)
            }), corridorSegments, levelProfile, out var wallCandidates);
            orientationGuideEdge.Type = room.DefaultWallType ?? "Glass";
            orientationGuideEdge.PrimaryEntryEdge = true;
            wallCandidateLines.Add(orientationGuideEdge);
            if (levelProfile != null)
            {
                var exteriorWalls = FindAllEdgesAdjacentToSegments(wallCandidates, levelProfile.Segments(), out var notAdjacentToFloorBoundary, out var corridorEdges);
                wallCandidateLines.AddRange(notAdjacentToFloorBoundary.Select(s =>
                {
                    s.Type = "Solid";
                    return s;
                }));
            }
            else
            {
                // if no level or floor is present, everything that's not glass is solid.
                wallCandidateLines.AddRange(wallCandidates.Select(s =>
                {
                    s.Type = "Solid";
                    return s;
                }));
            }
            if (wallTypeFilter != null)
            {
                return wallCandidateLines.Where((w) => wallTypeFilter.Contains(w.Type)).ToList();
            }
            return wallCandidateLines;
        }

        public static List<(RoomEdge OrientationGuideEdge, List<RoomEdge> WallCandidates)> FindWallCandidateOptions(ISpaceBoundary room, Profile levelProfile, IEnumerable<Line> corridorSegments, IEnumerable<string> wallTypeFilter = null)
        {
            var wallCandidateOptions = new List<(RoomEdge OrientationGuideEdge, List<RoomEdge> WallCandidates)>();
            var thicknesses = room.Boundary.GetEdgeThickness();
            var allSegments = room.Boundary.Perimeter.Segments().Select((s, i) => new RoomEdge
            {
                Line = s.TransformedLine(room.Transform),
                Thickness = thicknesses?.ElementAtOrDefault(i)
            }).ToList();
            var orientationGuideEdges = SortEdgesByPrimaryAccess(allSegments, corridorSegments, levelProfile, 0.3);
            foreach (var orientationGuideEdge in orientationGuideEdges)
            {
                orientationGuideEdge.Line.Type = room.DefaultWallType ?? "Glass";
                orientationGuideEdge.Line.PrimaryEntryEdge = true;
                var wallCandidateLines = new List<RoomEdge>
                {
                    orientationGuideEdge.Line
                };
                if (levelProfile != null)
                {
                    var exteriorWalls = FindAllEdgesAdjacentToSegments(orientationGuideEdge.OtherSegments, levelProfile.Segments(), out var notAdjacentToFloorBoundary, out var corridorEdges);
                    wallCandidateLines.AddRange(notAdjacentToFloorBoundary.Select(s =>
                    {
                        if (!orientationGuideEdges.Select(x => x.Line).Contains(s))
                        {
                            s.Type = "Solid";
                        }
                        return s;
                    }));
                }
                else
                {
                    // if no level or floor is present, everything that's not glass is solid.
                    wallCandidateLines.AddRange(orientationGuideEdge.OtherSegments.Select(s =>
                    {
                        if (!orientationGuideEdges.Select(x => x.Line).Contains(s))
                        {
                            s.Type = "Solid";
                        }
                        return s;
                    }));
                }
                wallCandidateOptions.Add((orientationGuideEdge.Line, (wallTypeFilter != null ? wallCandidateLines.Where((w) => wallTypeFilter.Contains(w.Type)).ToList() : wallCandidateLines)));
            }

            return wallCandidateOptions;
        }

        public static List<RoomEdge> DeduplicateWallLines(List<InteriorPartitionCandidate> interiorPartitionCandidates)
        {
            return interiorPartitionCandidates.SelectMany(i => i.WallCandidateLines).Where(l => l.Type != null && interiorPartitionTypePriority.Keys.Contains(l.Type)).ToList();
            var resultCandidates = new List<RoomEdge>();
            var typedLines = interiorPartitionCandidates.SelectMany(c => c.WallCandidateLines)
                            .Where(l => l.Type != null && interiorPartitionTypePriority.Keys.Contains(l.Type));
            var collinearLinesGroups = GroupCollinearLines(typedLines);

            foreach (var collinearLinesGroup in collinearLinesGroups)
            {
                if (collinearLinesGroup.Value.Count == 1)
                {
                    resultCandidates.Add(collinearLinesGroup.Value.First());
                    continue;
                }
                var linesOrderedByLength = collinearLinesGroup.Value.OrderByDescending(v => v.Line.Length());
                var dominantLineForGroup = linesOrderedByLength.First();
                var domLineDir = dominantLineForGroup.Direction;

                var orderEnds = new List<(double pos, bool isEnd, string type)>();
                foreach (var linePair in collinearLinesGroup.Value)
                {
                    var line = linePair.Line;
                    var start = (line.Start - dominantLineForGroup.Line.Start).Dot(domLineDir);
                    var end = (line.End - dominantLineForGroup.Line.Start).Dot(domLineDir);
                    if (start > end)
                    {
                        var oldStart = start;
                        start = end;
                        end = oldStart;
                    }

                    orderEnds.Add((start, false, linePair.Type));
                    orderEnds.Add((end, true, linePair.Type));
                }

                var totalCount = 0;
                (double pos, bool isEnd, string type) segmentStart = default;
                var endsOrdered = orderEnds.OrderBy(e => e.pos).ThenBy(e => e.isEnd);
                var typePointsCounts = new Dictionary<string, int>();
                foreach (var point in endsOrdered)
                {
                    var prevCount = totalCount;
                    typePointsCounts.TryGetValue(point.type, out var typeCount);
                    var delta = point.isEnd ? -1 : 1;
                    totalCount += delta;
                    typeCount += delta;
                    typePointsCounts[point.type] = typeCount;
                    if (totalCount == 1 && prevCount == 0) // begin segment
                    {
                        segmentStart = point;
                    }
                    else if (totalCount == 0) // end segment
                    {
                        AddWallCandidateLine(resultCandidates, dominantLineForGroup, domLineDir, segmentStart.pos, point.pos, point.type);
                    }
                    else if (segmentStart.type.Equals(point.type))
                    {
                        if (typePointsCounts[segmentStart.type] == 0) // end segment with current type
                        {
                            AddWallCandidateLine(resultCandidates, dominantLineForGroup, domLineDir, segmentStart.pos, point.pos, point.type);
                            var nextType = typePointsCounts
                                .FirstOrDefault(t => interiorPartitionTypePriority[t.Key] < interiorPartitionTypePriority[point.type]
                                            && t.Value > 0);
                            if (nextType.Key != null) // start segment with lower priority if it exists
                            {
                                segmentStart = (point.pos, false, nextType.Key);
                            }
                        }
                    }
                    else
                    {
                        // new type with higher priority starts
                        if (interiorPartitionTypePriority[point.type] > interiorPartitionTypePriority[segmentStart.type])
                        {
                            AddWallCandidateLine(resultCandidates, dominantLineForGroup, domLineDir, segmentStart.pos, point.pos, segmentStart.type);
                            segmentStart = point;
                        }
                    }
                }
            }

            return resultCandidates;
        }

        private static Dictionary<Line, List<RoomEdge>> GroupCollinearLines(IEnumerable<RoomEdge> typedLines, double tolerance = Vector3.EPSILON)
        {
            var collinearLinesGroups = new Dictionary<Line, List<RoomEdge>>();
            foreach (var typedLine in typedLines)
            {
                var isLineAdded = false;
                foreach (var linesGroup in collinearLinesGroups)
                {
                    if (typedLine.Line.IsCollinear(linesGroup.Key, tolerance))
                    {
                        linesGroup.Value.Add(typedLine);
                        isLineAdded = true;
                        break;
                    }
                }
                if (!isLineAdded)
                {
                    collinearLinesGroups.Add(typedLine.Line, new List<RoomEdge>() { typedLine });
                }
            }

            return collinearLinesGroups;
        }

        private static void AddWallCandidateLine(List<RoomEdge> resultCandidates, RoomEdge dominantLineForGroup, Vector3 domLineDir, double startPos, double endPos, string type)
        {
            var startPt = startPos * domLineDir + dominantLineForGroup.Line.Start;
            var endPt = endPos * domLineDir + dominantLineForGroup.Line.Start;

            if (startPt.DistanceTo(endPt) > 0.01)
            {
                var newLine = new Line(startPt, endPt);
                resultCandidates.Add(new RoomEdge
                {
                    Line = newLine,
                    Thickness = dominantLineForGroup.Thickness,
                    Type = type,
                    PrimaryEntryEdge = dominantLineForGroup.PrimaryEntryEdge
                });
            }
        }

        public static List<RoomEdge> SplitOverlappingWallCandidates(IEnumerable<RoomEdge> wallCandidateLines,
                                                                    IEnumerable<RoomEdge> prioritizedWallCandidateLines,
                                                                    double tolerance = 0.01)
        {
            var resultCandidates = new List<RoomEdge>();
            var typedLines = wallCandidateLines.Where(l => interiorPartitionTypePriority.ContainsKey(l.Type));
            var prioritizedTypedLines = prioritizedWallCandidateLines.Where(l => interiorPartitionTypePriority.ContainsKey(l.Type));
            var allTypedLines = new List<RoomEdge>();
            allTypedLines.AddRange(typedLines.Select(l => new RoomEdge()
            {
                Line = l.Line,
                Type = $"{l.Type}-0",
                Thickness = l.Thickness,
                PrimaryEntryEdge = l.PrimaryEntryEdge
            }));
            allTypedLines.AddRange(prioritizedTypedLines.Select(l => new RoomEdge()
            {
                Line = l.Line,
                Type = $"{l.Type}-1",
                Thickness = l.Thickness,
                PrimaryEntryEdge = l.PrimaryEntryEdge
            }));
            var collinearLinesGroups = GroupCollinearLines(allTypedLines, tolerance);

            foreach (var collinearLinesGroup in collinearLinesGroups)
            {
                if (collinearLinesGroup.Value.Count == 1)
                {
                    var candidate = collinearLinesGroup.Value.First();
                    candidate.Type = candidate.Type.Split('-').First();
                    resultCandidates.Add(candidate);
                    continue;
                }
                var linesOrderedByLength = collinearLinesGroup.Value.Where(x => !x.Item2.Contains("Partition")).OrderByDescending(v => v.Line.Length());

                var dominantLineForGroup = linesOrderedByLength.FirstOrDefault(x => x.PrimaryEntryEdge == true);

                if (dominantLineForGroup == null)
                {
                    dominantLineForGroup = linesOrderedByLength.First();
                }

                var domLineDir = dominantLineForGroup.Line.Direction();

                var orderEnds = new List<(double pos, bool isEnd, string type, int priority)>();
                foreach (var linePair in collinearLinesGroup.Value)
                {
                    var line = linePair.Line;
                    var start = (line.Start - dominantLineForGroup.Line.Start).Dot(domLineDir);
                    var end = (line.End - dominantLineForGroup.Line.Start).Dot(domLineDir);
                    if (start > end)
                    {
                        (end, start) = (start, end);
                    }

                    var typePriorityStrings = linePair.Type.Split('-');
                    orderEnds.Add((start, false, typePriorityStrings[0], int.Parse(typePriorityStrings[1])));
                    orderEnds.Add((end, true, typePriorityStrings[0], int.Parse(typePriorityStrings[1])));
                }

                var totalCount = 0;
                (double pos, bool isEnd, string type, int priority) segmentStart = default;
                var endsOrdered = orderEnds.OrderBy(e => e.pos).ThenBy(e => !e.isEnd);
                var typePointsCounts = new Dictionary<(string type, int priority), int>();
                foreach (var point in endsOrdered)
                {
                    var prevCount = totalCount;
                    typePointsCounts.TryGetValue((point.type, point.priority), out var typeCount);
                    var delta = point.isEnd ? -1 : 1;
                    totalCount += delta;
                    typeCount += delta;
                    typePointsCounts[(point.type, point.priority)] = typeCount;
                    if (totalCount == 1 && prevCount == 0) // begin segment
                    {
                        segmentStart = point;
                    }
                    else if (totalCount == 0) // end segment
                    {
                        AddWallCandidateLine(resultCandidates, dominantLineForGroup, domLineDir, segmentStart.pos, point.pos, point.type);
                    }
                    else
                    {
                        AddWallCandidateLine(resultCandidates, dominantLineForGroup, domLineDir, segmentStart.pos, point.pos, segmentStart.type);

                        if (segmentStart.priority < point.priority)
                        {
                            segmentStart = point;
                        }
                        else if (segmentStart.priority > point.priority)
                        {
                            segmentStart = (point.pos, false, segmentStart.type, segmentStart.priority);
                        }
                        else
                        {
                            var nextType = typePointsCounts
                                .OrderByDescending(t => t.Key.priority)
                                .ThenByDescending(t => interiorPartitionTypePriority[t.Key.type])
                                .FirstOrDefault(t => (t.Key.priority == point.priority
                                                      && interiorPartitionTypePriority[t.Key.type] < interiorPartitionTypePriority[point.type]
                                                      || t.Key.priority < point.priority)
                                                     && t.Value > 0);
                            if (nextType.Key.type != null) // start segment with lower priority if it exists
                            {
                                segmentStart = (point.pos, false, nextType.Key.type, nextType.Key.priority);
                            }
                            else
                            {
                                segmentStart = (point.pos, false, segmentStart.type, segmentStart.priority);
                            }
                        }
                    }
                }
            }

            return resultCandidates;
        }

        private static double CalculateTotalStorefrontHeight(double volumeHeight)
        {
            return Math.Min(2.7, volumeHeight);
        }

        private static GeometricElement CreateMullion(double height)
        {
            var totalStorefrontHeight = CalculateTotalStorefrontHeight(height);
            var mullion = new Mullion
            {
                BaseLine = new Line(new Vector3(-mullionSize / 2, 0, 0), new Vector3(mullionSize / 2, 0, 0)),
                Width = mullionSize,
                Height = totalStorefrontHeight,
                Material = mullionMat,
                IsElementDefinition = true
            };
            return mullion;
        }

        public static List<Element> GenerateWalls(IEnumerable<(Line line, string type, Guid elementId, (double innerWidth, double outerWidth)? Thickness)> wallCandidateLines, double height, Transform levelTransform, bool debugMode = false)
        {
            var elements = new List<Element>();

            if (debugMode)
            {
                foreach (var wallCandidate in wallCandidateLines)
                {
                    Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(wallCandidate.line));
                    var lineProjected = wallCandidate.line.TransformedLine(new Transform(0, 0, -wallCandidate.line.End.Z));
                    switch (wallCandidate.type)
                    {
                        case "Solid":
                            elements.Add(new StandardWall(lineProjected, 0.2, height, BuiltInMaterials.ZAxis, levelTransform));
                            break;
                        case "Glass":
                            elements.Add(new StandardWall(lineProjected, 0.2, height, BuiltInMaterials.XAxis, levelTransform));
                            break;
                        case "Partition":
                            elements.Add(new StandardWall(lineProjected, 0.2, height, BuiltInMaterials.YAxis, levelTransform));
                            break;
                    }
                }

                return elements;
            }
            var totalStorefrontHeight = CalculateTotalStorefrontHeight(height);

            var mullion = CreateMullion(height);

            foreach (var (line, type, wallCandidateId, thickness) in wallCandidateLines)
            {
                var lineProjected = line.TransformedLine(new Transform(0, 0, -line.End.Z));
                if (thickness != null && thickness.Value.innerWidth == 0 && thickness.Value.outerWidth == 0)
                {
                    continue;
                }
                var thicknessOrDefault = thickness ?? (type == "Solid" ? (0.1, 0.1) : (0.05, 0.05));
                var sumThickness = thicknessOrDefault.innerWidth + thicknessOrDefault.outerWidth;
                // the line we supply for the wall creation is always a
                // centerline. If the left thickness doesn't equal the right
                // thickness, we have to offset the centerline by their
                // difference.
                var offset = (thicknessOrDefault.outerWidth - thicknessOrDefault.innerWidth) / 2.0;
                lineProjected = lineProjected.Offset(offset, false);
                if (sumThickness < 0.01)
                {
                    sumThickness = 0.2;
                }

                if (type == "Solid")
                {
                    var wall = new StandardWall(lineProjected, sumThickness, height, wallMat, levelTransform);
                    wall.AdditionalProperties[wallCandidatePropertyName] = wallCandidateId;

                    if (wall.CenterLine.Length() > doorWidth + 2 * sideLightWidth)
                    {
                        double tPos = (sideLightWidth + doorWidth / 2) / wall.CenterLine.Length();
                        // Adding Wall as a property to door makes the wall not show up :shrug:
                        var door = new Door(wall.CenterLine, tPos, doorWidth, doorHeight, doorThickness, DoorOpeningSide.LeftHand, DoorOpeningType.SingleSwing)
                        {
                            Material = BuiltInMaterials.Concrete
                        };
                        door.Transform.Concatenate(new Transform(0, 0, 0.005));
                        wall.AddDoorOpening(door);

                        elements.Add(door);
                    }

                    elements.Add(wall);
                }
                else if (type == "Partition")
                {
                    var wall = new StandardWall(lineProjected, sumThickness, height, wallMat, levelTransform);
                    wall.AdditionalProperties[wallCandidatePropertyName] = wallCandidateId;
                    elements.Add(wall);
                }
                else if (type == "Glass")
                {
                    var primaryWall = new StorefrontWall(lineProjected, 0.05, height, glassMat, levelTransform);
                    primaryWall.AdditionalProperties[wallCandidatePropertyName] = wallCandidateId;
                    elements.Add(primaryWall);
                    var grid = new Grid1d(lineProjected);
                    var offsets = new[] { sideLightWidth, sideLightWidth + doorWidth }.Where(o => grid.Domain.Min + o < grid.Domain.Max);

                    if (primaryWall.CenterLine.Length() > doorWidth + 2 * sideLightWidth)
                    {
                        double tPos = (sideLightWidth + doorWidth / 2) / primaryWall.CenterLine.Length();
                        var door = new Door(primaryWall.CenterLine, tPos, doorWidth, doorHeight, doorThickness, DoorOpeningSide.LeftHand, DoorOpeningType.SingleSwing)
                        {
                            Material = BuiltInMaterials.Concrete
                        };

                        primaryWall.AddDoorOpening(door);
                        elements.Add(door);
                    }

                    grid.SplitAtOffsets(offsets);
                    if (grid.Cells != null && grid.Cells.Count >= 3)
                    {
                        grid[2].DivideByApproximateLength(2);
                    }
                    var separators = grid.GetCellSeparators(true);
                    var beam = new Beam(lineProjected, Polygon.Rectangle(mullionSize, mullionSize), null, mullionMat)
                    {
                        IsElementDefinition = true
                    };
                    var mullionInstances = new[] {
                        beam.CreateInstance(levelTransform, "Base Mullion"),
                        beam.CreateInstance(levelTransform.Concatenated(new Transform(0, 0, doorHeight)), "Base Mullion"),
                        beam.CreateInstance(levelTransform.Concatenated(new Transform(0, 0, totalStorefrontHeight)), "Base Mullion")
                    };
                    foreach (var mullionInstance in mullionInstances)
                    {
                        mullionInstance.AdditionalProperties["Wall"] = primaryWall.Id;
                        elements.Add(mullionInstance);
                    }
                    foreach (var separator in separators)
                    {
                        // var line = new Line(separator, separator + new Vector3(0, 0, height));
                        // elements.Add(new ModelCurve(line, BuiltInMaterials.XAxis, levelTransform));
                        var instance = mullion.CreateInstance(new Transform(separator, lineProjected.Direction(), Vector3.ZAxis, 0).Concatenated(levelTransform), "Mullion");
                        instance.AdditionalProperties["Wall"] = primaryWall.Id;
                        elements.Add(instance);
                    }

                    var headerHeight = height - totalStorefrontHeight;
                    if (headerHeight > 0.01)
                    {
                        var header = new Header(lineProjected, sumThickness, headerHeight, wallMat, levelTransform.Concatenated(new Transform(0, 0, totalStorefrontHeight)));
                        header.AdditionalProperties["Wall"] = primaryWall.Id;
                        elements.Add(header);
                        header.AdditionalProperties[wallCandidatePropertyName] = wallCandidateId;
                    }
                }
            }

            return elements;
        }

        public static RoomEdge FindPrimaryAccessEdge(IEnumerable<RoomEdge> edgesToClassify, IEnumerable<Line> corridorSegments, Profile floorBoundary, out IEnumerable<RoomEdge> otherSegments, double maxDist = 0)
        {
            if (corridorSegments != null && corridorSegments.Count() != 0)
            {
                // if we have corridors, find the best edge along a corridor
                return FindEdgeAdjacentToSegments(edgesToClassify, corridorSegments, out otherSegments, maxDist);
            }
            else if (floorBoundary != null)
            {
                // if we have no corridors, find the best edge not along the floor boundary
                var edgesAlongFloorBoundary = FindAllEdgesAdjacentToSegments(edgesToClassify, floorBoundary.Segments(), out var edgesNotAlongFloorBoundary, out var corridorEdges);
                var edgesByLength = edgesNotAlongFloorBoundary.OrderByDescending(l => l.Length);
                var bestEdge = edgesByLength.FirstOrDefault();
                if (bestEdge == null)
                {
                    bestEdge = edgesAlongFloorBoundary.OrderBy(e => e.Length).Last();
                    otherSegments = edgesAlongFloorBoundary.Except(new[] { bestEdge });
                    return bestEdge;
                }
                otherSegments = edgesByLength.Skip(1).Union(edgesAlongFloorBoundary);
                if (otherSegments.Count() != edgesToClassify.Count() - 1)
                {
                    Console.WriteLine("We lost somebody!");
                }
                return bestEdge;
            }
            else
            {
                var edgesByLength = edgesToClassify.OrderByDescending(l => l.Length);
                var bestEdge = edgesByLength.First();
                otherSegments = edgesByLength.Skip(1);
                return bestEdge;
            }
        }

        public static List<RoomEdge> FindAllEdgesAdjacentToSegments(IEnumerable<RoomEdge> edgesToClassify, IEnumerable<Line> comparisonSegments, out List<RoomEdge> otherSegments, out List<Line> corridorEdges)
        {
            otherSegments = new List<RoomEdge>();
            var adjacentSegments = new List<RoomEdge>();
            corridorEdges = new List<Line>();

            List<double> checkPoints = Enumerable.Range(1, 9) // Creates a range of integers [0, 10]
                                 .Select(i => i * 0.1) // Multiplies each integer by 0.1
                                 .ToList();


            foreach (var edge in edgesToClassify)
            {
                var adjacentToAny = false;
                foreach (var checkPoint in checkPoints)
                {
                    if (!adjacentToAny)
                    {
                        var midpt = edge.Line.PointAtNormalized(checkPoint);

                        // var midpt = edge.Line.Mid();
                        midpt.Z = 0;
                        foreach (var comparisonSegment in comparisonSegments)
                        {
                            var start = comparisonSegment.Start;
                            var end = comparisonSegment.End;
                            start.Z = 0;
                            end.Z = 0;
                            var comparisonSegmentProjected = new Line(start, end);
                            var dist = midpt.DistanceTo(comparisonSegmentProjected);
                            if (dist < 0.1 + 1e-3)
                            {
                                adjacentToAny = true;
                                adjacentSegments.Add(edge);
                                corridorEdges.Add(comparisonSegment);
                                break;
                            }
                        }
                    }
                }

                if (!adjacentToAny)
                {
                    otherSegments.Add(edge);
                }
            }
            return adjacentSegments;
        }

        // public static List<RoomEdge> FindAllEdgesAdjacentToSegments(IEnumerable<RoomEdge> edgesToClassify, IEnumerable<Line> comparisonSegments, out List<RoomEdge> otherSegments, out List<Line> corridorEdges)
        // {
        //     otherSegments = new List<RoomEdge>();
        //     var adjacentSegments = new List<RoomEdge>();
        //     corridorEdges = new List<Line>();

        //     foreach (var edge in edgesToClassify)
        //     {
        //         var midpt = edge.Line.Mid();
        //         midpt.Z = 0;
        //         var adjacentToAny = false;
        //         foreach (var comparisonSegment in comparisonSegments)
        //         {
        //             var start = comparisonSegment.Start;
        //             var end = comparisonSegment.End;
        //             start.Z = 0;
        //             end.Z = 0;
        //             var comparisonSegmentProjected = new Line(start, end);
        //             var dist = midpt.DistanceTo(comparisonSegmentProjected);
        //             if (dist < 0.3 + 1e-3)
        //             {
        //                 adjacentToAny = true;
        //                 adjacentSegments.Add(edge);
        //                 break;
        //             }
        //         }
        //         if (!adjacentToAny)
        //         {
        //             otherSegments.Add(edge);
        //         }
        //     }
        //     return adjacentSegments;
        // }
        public static RoomEdge FindEdgeAdjacentToSegments(IEnumerable<RoomEdge> edgesToClassify, IEnumerable<Line> segmentsToTestAgainst, out IEnumerable<RoomEdge> otherSegments, double maxDist = 0)
        {
            var minDist = double.MaxValue;
            var minSeg = edgesToClassify.OrderBy(e => e.Length).Last(); // if no max dist, and no corridor, we just return the longest edge as an orientation guide.
            var allEdges = edgesToClassify.ToList();
            var selectedIndex = 0;
            for (int i = 0; i < allEdges.Count; i++)
            {
                var edge = allEdges[i];
                var midpt = edge.Line.Mid().Project(Plane.XY);
                foreach (var seg in segmentsToTestAgainst)
                {
                    var segProjected = seg.Projected(Plane.XY);
                    var dist = midpt.DistanceTo(seg);
                    // if two segments are basically the same distance to the corridor segment,
                    // prefer the longer one.
                    if (Math.Abs(dist - minDist) < 0.1)
                    {
                        minDist = dist;
                        if (minSeg.Length < edge.Length)
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
            if (maxDist != 0 && minDist >= maxDist)
            {
                Console.WriteLine($"no matches: {minDist}");
                otherSegments = allEdges;
                return null;
            }
            otherSegments = Enumerable.Range(0, allEdges.Count).Except(new[] { selectedIndex }).Select(i => allEdges[i]);
            return minSeg;
        }

        public static List<(RoomEdge Line, IEnumerable<RoomEdge> OtherSegments)> SortEdgesByPrimaryAccess(IEnumerable<RoomEdge> edgesToClassify, IEnumerable<Line> corridorSegments, Profile floorBoundary, double maxDist = 0)
        {
            if (corridorSegments != null && corridorSegments.Count() != 0)
            {
                // if we have corridors, find the best edge along a corridor
                return FindEdgesAdjacentToSegments(edgesToClassify, corridorSegments, maxDist);
            }

            if (floorBoundary != null)
            {
                // if we have no corridors, find the best edge not along the floor boundary
                var edgesAlongFloorBoundary = FindAllEdgesAdjacentToSegments(edgesToClassify, floorBoundary.Segments(), out var edgesNotAlongFloorBoundary, out var corridorEdges);
                return (edgesNotAlongFloorBoundary.Count() == 0 ? edgesAlongFloorBoundary : edgesNotAlongFloorBoundary)
                .OrderByDescending(e => e.Length).Select(e =>
                {
                    var otherSegments = edgesToClassify.Except(new[] { e });
                    return (e, otherSegments);
                }).ToList();
            }

            var edgesByLength = edgesToClassify.OrderByDescending(e => e.Length);
            return edgesByLength.Select(e =>
            {
                var otherSegments = edgesToClassify.Except(new[] { e });
                return (e, otherSegments);
            }).ToList();
        }
        public static List<(RoomEdge Line, IEnumerable<RoomEdge> OtherSegments)> FindEdgesAdjacentToSegments(IEnumerable<RoomEdge> edgesToClassify, IEnumerable<Line> segmentsToTestAgainst, double maxDist = 0)
        {
            if (segmentsToTestAgainst.Count() > 0)
            {
                var edgesByDist = edgesToClassify.Select(e =>
                {
                    var midpt = e.Line.Mid().Project(Plane.XY);
                    (RoomEdge line, double dist) edge = (e, segmentsToTestAgainst.Min(s => midpt.DistanceTo(s)));
                    return edge;
                });

                if (maxDist != 0)
                {
                    var edgesUnderMaxDist = edgesByDist.Where(e => e.dist < maxDist);
                    if (edgesUnderMaxDist.Count() > 0)
                    {
                        edgesByDist = edgesUnderMaxDist;
                    }
                    else
                    {
                        Console.WriteLine($"no matches under max dist — using all edges: {maxDist}");
                    }
                }

                var comparer = new EdgesComparer();
                edgesByDist = edgesByDist.OrderBy(e => e, comparer);

                return edgesByDist.Select(e =>
                {
                    var otherSegments = edgesToClassify.Except(new[] { e.line });
                    return (e.line, otherSegments);
                }).ToList();
            }

            if (maxDist != 0)
            {
                Console.WriteLine($"no matches: {maxDist}");
                return new List<(RoomEdge Line, IEnumerable<RoomEdge> OtherSegments)>() { (null, edgesToClassify) };
            }

            // if no max dist, and no corridor, we just return the longest edge as an orientation guide.
            var edgesByLength = edgesToClassify.OrderByDescending(e => e.Length);
            return edgesByLength.Select(e =>
            {
                var otherSegments = edgesToClassify.Except(new[] { e });
                return (e, otherSegments);
            }).ToList();
        }

        public static List<RoomEdge> PartitionsAndGlazingCandidatesFromGrid(List<RoomEdge> wallCandidateLines, Grid2d grid, ISpaceBoundary room)
        {
            var wallCandidatesOut = new List<RoomEdge>();
            try
            {
                var cellSeparators = grid.GetCellSeparators(GridDirection.V, true);
                wallCandidatesOut.AddRange(cellSeparators.OfType<Line>().Select(c => new RoomEdge { Line = c, Type = "Partition" }));
            }
            catch (Exception e)
            {
                Console.WriteLine("Couldn't get grid cell separators");
                Console.WriteLine(e.Message);
                // exception in cell separators
            }
            var glassLines = wallCandidateLines.Where(l => l.Type == "Glass-Edge").Select(w => w.Line);

            // Construct an appropriate room edge from the given line, matching thickness from already existing wall candidates.
            RoomEdge GetRoomEdgeFromLine(Line line)
            {
                RoomEdge closestRoomEdge = null;
                var closestDist = double.MaxValue;
                foreach (var wallCandidateLine in wallCandidateLines.Where(l => l.Thickness is not null))
                {
                    var mid = line.Mid();
                    var dist = mid.ClosestPointOn(wallCandidateLine.Line).DistanceTo(mid);
                    if (dist < closestDist)
                    {
                        closestDist = dist;
                        closestRoomEdge = wallCandidateLine;
                    }
                }
                return new RoomEdge
                {
                    Line = line,
                    Thickness = closestRoomEdge?.Thickness,
                    PrimaryEntryEdge = closestRoomEdge?.PrimaryEntryEdge ?? false
                };
            }

            foreach (var gridCell in grid.GetCells())
            {
                var trimmedGeo = gridCell.GetTrimmedCellGeometry();
                if (trimmedGeo.Count() > 0)
                {
                    var segments = trimmedGeo.OfType<Polygon>().SelectMany(g => g.Segments()).Select(GetRoomEdgeFromLine);
                    var entrySegment = FindEdgeAdjacentToSegments(segments, glassLines, out var otherEdges);
                    if (entrySegment != null)
                    {
                        entrySegment.Type = room.DefaultWallType ?? "Glass";
                        entrySegment.PrimaryEntryEdge = true;
                        wallCandidatesOut.Add(entrySegment);
                    }
                }
            }

            return wallCandidatesOut;
        }
    }

    public class EdgesComparer : IComparer<(RoomEdge line, double dist)>
    {
        public int Compare((RoomEdge line, double dist) edge1, (RoomEdge line, double dist) edge2)
        {
            if (Math.Abs(edge1.dist - edge2.dist) < 0.1)
            {
                return edge2.line.Line.Length().CompareTo(edge1.line.Line.Length());
            }
            return edge1.dist.CompareTo(edge2.dist);
        }

        int IComparer<(RoomEdge line, double dist)>.Compare((RoomEdge line, double dist) x, (RoomEdge line, double dist) y)
        {
            return Compare(x, y);
        }
    }
}
