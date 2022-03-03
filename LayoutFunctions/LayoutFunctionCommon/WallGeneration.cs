using System;
using System.Collections.Generic;
using System.Linq;
using Elements;
using Elements.Geometry;
using Elements.Spatial;

namespace Elements
{
    public static class WallGeneration
    {
        private static double mullionSize = 0.07;
        private static double doorWidth = 0.9;
        private static double doorHeight = 2.1;
        private static double sideLightWidth = 0.4;

        private static Material wallMat = new Material("Drywall", new Color(0.9, 0.9, 0.9, 1.0), 0.01, 0.01);
        private static Material glassMat = new Material("Glass", new Color(0.7, 0.7, 0.7, 0.3), 0.3, 0.6);
        private static Material mullionMat = new Material("Storefront Mullions", new Color(0.5, 0.5, 0.5, 1.0));
        public static List<(Line line, string type)> FindWallCandidates(ISpaceBoundary room, Profile levelProfile, IEnumerable<Line> corridorSegments, out Line orientationGuideEdge, IEnumerable<string> wallTypeFilter = null)
        {
            var spaceBoundary = room.Boundary;
            var wallCandidateLines = new List<(Line line, string type)>();
            orientationGuideEdge = FindPrimaryAccessEdge(spaceBoundary.Perimeter.Segments().Select(s => s.TransformedLine(room.Transform)), corridorSegments, levelProfile, out var wallCandidates);
            wallCandidateLines.Add((orientationGuideEdge, "Glass"));
            if (levelProfile != null)
            {
                var exteriorWalls = FindAllEdgesAdjacentToSegments(wallCandidates, levelProfile.Segments(), out var notAdjacentToFloorBoundary);
                wallCandidateLines.AddRange(notAdjacentToFloorBoundary.Select(s => (s, "Solid")));
            }
            else
            {
                // if no level or floor is present, everything that's not glass is solid.
                wallCandidateLines.AddRange(wallCandidates.Select(s => (s, "Solid")));
            }
            if (wallTypeFilter != null)
            {
                return wallCandidateLines.Where((w) => wallTypeFilter.Contains(w.type)).ToList();
            }
            return wallCandidateLines;
        }

        private static double CalculateTotalStorefrontHeight(double volumeHeight)
        {
            return Math.Min(2.7, volumeHeight);
        }

        private static GeometricElement CreateMullion(double height)
        {
            var totalStorefrontHeight = CalculateTotalStorefrontHeight(height);
            var mullion = new StandardWall(new Line(new Vector3(-mullionSize / 2, 0, 0), new Vector3(mullionSize / 2, 0, 0)), mullionSize, totalStorefrontHeight, mullionMat);
            mullion.IsElementDefinition = true;
            return mullion;
        }

        public static void GenerateWalls(Model model, List<(Line line, string type)> wallCandidateLines, double height, Transform levelTransform, bool debugMode = false)
        {
            if (debugMode)
            {
                foreach (var wallCandidate in wallCandidateLines)
                {
                    Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(wallCandidate.line));
                    var lineProjected = wallCandidate.line.TransformedLine(new Transform(0, 0, -wallCandidate.line.End.Z));
                    switch (wallCandidate.type)
                    {
                        case "Solid":
                            model.AddElement(new StandardWall(lineProjected, 0.2, height, BuiltInMaterials.ZAxis, levelTransform));
                            break;
                        case "Glass":
                            model.AddElement(new StandardWall(lineProjected, 0.2, height, BuiltInMaterials.XAxis, levelTransform));
                            break;
                        case "Partition":
                            model.AddElement(new StandardWall(lineProjected, 0.2, height, BuiltInMaterials.YAxis, levelTransform));
                            break;
                    }
                }

                return;
            }
            var totalStorefrontHeight = CalculateTotalStorefrontHeight(height);
            var mullion = CreateMullion(height);
            foreach (var wallCandidate in wallCandidateLines)
            {
                var lineProjected = wallCandidate.line.TransformedLine(new Transform(0, 0, -wallCandidate.line.End.Z));

                if (wallCandidate.type == "Solid")
                {
                    model.AddElement(new StandardWall(lineProjected, 0.2, height, wallMat, levelTransform));
                }
                else if (wallCandidate.type == "Partition")
                {
                    model.AddElement(new StandardWall(wallCandidate.line, 0.1, height, wallMat, levelTransform));
                }
                else if (wallCandidate.type == "Glass")
                {
                    var grid = new Grid1d(lineProjected);
                    var offsets = new[] { sideLightWidth, sideLightWidth + doorWidth }.Where(o => grid.Domain.Min + o < grid.Domain.Max);
                    grid.SplitAtOffsets(offsets);
                    if (grid.Cells != null && grid.Cells.Count >= 3)
                    {
                        grid[2].DivideByApproximateLength(2);
                    }
                    var separators = grid.GetCellSeparators(true);
                    var beam = new Beam(lineProjected, Polygon.Rectangle(mullionSize, mullionSize), mullionMat, 0, 0, 0, isElementDefinition: true);
                    model.AddElement(beam.CreateInstance(levelTransform, "Base Mullion"));
                    model.AddElement(beam.CreateInstance(levelTransform.Concatenated(new Transform(0, 0, doorHeight)), "Base Mullion"));
                    model.AddElement(beam.CreateInstance(levelTransform.Concatenated(new Transform(0, 0, totalStorefrontHeight)), "Base Mullion"));
                    foreach (var separator in separators)
                    {
                        // var line = new Line(separator, separator + new Vector3(0, 0, height));
                        // model.AddElement(new ModelCurve(line, BuiltInMaterials.XAxis, levelTransform));
                        var instance = mullion.CreateInstance(new Transform(separator, lineProjected.Direction(), Vector3.ZAxis, 0).Concatenated(levelTransform), "Mullion");
                        model.AddElement(instance);
                    }
                    model.AddElement(new StandardWall(lineProjected, 0.05, totalStorefrontHeight, glassMat, levelTransform));
                    var headerHeight = height - totalStorefrontHeight;
                    if (headerHeight > 0.01)
                    {
                        model.AddElement(new StandardWall(lineProjected, 0.2, headerHeight, wallMat, levelTransform.Concatenated(new Transform(0, 0, totalStorefrontHeight))));
                    }
                }
            }
        }

        public static Line FindPrimaryAccessEdge(IEnumerable<Line> edgesToClassify, IEnumerable<Line> corridorSegments, Profile floorBoundary, out IEnumerable<Line> otherSegments, double maxDist = 0)
        {
            if (corridorSegments != null && corridorSegments.Count() != 0)
            {
                // if we have corridors, find the best edge along a corridor
                return FindEdgeAdjacentToSegments(edgesToClassify, corridorSegments, out otherSegments, maxDist);
            }
            else if (floorBoundary != null)
            {
                // if we have no corridors, find the best edge not along the floor boundary
                var edgesAlongFloorBoundary = FindAllEdgesAdjacentToSegments(edgesToClassify, floorBoundary.Segments(), out var edgesNotAlongFloorBoundary);
                var edgesByLength = edgesNotAlongFloorBoundary.OrderByDescending(l => l.Length());
                var bestEdge = edgesByLength.FirstOrDefault();
                if (bestEdge == null)
                {
                    bestEdge = edgesAlongFloorBoundary.OrderBy(e => e.Length()).Last();
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
                var edgesByLength = edgesToClassify.OrderByDescending(l => l.Length());
                var bestEdge = edgesByLength.First();
                otherSegments = edgesByLength.Skip(1);
                return bestEdge;
            }
        }

        public static List<Line> FindAllEdgesAdjacentToSegments(IEnumerable<Line> edgesToClassify, IEnumerable<Line> comparisonSegments, out List<Line> otherSegments)
        {
            otherSegments = new List<Line>();
            var adjacentSegments = new List<Line>();

            foreach (var edge in edgesToClassify)
            {
                var midpt = edge.PointAt(0.5);
                midpt.Z = 0;
                var adjacentToAny = false;
                foreach (var comparisonSegment in comparisonSegments)
                {
                    var start = comparisonSegment.Start;
                    var end = comparisonSegment.End;
                    start.Z = 0;
                    end.Z = 0;
                    var comparisonSegmentProjected = new Line(start, end);
                    var dist = midpt.DistanceTo(comparisonSegment);
                    if (dist < 0.3)
                    {
                        adjacentToAny = true;
                        adjacentSegments.Add(edge);
                        break;
                    }
                }
                if (!adjacentToAny)
                {
                    otherSegments.Add(edge);
                }
            }
            return adjacentSegments;
        }
        public static Line FindEdgeAdjacentToSegments(IEnumerable<Line> edgesToClassify, IEnumerable<Line> segmentsToTestAgainst, out IEnumerable<Line> otherSegments, double maxDist = 0)
        {
            var minDist = double.MaxValue;
            var minSeg = edgesToClassify.OrderBy(e => e.Length()).Last(); // if no max dist, and no corridor, we just return the longest edge as an orientation guide.
            var allEdges = edgesToClassify.ToList();
            var selectedIndex = 0;
            for (int i = 0; i < allEdges.Count; i++)
            {
                var edge = allEdges[i];
                var midpt = edge.PointAt(0.5);
                foreach (var seg in segmentsToTestAgainst)
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

        public static List<(Line line, string type)> PartitionsAndGlazingCandidatesFromGrid(List<(Line line, string type)> wallCandidateLines, Grid2d grid, Profile levelBoundary)
        {
            var wallCandidatesOut = new List<(Line line, string type)>();
            try
            {
                var cellSeparators = grid.GetCellSeparators(GridDirection.V, true);
                wallCandidatesOut.AddRange(cellSeparators.OfType<Line>().Select(c => (c, "Partition")));
            }
            catch (Exception e)
            {
                Console.WriteLine("Couldn't get grid cell separators");
                Console.WriteLine(e.Message);
                // exception in cell separators
            }
            var glassLines = wallCandidateLines.Where(l => l.type == "Glass-Edge").Select(w => w.line);
            foreach (var gridCell in grid.GetCells())
            {
                var trimmedGeo = gridCell.GetTrimmedCellGeometry();
                if (trimmedGeo.Count() > 0)
                {
                    var segments = trimmedGeo.OfType<Polygon>().SelectMany(g => g.Segments());
                    var glassSegment = FindEdgeAdjacentToSegments(segments, glassLines, out var otherEdges);
                    if (glassSegment != null)
                    {
                        wallCandidatesOut.Add((glassSegment, "Glass"));
                    }
                }
            }

            return wallCandidatesOut;
        }
    }
}
