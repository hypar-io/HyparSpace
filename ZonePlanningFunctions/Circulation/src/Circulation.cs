using Elements;
using Elements.Geometry;
using Elements.Geometry.Solids;
using Elements.Spatial;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Circulation
{
    public static class Circulation
    {
        /// <summary>
        /// Generate a circulation path
        /// </summary>
        /// <param name="model">The input model.</param>
        /// <param name="input">The arguments to the execution.</param>
        /// <returns>A CirculationOutputs instance containing computed results and the model with any new elements.</returns>
        public static CirculationOutputs Execute(Dictionary<string, Model> inputModels, CirculationInputs input)
        {
            #region Gather Inputs

            // Set up output object
            var output = new CirculationOutputs();

            // Get Levels
            var levelsModel = inputModels["Levels"];
            var levelVolumes = levelsModel.AllElementsOfType<LevelVolume>();
            // Validate that level volumes are present in Levels dependency
            if (levelVolumes.Count() == 0)
            {
                output.Warnings.Add("This function requires LevelVolumes, produced by functions like \"Simple Levels by Envelope\". Try a different levels function.");
                return output;
            }

            // Get Floors
            inputModels.TryGetValue("Floors", out var floorsModel);

            // Get Cores
            var hasCore = inputModels.TryGetValue("Core", out var coresModel);
            var cores = coresModel?.AllElementsOfType<ServiceCore>() ?? new List<ServiceCore>();

            // Get Walls
            var hasWalls = inputModels.TryGetValue("Walls", out var wallsModel);
            var walls = wallsModel?.Elements.Values.Where(e => new[] { typeof(Wall), typeof(WallByProfile), typeof(StandardWall) }.Contains(e.GetType())) ?? new List<Element>();
            // Get Columns
            var hasColumns = inputModels.TryGetValue("Columns", out var columnsModel);
            var columns = columnsModel?.AllElementsOfType<Column>() ?? new List<Column>();

            // Get program requirements
            var hasProgramRequirements = inputModels.TryGetValue("Program Requirements", out var programReqsModel);

            #endregion

            // create a collection of LevelElements (which contain other elements)
            // to add to the model
            var levels = new List<LevelElements>();

            // For every level volume, create space boundaries with corridors and splits
            CreateCorridors(input, output, levelVolumes, floorsModel, cores, levels, walls);

            // adding levels also adds the space boundaries, since they're in the levels' own elements collections
            output.Model.AddElements(levels);

            return output;
        }

        private static void CreateCorridors(CirculationInputs input,
                                                        CirculationOutputs output,
                                                        IEnumerable<LevelVolume> levelVolumes,
                                                        Model floorsModel,
                                                        IEnumerable<ServiceCore> cores,
                                                        List<LevelElements> levels,
                                                        IEnumerable<Element> walls)
        {
            var corridorWidth = input.CorridorWidth;
            Color CORRIDOR_MATERIAL_COLOR = new Color(0.996, 0.965, 0.863, 1.0);
            var corridorMat = new Material("Circulation", CORRIDOR_MATERIAL_COLOR, doubleSided: true);
            // for every level volume
            foreach (var lvl in levelVolumes)
            {
                AdjustLevelVolumesToFloors(floorsModel, lvl);

                var levelBoundary = new Profile(lvl.Profile.Perimeter, lvl.Profile.Voids, Guid.NewGuid(), null);

                // Add any cores present within the level boundary as voids
                var coresInBoundary = cores.Where(c => levelBoundary.Contains(c.Centroid)).ToList();
                foreach (var core in coresInBoundary)
                {
                    levelBoundary.Voids.Add(new Polygon(core.Profile.Perimeter.Vertices).Reversed());
                    levelBoundary.OrientVoids();
                }

                // if we have any walls, try to create boundaries from any enclosed spaces, 
                // and then continue to operate on the largest "leftover" region.
                var wallsInBoundary = walls.Where(w =>
                {
                    var pt = GetPointFromWall(w);
                    return pt.HasValue && levelBoundary.Contains(pt.Value);
                }).ToList();
                var interiorZones = new List<Profile>();
                if (wallsInBoundary.Count() > 0)
                {
                    var newLevelBoundary = AttemptToSplitWallsAndYieldLargestZone(levelBoundary, wallsInBoundary, out var centerlines, out interiorZones, output.Model);
                    levelBoundary = newLevelBoundary;
                }

                List<Profile> corridorProfiles = new List<Profile>();
                List<Profile> thickerOffsetProfiles = null;

                // Process circulation
                if (input.CirculationMode == CirculationInputsCirculationMode.Automatic)
                {
                    (corridorProfiles, thickerOffsetProfiles) = GenerateAutomaticCirculation(input, corridorWidth, lvl, levelBoundary, coresInBoundary);
                }

                output.Model.AddElements(thickerOffsetProfiles);

                // Construct LevelElements to contain space boundaries
                var level = new LevelElements()
                {
                    Name = lvl.Name,
                    Elements = new List<Element>()
                };
                level.LevelVolumeId = lvl.Id;
                levels.Add(level);

                ProcessManuallyAddedCorridors(input, corridorProfiles);

                // create floors for corridors and add them to the associated level.
                try
                {
                    var cpUnion = Profile.UnionAll(corridorProfiles);
                    cpUnion.Select(p => new Floor(p, 0.1, lvl.Transform, corridorMat)).ToList().ForEach(f => level.Elements.Add(f));
                }
                catch
                {
                    corridorProfiles.Select(p => new Floor(p, 0.1, lvl.Transform, corridorMat)).ToList().ForEach(f => level.Elements.Add(f));
                }
            }
        }

        private static void ProcessManuallyAddedCorridors(CirculationInputs input, List<Profile> corridorProfiles)
        {
            var corridorProfilesForUnion = new List<Profile>();
            foreach (var corridorPolyline in input.AddCorridors)
            {
                if (corridorPolyline == null || corridorPolyline.Polyline == null)
                {
                    continue;
                }
                var corrPgons = corridorPolyline.Polyline.OffsetOnSide(corridorPolyline.Width, corridorPolyline.Flip);
                corridorProfilesForUnion.AddRange(corrPgons.Select(p => new Profile(p)));
            }
            corridorProfiles.AddRange(corridorProfilesForUnion);
        }

        private static (List<Profile> corridorProfiles, List<Profile> thickenedOffsetProfile)  GenerateAutomaticCirculation(CirculationInputs input, double corridorWidth, LevelVolume lvl, Profile levelBoundary, List<ServiceCore> coresInBoundary)
        {
            List<Profile> corridorProfiles = new List<Profile>();
            var perimeter = levelBoundary.Perimeter;
            var perimeterSegments = perimeter.Segments();

            IdentifyShortEdges(perimeter, perimeterSegments, out var shortEdges, out var shortEdgeIndices);

            // Single Loaded Zones
            var singleLoadedZones = CalculateSingleLoadedZones(input, corridorWidth, perimeterSegments, shortEdgeIndices);

            GenerateEndZones(input, corridorWidth, lvl, corridorProfiles, perimeterSegments, shortEdges, singleLoadedZones, out var thickenedEnds, out var thickerOffsetProfiles, out var innerOffsetMinusThickenedEnds, out var exclusionRegions);

            // join single loaded zones to each other (useful in bent-bar case)
            var allCenterLines = JoinSingleLoaded(singleLoadedZones);

            // thicken and extend single loaded
            ThickenAndExtendSingleLoaded(corridorWidth, corridorProfiles, coresInBoundary, thickenedEnds, innerOffsetMinusThickenedEnds, allCenterLines);

            CorridorsFromCore(corridorWidth, corridorProfiles, levelBoundary, coresInBoundary, innerOffsetMinusThickenedEnds, exclusionRegions);
            return (corridorProfiles, thickerOffsetProfiles);
        }

        private static Line GetCenterlineFromWall(Element w)
        {
            switch (w)
            {
                case StandardWall standardWall:
                    return standardWall.CenterLine.TransformedLine(standardWall.Transform);
                case WallByProfile wallByProfile:
                    return wallByProfile.Centerline.TransformedLine(wallByProfile.Transform);
                case Wall wall:
                    //TODO: handle this case
                    return null;
                default:
                    return null;
            }
        }
        private static Vector3? GetPointFromWall(Element w)
        {
            switch (w)
            {
                case StandardWall standardWall:
                    return standardWall.Transform.OfPoint(standardWall.CenterLine.PointAt(0.5));
                case WallByProfile wallByProfile:
                    return wallByProfile.Transform.OfPoint(wallByProfile.Centerline.PointAt(0.5));
                case Wall wall:
                    return wall.Transform.OfPoint(wall.Profile.Perimeter.Centroid());
                default:
                    return null;
            }
        }

        private static void IdentifyShortEdges(Polygon perimeter, Line[] perimeterSegments, out List<Line> shortEdges, out List<int> shortEdgeIndices)
        {
            var TOO_SHORT = 9.0;
            var perimeterAngles = new List<double>();
            for (int i = 0; i < perimeter.Vertices.Count; i++)
            {
                var nextIndex = (i + 1) % perimeter.Vertices.Count;
                var prevIndex = (i + perimeter.Vertices.Count - 1) % perimeter.Vertices.Count;
                var prevVec = perimeter.Vertices[i] - perimeter.Vertices[prevIndex];
                var nextVec = perimeter.Vertices[nextIndex] - perimeter.Vertices[i];
                var angle = prevVec.PlaneAngleTo(nextVec);
                perimeterAngles.Add(angle);
            }
            var allLengths = perimeterSegments.Select(s => s.Length());
            var validLengths = allLengths.Where(l => l > TOO_SHORT)?.OrderBy(l => l);
            var shortLength = (validLengths?.FirstOrDefault() ?? 35.0 / 1.2) * 1.2;
            var longLength = Math.Min(validLengths.Cast<double?>().SkipLast(1).LastOrDefault<double?>() ?? 50.0, 50.0);
            shortEdges = new List<Line>();
            shortEdgeIndices = new List<int>();
            for (int i = 0; i < perimeterSegments.Count(); i++)
            {
                var start = perimeterAngles[i];
                var end = perimeterAngles[(i + 1) % perimeterAngles.Count];
                if (start > 80.0 && start < 100.0 && end > 80.0 && end < 100.0 && perimeterSegments[i].Length() < longLength)
                {
                    shortEdges.Add(perimeterSegments[i]);
                    shortEdgeIndices.Add(i);
                }
            }
        }

        private static void GenerateEndZones(CirculationInputs input, double corridorWidth, LevelVolume lvl, List<Profile> corridorProfiles, Line[] perimeterSegments, List<Line> shortEdges, List<(Polygon hull, Line centerLine)> singleLoadedZones, out List<Polygon> thickenedEndsOut, out List<Profile> thickerOffsetProfiles, out IEnumerable<Polygon> innerOffsetMinusThickenedEnds, out IEnumerable<Polygon> exclusionRegions)
        {
            // separate out short and long perimeter edges
            const double EXTEND_FACTOR = 0.2;
            var shortEdgesExtended = shortEdges.Select(l => new Line(l.Start - l.Direction() * EXTEND_FACTOR, l.End + l.Direction() * EXTEND_FACTOR));
            var longEdges = perimeterSegments.Except(shortEdges);
            var shortEdgeDepth = Math.Max(input.DepthAtEnds, input.OuterBandDepth);
            var longEdgeDepth = input.OuterBandDepth;

            var perimeterMinusSingleLoaded = new List<Profile>();
            perimeterMinusSingleLoaded.AddRange(Profile.Difference(new[] { lvl.Profile }, singleLoadedZones.Select(p => new Profile(p.hull))));
            var innerOffset = perimeterMinusSingleLoaded.SelectMany(p => p.Perimeter.Offset(-longEdgeDepth));
            // calculate zones at rectangular "ends"
            var thickenedEnds = shortEdgesExtended.SelectMany(s => s.ToPolyline(1).Offset(shortEdgeDepth, EndType.Butt)).ToList();
            thickerOffsetProfiles = thickenedEnds.Select(o => new Profile(o.Offset(0.01))).ToList();

            innerOffsetMinusThickenedEnds = innerOffset.SelectMany(i => Polygon.Difference(new[] { i }, thickenedEnds));
            exclusionRegions = innerOffsetMinusThickenedEnds.SelectMany(r => r.Offset(2.0 * corridorWidth, EndType.Square));

            var corridorInset = innerOffsetMinusThickenedEnds.Select(p => new Profile(p, p.Offset(-corridorWidth), Guid.NewGuid(), "Corridor"));
            corridorProfiles.AddRange(corridorInset);
            thickenedEndsOut = thickenedEnds;
        }

        private static void CorridorsFromCore(double corridorWidth, List<Profile> corridorProfiles, Profile levelBoundary, List<ServiceCore> coresInBoundary, IEnumerable<Polygon> innerOffsetMinusThickenedEnds, IEnumerable<Polygon> exclusionRegions)
        {
            var coreSegments = coresInBoundary.SelectMany(c => c.Profile.Perimeter.Offset((corridorWidth / 2.0) * 0.999).FirstOrDefault()?.Segments());

            foreach (var enclosedRegion in innerOffsetMinusThickenedEnds)
            {
                foreach (var segment in coreSegments)
                {
                    enclosedRegion.Contains(segment.Start, out var startContainment);
                    enclosedRegion.Contains(segment.End, out var endContainment);
                    if (endContainment == Containment.Outside && startContainment == Containment.Outside)
                    {
                        continue;
                    }
                    var extendedSegment = segment.ExtendTo(new Profile(enclosedRegion));
                    if (extendedSegment.Length() - segment.Length() < 2 * 8)
                    {
                        continue;
                    }
                    var thickenedCorridor = extendedSegment.ToPolyline(1).Offset(corridorWidth / 2.0, EndType.Butt);
                    var difference = new List<Profile>();

                    difference = Profile.Difference(corridorProfiles, exclusionRegions.Select(r => new Profile(r)));

                    if (difference.Count > 0 && difference.Sum(d => d.Perimeter.Area()) > 10.0)
                    {
                        corridorProfiles.AddRange(Profile.Intersection(thickenedCorridor.Select(c => new Profile(c)), new[] { levelBoundary }));
                    }
                }
            }
        }

        private static void ThickenAndExtendSingleLoaded(double corridorWidth, List<Profile> corridorProfiles, List<ServiceCore> coresInBoundary, List<Polygon> thickerOffsets, IEnumerable<Polygon> innerOffsetMinusThicker, (Polygon hull, Line centerLine)[] allCenterLines)
        {
            // thicken and extend single loaded
            foreach (var singleLoadedZone in allCenterLines)
            {
                var cl = singleLoadedZone.centerLine;
                List<Line> centerlines = new List<Line> { cl };
                foreach (var core in coresInBoundary)
                {
                    List<Line> linesRunning = new List<Line>();
                    foreach (var curve in centerlines)
                    {
                        curve.Trim(core.Profile.Perimeter, out var linesTrimmedByCore);
                        linesRunning.AddRange(linesTrimmedByCore);
                    }
                    centerlines = linesRunning;
                }
                cl = centerlines.OrderBy(l => l.Length()).Last();
                foreach (var clCandidate in centerlines)
                {

                    var extended = clCandidate.ExtendTo(innerOffsetMinusThicker.SelectMany(p => p.Segments())).ToPolyline(1);
                    if (extended.Length() == cl.Length() && innerOffsetMinusThicker.Count() > 0)
                    {
                        var end = extended.End;
                        var dist = double.MaxValue;
                        Vector3? runningPt = null;
                        foreach (var boundary in innerOffsetMinusThicker)
                        {
                            var closestDist = end.DistanceTo(boundary, out var pt);
                            if (closestDist < dist)
                            {
                                dist = closestDist;
                                runningPt = pt;
                            }
                        }
                        extended = new Polyline(new[] { extended.Start, extended.End, runningPt.Value });
                    }
                    //TODO - verify that newly constructed line is contained within building perimeter
                    var thickenedCorridor = extended.Offset(corridorWidth / 2.0, EndType.Square);
                    corridorProfiles.AddRange(Profile.Difference(thickenedCorridor.Select(c => new Profile(c)), thickerOffsets.Select(c => new Profile(c))));
                }

            }
        }

        private static (Polygon hull, Line centerLine)[] JoinSingleLoaded(List<(Polygon hull, Line centerLine)> singleLoadedZones)
        {
            // join single loaded zones to each other (useful in bent-bar case)
            var allCenterLines = singleLoadedZones.ToArray();
            const double DISTANCE_TRESHHOLD_ID = 10.0;
            for (int i = 0; i < allCenterLines.Count(); i++)
            {
                var crvA = allCenterLines[i].centerLine;
                for (int j = 0; j < i; j++)
                {
                    var crvB = allCenterLines[j].centerLine;
                    var doesIntersect = crvA.Intersects(crvB, out var intersection, true, true);
                    // Console.WriteLine($"DOES INTERSECT: " + doesIntersect.ToString());

                    var nearPtA = intersection.ClosestPointOn(crvA);
                    var nearPtB = intersection.ClosestPointOn(crvB);
                    if (nearPtA.DistanceTo(intersection) + nearPtB.DistanceTo(intersection) < DISTANCE_TRESHHOLD_ID)
                    {
                        if (nearPtA.DistanceTo(crvA.Start) < 0.01)
                        {
                            allCenterLines[i] = (allCenterLines[i].hull, new Line(intersection, crvA.End));
                        }
                        else
                        {
                            allCenterLines[i] = (allCenterLines[i].hull, new Line(crvA.Start, intersection));
                        }
                        if (nearPtB.DistanceTo(crvB.Start) < 0.01)
                        {
                            allCenterLines[j] = (allCenterLines[j].hull, new Line(intersection, crvB.End));
                        }
                        else
                        {
                            allCenterLines[j] = (allCenterLines[j].hull, new Line(crvB.Start, intersection));
                        }
                    }

                }
            }

            return allCenterLines;
        }

        private static List<(Polygon hull, Line centerLine)> CalculateSingleLoadedZones(CirculationInputs input, double corridorWidth, 
                                                                                        Line[] perimeterSegments, List<int> shortEdgeIndices)
        {
            var singleLoadedZones = new List<(Polygon hull, Line centerLine)>();
            const double SPACE_TOLERANCE = 5.0;
            // (two offsets, two corridors, and a usable space width)
            var singleLoadedLengthThreshold = input.OuterBandDepth * 2.0 + corridorWidth * 2.0 + SPACE_TOLERANCE; 
            foreach (var sei in shortEdgeIndices)
            {
                var ps = perimeterSegments;
                if (ps[sei].Length() < singleLoadedLengthThreshold)
                {
                    var legSegments = new[] {
                            ps[(sei + ps.Length -1) % ps.Length],
                            ps[sei],
                            ps[(sei + 1) % ps.Length]
                        };
                    var legLength = Math.Min(legSegments[0].Length(), legSegments[2].Length());
                    legSegments[0] = new Line(ps[sei].Start, ps[sei].Start + legLength * (legSegments[0].Direction() * -1));
                    legSegments[2] = new Line(ps[sei].End, ps[sei].End + legLength * (legSegments[2].Direction()));
                    var hull = ConvexHull.FromPolylines(legSegments.Select(l => l.ToPolyline(1)));
                    var centerLine = new Line((legSegments[0].Start + legSegments[2].Start) / 2, (legSegments[0].End + legSegments[2].End) / 2);

                    singleLoadedZones.Add((hull, centerLine));
                }

            }

            return singleLoadedZones;
        }

        private static Profile AttemptToSplitWallsAndYieldLargestZone(Profile levelBoundary, List<Element> wallsInBoundary, 
                                                                      out IEnumerable<Polyline> wallCenterlines, out List<Profile> otherProfiles, 
                                                                      Model m = null)
        {
            var centerlines = wallsInBoundary.Select(w => GetCenterlineFromWall(w).Extend(0.1)).Where(w => w != null).Select(l => l.ToPolyline(1));
            otherProfiles = new List<Profile>();
            wallCenterlines = centerlines;
            var xy = new Plane((0, 0), (0, 0, 1));
            try
            {
                var graph = HalfEdgeGraph2d.Construct(new Polygon[] { }, centerlines);
                var pgons = graph.Polygonize().Select(p => p.Project(xy));
                var nonPrimaryPgons = pgons.Where(p => !p.IsClockWise() && p.Area() < levelBoundary.Area() * 0.5).Select(p => new Profile(p)).ToList();
                var union = Profile.UnionAll(nonPrimaryPgons);
                var splitResults = Profile.Difference(new[] { levelBoundary }, union);
                // Console.WriteLine(pgons.Count());
                // var rand = new Random();
                // nonPrimaryPgons.ForEach(p => m?.AddElement(new GeometricElement(new Transform(0, 0, 2), rand.NextMaterial(), new Lamina(p)) { AdditionalProperties = new Dictionary<string, object> { { "Area", p.Area() } } }));
                var resultsSorted = splitResults.OrderByDescending(p => Math.Abs(p.Area()));
                var largestZone = resultsSorted.First();
                // Console.WriteLine(largestZone.Area());
                otherProfiles = nonPrimaryPgons;
                return largestZone;
            }
            catch
            {
                Console.WriteLine("🚃");
                return levelBoundary;
            }
        }

        /// <summary>
        /// If we have floors, we shrink our internal level volumes so they sit on top of / don't intersect with the floors.
        /// </summary>
        /// <param name="floorsModel">The floors model, which may or may not exist</param>
        /// <param name="lvl">The level volume</param>
        private static void AdjustLevelVolumesToFloors(Model floorsModel, LevelVolume lvl)
        {
            if (floorsModel != null)
            {
                var floorAtLevel = floorsModel.AllElementsOfType<Floor>().FirstOrDefault(f => Math.Abs(lvl.Transform.Origin.Z - f.Transform.Origin.Z) < (f.Thickness * 1.1));
                if (floorAtLevel != null)
                {
                    lvl.Height -= floorAtLevel.Thickness;
                    var floorFaceOffset = (floorAtLevel.Transform.Origin.Z + floorAtLevel.Thickness) - lvl.Transform.Origin.Z;
                    if (floorFaceOffset > 0.001)
                    {
                        lvl.Transform.Concatenate(new Transform(0, 0, floorFaceOffset));
                        lvl.Height -= floorFaceOffset;
                    }
                }
            }
        }

        /// <summary>
        /// A modified version of Line.ExtendTo that provides information about the direction of the lines that were extended to.
        /// </summary>
        public static Line ExtendToWithEndInfo(this Line line, IEnumerable<Line> otherLines, double maxDistance, out Vector3 dirAtStart, out Vector3 dirAtEnd, bool bothSides = true, bool extendToFurthest = false, double tolerance = Vector3.EPSILON)
        {
            // this test line — inset slightly from the line — helps treat the ends as valid intersection points, to prevent
            // extension beyond an immediate intersection.
            var testLine = new Line(line.PointAt(0.001), line.PointAt(0.999));
            var intersectionsForLine = new List<(Vector3 pt, Vector3 dir)>();
            foreach (var segment in otherLines)
            {
                bool pointAdded = false;
                // Special case for parallel + collinear lines:
                // ____   |__________
                // We want to extend only to the first corner of the other lines,
                // not all the way through to the other end
                if (segment.Direction().IsParallelTo(testLine.Direction(), tolerance) && // if the two lines are parallel
                    (new[] { segment.End, segment.Start, testLine.Start, testLine.End }).AreCollinear())// and collinear
                {
                    if (!line.PointOnLine(segment.End, true))
                    {
                        intersectionsForLine.Add((segment.End, segment.Direction()));
                        pointAdded = true;
                    }

                    if (!line.PointOnLine(segment.Start, true))
                    {
                        intersectionsForLine.Add((segment.Start, segment.Direction()));
                        pointAdded = true;
                    }
                }
                if (extendToFurthest || !pointAdded)
                {
                    var intersects = testLine.Intersects(segment, out Vector3 intersection, true, true);

                    // if the intersection lies on the obstruction, but is beyond the segment, we collect it
                    if (segment.PointOnLine(intersection, true) && !testLine.PointOnLine(intersection, true))
                    {
                        intersectionsForLine.Add((intersection, segment.Direction()));
                    }
                }
            }

            var dir = line.Direction();
            var intersectionsOrdered = intersectionsForLine.OrderBy(i => (testLine.Start - i.pt).Dot(dir));

            var start = line.Start;
            var end = line.End;
            dirAtStart = line.Direction();
            dirAtEnd = line.Direction();

            var startCandidates = intersectionsOrdered
                    .Where(i => (testLine.Start - i.pt).Dot(dir) > 0);

            var endCandidates = intersectionsOrdered
                .Where(i => (testLine.Start - i.pt).Dot(dir) < testLine.Length() * -1)
                .Reverse();

            ((Vector3 pt, Vector3 dir) Start, (Vector3 pt, Vector3 dir) End) startEndCandidates = extendToFurthest ?
                (startCandidates.LastOrDefault(p => p.pt.DistanceTo(start) < maxDistance), endCandidates.LastOrDefault(p => p.pt.DistanceTo(end) < maxDistance)) :
                (startCandidates.FirstOrDefault(p => p.pt.DistanceTo(start) < maxDistance), endCandidates.FirstOrDefault(p => p.pt.DistanceTo(end) < maxDistance));

            if (bothSides && startEndCandidates.Start != default((Vector3 pt, Vector3 dir)))
            {
                start = startEndCandidates.Start.pt;
                dirAtStart = startEndCandidates.Start.dir;
            }
            if (startEndCandidates.End != default((Vector3 pt, Vector3 dir)))
            {
                end = startEndCandidates.End.pt;
                dirAtEnd = startEndCandidates.End.dir;
            }

            return new Line(start, end);
        }
        private static Line Extend(this Line l, double amt)
        {
            var dir = l.Direction().Unitized();
            return new Line(l.Start - dir * amt, l.End + dir * amt);
        }
    }
}