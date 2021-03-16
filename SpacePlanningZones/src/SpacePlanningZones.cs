using Elements;
using Elements.Geometry;
using Elements.Geometry.Solids;
using Elements.Spatial;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SpacePlanningZones
{
    public static class SpacePlanningZones
    {
        /// <summary>
        /// The SpacePlanningZones function.
        /// </summary>
        /// <param name="model">The input model.</param>
        /// <param name="input">The arguments to the execution.</param>
        /// <returns>A SpacePlanningZonesOutputs instance containing computed results and the model with any new elements.</returns>
        public static SpacePlanningZonesOutputs Execute(Dictionary<string, Model> inputModels, SpacePlanningZonesInputs input)
        {
            var corridorWidth = input.CorridorWidth;
            var corridorMat = SpaceBoundary.MaterialDict["Circulation"];

            var output = new SpacePlanningZonesOutputs();
            var levelsModel = inputModels["Levels"];
            var levelVolumes = levelsModel.AllElementsOfType<LevelVolume>();
            inputModels.TryGetValue("Floors", out var floorsModel);
            var coresModel = inputModels["Core"];
            var cores = coresModel.AllElementsOfType<ServiceCore>();
            var random = new Random(5);
            var levels = new List<LevelElements>();
            var levelMappings = new Dictionary<Guid, (SpaceBoundary boundary, LevelElements level)>();
            if (levelVolumes.Count() == 0)
            {
                throw new Exception("This function requires LevelVolumes, produced by functions like \"Simple Levels by Envelope\". Try using a different levels function.");
            }
            if (cores.Count() == 0)
            {
                throw new Exception("No ServiceCore elements were found in the model.");
            }
            foreach (var lvl in levelVolumes)
            {
                var spaceBoundaries = new List<Element>();
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
                List<Profile> corridorProfiles = new List<Profile>();
                var TOO_SHORT = 9;
                var levelBoundary = new Profile(lvl.Profile.Perimeter, lvl.Profile.Voids, Guid.NewGuid(), null);
                var coresInBoundary = cores.Where(c => levelBoundary.Contains(c.Centroid)).ToList();
                foreach (var core in coresInBoundary)
                {
                    levelBoundary.Voids.Add(new Polygon(core.Profile.Perimeter.Vertices).Reversed());
                    levelBoundary.OrientVoids();
                }

                var perimeter = levelBoundary.Perimeter;
                var perimeterSegments = perimeter.Segments();
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
                var shortLength = (validLengths?.FirstOrDefault() ?? 35 / 1.2) * 1.2;
                var longLength = Math.Min(validLengths.SkipLast(1).Last(), 50);
                var shortEdges = new List<Line>();
                var shortEdgeIndices = new List<int>();
                for (int i = 0; i < perimeterSegments.Count(); i++)
                {
                    var start = perimeterAngles[i];
                    var end = perimeterAngles[(i + 1) % perimeterAngles.Count];
                    if (start > 80 && start < 100 && end > 80 && end < 100 && perimeterSegments[i].Length() < longLength)
                    {
                        shortEdges.Add(perimeterSegments[i]);
                        shortEdgeIndices.Add(i);
                    }
                }


                // Single Loaded Zones

                var singleLoadedZones = new List<(Polygon hull, Line centerLine)>();
                var singleLoadedLengthThreshold = input.OuterBandDepth * 2 + corridorWidth * 2 + 5; // (two offsets, two corridors, and a usable space width)
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

                var shortEdgesExtended = shortEdges.Select(l => new Line(l.Start - l.Direction() * 0.2, l.End + l.Direction() * 0.2));
                var longEdges = perimeterSegments.Except(shortEdges);
                var shortEdgeDepth = Math.Max(input.DepthAtEnds, input.OuterBandDepth);
                var longEdgeDepth = input.OuterBandDepth;


                var perimeterMinusSingleLoaded = new List<Profile>();
                perimeterMinusSingleLoaded.AddRange(Profile.Difference(new[] { lvl.Profile }, singleLoadedZones.Select(p => new Profile(p.hull))));
                var innerOffset = perimeterMinusSingleLoaded.SelectMany(p => p.Perimeter.Offset(-longEdgeDepth));
                var thickerOffsets = shortEdgesExtended.SelectMany(s => s.ToPolyline(1).Offset(shortEdgeDepth, EndType.Butt)).ToList();
                var thickerOffsetProfiles = thickerOffsets.Select(o => new Profile(o.Offset(0.01))).ToList();
                var endOffsetSegments = thickerOffsets.SelectMany(o => o.Segments()).Where(l => innerOffset.Any(o => o.Contains(l.PointAt(0.5))));

                var innerOffsetMinusThicker = innerOffset.SelectMany(i => Polygon.Difference(new[] { i }, thickerOffsets));

                var outerband = new Profile(lvl.Profile.Perimeter, innerOffsetMinusThicker.ToList(), Guid.NewGuid(), null);

                var outerbandLongEdges = Profile.Difference(new List<Profile> { outerband }, thickerOffsetProfiles);
                var ends = Profile.Intersection(new List<Profile> { outerband }, thickerOffsets.Select(o => new Profile(o)).ToList());
                var coreSegments = coresInBoundary.SelectMany(c => c.Profile.Perimeter.Offset((corridorWidth / 2) * 0.999).FirstOrDefault()?.Segments());


                var corridorInset = innerOffsetMinusThicker.Select(p => new Profile(p, p.Offset(-corridorWidth), Guid.NewGuid(), "Corridor"));
                corridorProfiles.AddRange(corridorInset);

                // join single loaded zones to each other (useful in bent-bar case)
                var allCenterLines = singleLoadedZones.ToArray();
                var distanceThreshold = 10.0;
                for (int i = 0; i < allCenterLines.Count(); i++)
                {
                    var crvA = allCenterLines[i].centerLine;
                    for (int j = 0; j < i; j++)
                    {
                        var crvB = allCenterLines[j].centerLine;
                        var doesIntersect = crvA.Intersects(crvB, out var intersection, true, true);
                        Console.WriteLine($"DOES INTERSECT: " + doesIntersect.ToString());

                        var nearPtA = intersection.ClosestPointOn(crvA);
                        var nearPtB = intersection.ClosestPointOn(crvB);
                        if (nearPtA.DistanceTo(intersection) + nearPtB.DistanceTo(intersection) < distanceThreshold)
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

                foreach (var core in coresInBoundary)
                {
                    if (singleLoadedZones.Any(z => z.hull.Covers(core.Profile.Perimeter)))
                    {
                        continue;
                    }
                    var boundary = core.Profile.Perimeter.Offset(corridorWidth / 2.0).FirstOrDefault();
                    var outerOffset = boundary.Offset(corridorWidth).FirstOrDefault();
                    var coreWrap = new Profile(outerOffset, boundary);
                    var coreWrapWithinFloor = Profile.Intersection(new[] { coreWrap }, new[] { levelBoundary });
                    // corridorProfiles.AddRange(coreWrapWithinFloor);
                }

                var extendedLines = new List<Line>();
                var corridorRegions = new List<Polygon>();
                var exclusionRegions = innerOffsetMinusThicker.SelectMany(r => r.Offset(2 * corridorWidth, EndType.Square));
                foreach (var enclosedRegion in innerOffsetMinusThicker)
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
                        extendedLines.Add(extendedSegment);
                        var thickenedCorridor = extendedSegment.ToPolyline(1).Offset(corridorWidth / 2.0, EndType.Butt);
                        var difference = new List<Profile>();

                        difference = Profile.Difference(corridorProfiles, exclusionRegions.Select(r => new Profile(r)));

                        if (difference.Count > 0 && difference.Sum(d => d.Perimeter.Area()) > 10)
                        {
                            corridorProfiles.AddRange(Profile.Intersection(thickenedCorridor.Select(c => new Profile(c)), new[] { levelBoundary }));
                        }
                    }
                }

                var remainingSpaces = Profile.Difference(new[] { levelBoundary }, corridorProfiles);
                foreach (var remainingSpace in remainingSpaces)
                {
                    try
                    {
                        if (remainingSpace.Perimeter.Vertices.Any(v => v.DistanceTo(levelBoundary.Perimeter) < 0.1))
                        {
                            var endCapZones = Profile.Intersection(new[] { remainingSpace }, thickerOffsetProfiles);
                            var linearZones = Profile.Difference(new[] { remainingSpace }, thickerOffsetProfiles);
                            foreach (var linearZone in linearZones)
                            {
                                // spaceBoundaries.Add(SpaceBoundary.Make(linearZone, input.DefaultProgramAssignment, lvl.Transform, lvl.Height));
                                // output.Model.AddElements(linearZone.Segments().Where(s => s.Length() > 2).Select(s => new ModelCurve(s, BuiltInMaterials.YAxis, new Transform(0, 0, 10))));
                                var segmentsExtended = new List<Polyline>();
                                foreach (var line in linearZone.Segments())
                                {
                                    if (line.Length() < 2) continue;
                                    var l = new Line(line.Start - line.Direction() * 0.1, line.End + line.Direction() * 0.1);
                                    var extended = l.ExtendTo(linearZone);
                                    var endDistance = extended.End.DistanceTo(l.End);
                                    var startDistance = extended.Start.DistanceTo(l.Start);
                                    var maxExtension = Math.Max(input.OuterBandDepth, input.DepthAtEnds) * 1.6;
                                    if (startDistance > 0.1 && startDistance < maxExtension)
                                    {
                                        var startLine = new Line(extended.Start, line.Start);
                                        segmentsExtended.Add(startLine.ToPolyline(1));
                                    }
                                    if (endDistance > 0.1 && endDistance < maxExtension)
                                    {
                                        var endLine = new Line(extended.End, line.End);
                                        segmentsExtended.Add(endLine.ToPolyline(1));
                                    }
                                    // if (startDistance > maxExtension || endDistance > maxExtension) continue;
                                    // var pl = new Line(extended.Start - extended.Direction() * 0.1, extended.End + extended.Direction() * 0.1).ToPolyline(1);
                                    // segmentsExtended.Add(pl);
                                }
                                // output.Model.AddElements(segmentsExtended.Select(s => new ModelCurve(s, BuiltInMaterials.YAxis)));
                                // output.Model.AddElements(segmentsExtended.Select(s => new ModelCurve(s, BuiltInMaterials.YAxis, lvl.Transform.Concatenated(new Transform(0, 0, lvl.Height)))));
                                // var segmentsExtended = linearZone.Segments()
                                // .Where(s => s.Length() > 2)
                                // .Select(s => new Line(s.Start - s.Direction() * 0.1, s.End + s.Direction() * 0.1))
                                // .Select(s => s.ExtendTo(linearZone))
                                // .Select(s => new Line(s.Start - s.Direction() * 0.1, s.End + s.Direction() * 0.1).ToPolyline(1));
                                Console.WriteLine(JsonConvert.SerializeObject(linearZone.Perimeter));
                                Console.WriteLine(JsonConvert.SerializeObject(linearZone.Voids));
                                Console.WriteLine(JsonConvert.SerializeObject(segmentsExtended));
                                var splits = Profile.Split(new[] { linearZone }, segmentsExtended, Vector3.EPSILON);
                                // output.Model.AddElements(segmentsExtended.Select(s => new ModelCurve(s, random.NextMaterial(), lvl.Transform.Concatenated(new Transform(0, 0, 0.3)))));
                                spaceBoundaries.AddRange(splits.Select(s => SpaceBoundary.Make(s, input.DefaultProgramAssignment, lvl.Transform, lvl.Height)));
                                // output.Model.AddElements(splits.Select(s => new Floor(s, 0.2, lvl.Transform, random.NextMaterial(false))));
                            }
                            spaceBoundaries.AddRange(endCapZones.Select(s => SpaceBoundary.Make(s, input.DefaultProgramAssignment, lvl.Transform, lvl.Height)));
                            // output.Model.AddElements(endCapZones.Select(z => new Floor(z, 0.2, lvl.Transform, BuiltInMaterials.ZAxis)));
                            // output.Model.AddElements(linearZones.Select(z => new Floor(z, 0.2, lvl.Transform, BuiltInMaterials.YAxis)));
                        }
                        else
                        {
                            spaceBoundaries.Add(SpaceBoundary.Make(remainingSpace, input.DefaultProgramAssignment, lvl.Transform, lvl.Height));
                            // output.Model.AddElement(new Floor(remainingSpace, 0.2, lvl.Transform, random.NextMaterial(false)));
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("🚨");
                        Console.WriteLine(e.Message);
                        Console.WriteLine(e.StackTrace);
                        spaceBoundaries.Add(SpaceBoundary.Make(remainingSpace, input.DefaultProgramAssignment, lvl.Transform, lvl.Height));
                        // output.Model.AddElement(new Floor(remainingSpace, 0.2, lvl.Transform, BuiltInMaterials.XAxis));
                    }
                }
                var level = new LevelElements(new List<Element>(), Guid.NewGuid(), lvl.Name);
                levels.Add(level);

                foreach (var pt in input.AdditionalCorridorLocations)
                {
                    SplitZones(input, corridorWidth, lvl, spaceBoundaries, corridorProfiles, pt);
                }

                foreach (var pt in input.ManualSplitLocations)
                {
                    SplitZones(input, corridorWidth, lvl, spaceBoundaries, corridorProfiles, pt, false);
                }

                foreach (SpaceBoundary b in spaceBoundaries)
                {
                    levelMappings.Add(b.Id, (b, level));
                    output.Model.AddElements(b.Boundary.ToModelCurves(b.Transform.Concatenated(new Transform(0, 0, 0.03))));
                }
                corridorProfiles.Select(p => new Floor(p, 0.1, lvl.Transform, corridorMat)).ToList().ForEach(f => level.Elements.Add(f));
            }
            List<SpaceBoundary> SubdividedBoundaries = new List<SpaceBoundary>();
            if (input.Overrides != null && input.Overrides.ProgramAssignments.Count > 0)
            {
                var spaceBoundaries = levelMappings.Select(kvp => kvp.Value);
                foreach (var overrideValue in input.Overrides.ProgramAssignments.Where(o => o.Identity.IndividualCentroid.IsAlmostEqualTo(o.Identity.ParentCentroid)))
                {
                    var centroid = overrideValue.Identity.ParentCentroid;
                    var matchingSB = spaceBoundaries
                        .OrderBy(sb => sb.boundary.Transform.OfPoint(sb.boundary.Boundary.Perimeter.Centroid()).DistanceTo(centroid))
                        .FirstOrDefault(sb => sb.boundary.Transform.OfPoint(sb.boundary.Boundary.Perimeter.Centroid()).DistanceTo(centroid) < 2.0);
                    // var matchingSB = spaceBoundaries.FirstOrDefault(sb => sb.boundary.Transform.OfPoint(sb.boundary.Boundary.Perimeter.Centroid()).DistanceTo(centroid) < 0.01);
                    var allMatchingSBs = spaceBoundaries
                        .OrderBy(sb => sb.boundary.Transform.OfPoint(sb.boundary.Boundary.Perimeter.Centroid()).DistanceTo(centroid))
                        .Where(sb => sb.boundary.Transform.OfPoint(sb.boundary.Boundary.Perimeter.Centroid()).DistanceTo(centroid) < 2.0);
                    if (matchingSB.boundary != null)
                    {
                        if (overrideValue.Value.Split <= 1)
                        {
                            matchingSB.boundary.SetProgram(overrideValue.Value.ProgramType ?? input.DefaultProgramAssignment);
                            Identity.AddOverrideIdentity(matchingSB.boundary, "Program Assignments", overrideValue.Id, overrideValue.Identity);
                        }
                        else
                        {
                            levelMappings.Remove(matchingSB.boundary.Id);
                            var boundaries = new List<Polygon>(matchingSB.boundary.Boundary.Voids) { matchingSB.boundary.Boundary.Perimeter };
                            var guideVector = GetDominantAxis(boundaries.SelectMany(b => b.Segments()));
                            var alignmentXform = new Transform(boundaries[0].Start, guideVector, Vector3.ZAxis);
                            var grid = new Grid2d(boundaries, alignmentXform);
                            grid.U.DivideByCount(Math.Max(overrideValue.Value.Split, 1));
                            output.Model.AddElements(grid.GetCellSeparators(GridDirection.V, false).Select(c => new ModelCurve(c as Line, new Material("Grey", new Color(0.3, 0.3, 0.3, 1)), matchingSB.boundary.Transform.Concatenated(new Transform(0, 0, 0.02)))));
                            foreach (var cell in grid.GetCells().SelectMany(c => c.GetTrimmedCellGeometry()))
                            {
                                var rep = matchingSB.boundary.Representation.SolidOperations.OfType<Extrude>().First();
                                var newCellSb = SpaceBoundary.Make(cell as Polygon, overrideValue.Value.ProgramType ?? input.DefaultProgramAssignment, matchingSB.boundary.Transform, rep.Height, matchingSB.boundary.AdditionalProperties["ParentCentroid"] as Vector3?);
                                Identity.AddOverrideIdentity(newCellSb, "Program Assignments", overrideValue.Id, overrideValue.Identity);
                                newCellSb.AdditionalProperties["Split"] = overrideValue.Value.Split;
                                SubdividedBoundaries.Add(newCellSb);
                                levelMappings.Add(newCellSb.Id, (newCellSb, matchingSB.level));
                            }
                        }
                    }
                }

                foreach (var overrideValue in input.Overrides.ProgramAssignments.Where(o => !o.Identity.IndividualCentroid.IsAlmostEqualTo(o.Identity.ParentCentroid)))
                {
                    var matchingCell = SubdividedBoundaries.FirstOrDefault(b => (b.AdditionalProperties["IndividualCentroid"] as Vector3?)?.DistanceTo(overrideValue.Identity.IndividualCentroid) < 0.01);
                    if (matchingCell != null)
                    {
                        Identity.AddOverrideIdentity(matchingCell, "Program Assignments", overrideValue.Id, overrideValue.Identity);
                        matchingCell.SetProgram(overrideValue.Value.ProgramType);
                    }
                }
            }
            foreach (var levelMapping in levelMappings)
            {
                levelMapping.Value.level.Elements.Add(levelMapping.Value.boundary);
            }
            Dictionary<string, AreaTally> areas = new Dictionary<string, AreaTally>();
            foreach (var sb in levels.SelectMany(lev => lev.Elements.OfType<SpaceBoundary>()))
            {
                var area = sb.Boundary.Area();
                if (sb.Name == null)
                {
                    continue;
                }
                if (!areas.ContainsKey(sb.Name))
                {
                    areas[sb.Name] = new AreaTally(sb.Name, sb.Material.Color, area, area, 1, null, Guid.NewGuid(), sb.Name);
                }
                else
                {
                    var existingTally = areas[sb.Name];
                    existingTally.AchievedArea += area;
                    existingTally.AreaTarget += area;
                    existingTally.DistinctAreaCount++;
                }
            }
            output.Model.AddElements(areas.Select(kvp => kvp.Value).OrderByDescending(a => a.AchievedArea));
            output.Model.AddElements(levels);
            return output;
        }

        private static void SplitZones(SpacePlanningZonesInputs input, double corridorWidth, LevelVolume lvl, List<Element> spaceBoundaries, List<Profile> corridorProfiles, Vector3 pt, bool addCorridor = true)
        {
            var containingBoundary = spaceBoundaries.OfType<SpaceBoundary>().FirstOrDefault(b => b.Boundary.Contains(pt));
            if (containingBoundary != null)
            {
                if (input.Overrides != null)
                {
                    var spaceOverrides = input.Overrides.ProgramAssignments.FirstOrDefault(s => s.Identity.IndividualCentroid.IsAlmostEqualTo(containingBoundary.Boundary.Perimeter.Centroid()));
                    if (spaceOverrides != null)
                    {
                        containingBoundary.Name = spaceOverrides.Value.ProgramType;
                    }
                }
                spaceBoundaries.Remove(containingBoundary);
                var perim = containingBoundary.Boundary.Perimeter;
                pt.DistanceTo(perim, out var cp);
                var line = new Line(pt, cp);
                var extension = line.ExtendTo(containingBoundary.Boundary);
                List<Profile> newSbs = new List<Profile>();
                if (addCorridor)
                {
                    var corridorShape = extension.ToPolyline(1).Offset(corridorWidth / 2, EndType.Square);
                    var csAsProfiles = corridorShape.Select(s => new Profile(s));
                    var corridorShapesIntersected = Profile.Intersection(new[] { containingBoundary.Boundary }, csAsProfiles);
                    corridorProfiles.AddRange(corridorShapesIntersected);
                    newSbs = Profile.Difference(new[] { containingBoundary.Boundary }, csAsProfiles);
                }
                else
                {
                    newSbs = Profile.Split(new[] { containingBoundary.Boundary }, new[] { extension.ToPolyline(1) }, Vector3.EPSILON);
                }
                spaceBoundaries.AddRange(newSbs.Select(p => SpaceBoundary.Make(p, containingBoundary.Name, containingBoundary.Transform, lvl.Height)));
            }
        }

        private static Vector3 GetDominantAxis(IEnumerable<Line> allLines, Model model = null)
        {
            var refVec = new Vector3(1, 0, 0);
            var lengthByAngle = new Dictionary<double, (double length, IEnumerable<double> angles)>();
            var matsByAngle = new Dictionary<double, Material>();
            foreach (var line in allLines)
            {
                var wallDir = line.Direction();
                var trueAngle = refVec.PlaneAngleTo(wallDir) % 180;
                var angle = Math.Round(trueAngle);
                if (!lengthByAngle.ContainsKey(angle))
                {
                    lengthByAngle[angle] = (line.Length(), new[] { trueAngle });
                    // matsByAngle[angle] = new Material(angle.ToString(), HlsToColor(angle, 0.5, 1));
                    // model?.AddElement(new ModelCurve(line, matsByAngle[angle]));
                }
                else
                {
                    var existingRecord = lengthByAngle[angle];
                    lengthByAngle[angle] = (existingRecord.length + line.Length(), existingRecord.angles.Union(new[] { trueAngle }));
                    // model?.AddElement(new ModelCurve(line, matsByAngle[angle]));
                }
            }
            var dominantAngle = lengthByAngle.ToArray().OrderByDescending(kvp => kvp.Value).First().Value.angles.Average();
            var rotation = new Transform();
            rotation.Rotate(dominantAngle);
            return rotation.OfVector(refVec);
        }
    }
}