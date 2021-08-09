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
            var hasCore = inputModels.TryGetValue("Core", out var coresModel);
            var cores = coresModel?.AllElementsOfType<ServiceCore>() ?? new List<ServiceCore>();

            var hasProgramRequirements = inputModels.TryGetValue("Program Requirements", out var programReqsModel);
            var programReqs = programReqsModel?.AllElementsOfType<ProgramRequirement>();

            SpaceBoundary.Reset();
            if (programReqs != null && programReqs.Count() > 0)
            {
                SpaceBoundary.SetRequirements(programReqs);
            }

            var random = new Random(5);
            var levels = new List<LevelElements>();
            // var levelMappings = new Dictionary<Guid, (SpaceBoundary boundary, LevelElements level)>();
            var allSpaceBoundaries = new List<SpaceBoundary>();
            if (levelVolumes.Count() == 0)
            {
                output.Warnings.Add("This function requires LevelVolumes, produced by functions like \"Simple Levels by Envelope\". Try a different levels function.");
                return output;
            }
            foreach (var lvl in levelVolumes)
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
                var levelBoundary = new Profile(lvl.Profile.Perimeter, lvl.Profile.Voids, Guid.NewGuid(), null);
                var coresInBoundary = cores.Where(c => levelBoundary.Contains(c.Centroid)).ToList();
                foreach (var core in coresInBoundary)
                {
                    levelBoundary.Voids.Add(new Polygon(core.Profile.Perimeter.Vertices).Reversed());
                    levelBoundary.OrientVoids();
                }

                var spaceBoundaries = new List<Element>();
                List<Profile> corridorProfiles = new List<Profile>();
                if (input.CirculationMode == SpacePlanningZonesInputsCirculationMode.Automatic)
                {
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

                    SplitCornersAndGenerateSpaceBoundaries(spaceBoundaries, input, lvl, corridorProfiles, levelBoundary, thickerOffsetProfiles);
                }
                else if (input.CirculationMode == SpacePlanningZonesInputsCirculationMode.Manual)
                {
                    if (input.Corridors != null && input.Corridors.Count > 0)
                    {
                        var corridorProfilesForUnion = new List<Profile>();
                        foreach (var corridorPolyline in input.Corridors)
                        {
                            if (corridorPolyline == null || corridorPolyline.Polyline == null)
                            {
                                continue;
                            }
                            var corrPgons = corridorPolyline.Polyline.OffsetOnSide(corridorPolyline.Width, corridorPolyline.Flip);
                            corridorProfilesForUnion.AddRange(corrPgons.Select(p => new Profile(p)));
                        }
                        corridorProfiles = Profile.UnionAll(corridorProfilesForUnion);
                    }
                    SplitCornersAndGenerateSpaceBoundaries(spaceBoundaries, input, lvl, corridorProfiles, levelBoundary);
                }

                // Construct Level 
                var level = new LevelElements(new List<Element>(), Guid.NewGuid(), lvl.Name);
                levels.Add(level);

                // These are the new, correct methods using the split inputs: 

                // Create snapping geometry for corridors
                foreach (var sb in spaceBoundaries)
                {
                    var boundary = sb as SpaceBoundary;
                    output.Model.AddElement(new PolygonReference(boundary.Boundary.Perimeter, Guid.NewGuid(), "corridors"));
                    if ((boundary.Boundary.Voids?.Count() ?? 0) > 0)
                    {
                        boundary.Boundary.Voids.ToList().ForEach(v => output.Model.AddElement(new PolygonReference(v, Guid.NewGuid(), "corridors")));
                    }
                }

                //Manual Corridor Splits
                foreach (var pt in input.AddCorridors.SplitLocations)
                {
                    SplitZones(input, corridorWidth, lvl, spaceBoundaries, corridorProfiles, pt);
                }
                //Create snapping geometry for splits
                foreach (var sb in spaceBoundaries)
                {
                    var boundary = sb as SpaceBoundary;
                    output.Model.AddElement(new PolygonReference(boundary.Boundary.Perimeter, Guid.NewGuid(), "splits"));
                    if ((boundary.Boundary.Voids?.Count() ?? 0) > 0)
                    {
                        boundary.Boundary.Voids.ToList().ForEach(v => output.Model.AddElement(new PolygonReference(v, Guid.NewGuid(), "splits")));
                    }
                }

                // Manual Split Locations
                foreach (var pt in input.SplitZones.SplitLocations)
                {
                    SplitZones(input, corridorWidth, lvl, spaceBoundaries, corridorProfiles, pt, false);
                }

                // These are the old style methods, just left in place for backwards compatibility. 
                // Most of the time we expect these values to be empty.
                // Manual Corridor Splits
                foreach (var pt in input.AdditionalCorridorLocations)
                {
                    SplitZones(input, corridorWidth, lvl, spaceBoundaries, corridorProfiles, pt);
                }

                // Manual Split Locations
                foreach (var pt in input.ManualSplitLocations)
                {
                    SplitZones(input, corridorWidth, lvl, spaceBoundaries, corridorProfiles, pt, false);
                }

                foreach (SpaceBoundary b in spaceBoundaries)
                {
                    b.Level = level;
                }
                allSpaceBoundaries.AddRange(spaceBoundaries.OfType<SpaceBoundary>());
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
            List<SpaceBoundary> SubdividedBoundaries = new List<SpaceBoundary>();

            // merge overrides
            if (input.Overrides != null && input.Overrides.MergeZones != null && input.Overrides.MergeZones.Count > 0)
            {
                foreach (var mz in input.Overrides.MergeZones)
                {
                    var identitiesToMerge = mz.Identities;
                    var matchingSbs = identitiesToMerge.Select(mzI => allSpaceBoundaries.FirstOrDefault(
                        sb => ((Vector3)sb.ParentCentroid).DistanceTo(mzI.ParentCentroid) < 1.0)).Where(s => s != null).ToList();
                    foreach (var msb in matchingSbs)
                    {
                        msb.Remove();
                    }
                    var sbsByLevel = matchingSbs.GroupBy(sb => sb.Level?.Id ?? Guid.Empty);
                    foreach (var lvlGrp in sbsByLevel)
                    {
                        var level = lvlGrp.First().Level;
                        var profiles = lvlGrp.Select(sb => sb.Boundary);
                        var baseobj = lvlGrp.FirstOrDefault(n => n.Name != null && n.Name != "unspecified");
                        var corrSegments = lvlGrp.SelectMany(sb => sb.AdjacentCorridorEdges);
                        if (baseobj == default)
                        {
                            baseobj = lvlGrp.First();
                        }
                        var baseSB = baseobj;
                        var union = Profile.UnionAll(profiles);
                        foreach (var newProfile in union)
                        {
                            var rep = baseSB.Representation.SolidOperations.OfType<Extrude>().First();

                            var newSB = SpaceBoundary.Make(newProfile, baseSB.Name, baseSB.Transform, rep.Height, (Vector3)baseSB.ParentCentroid, (Vector3)baseSB.ParentCentroid, corrSegments);
                            newSB.SetProgram(baseSB.Name);
                            newSB.Level = level;
                        }
                    }
                }
            }

            // assignment overrides for auto-placed spaces
            if (input.Overrides?.ProgramAssignments != null && input.Overrides.ProgramAssignments.Count > 0)
            {
                foreach (var overrideValue in input.Overrides.ProgramAssignments.Where(o => o.Identity.AutoPlaced))
                {
                    // If we've got an override from a space that was autoplaced, 
                    // we have to manually reproduce the split and reinsert the space
                    if (overrideValue.Identity.AutoPlaced)
                    {
                        var frontEdge = overrideValue.Identity.AlignmentEdge;
                        var matchingSb = allSpaceBoundaries.OrderBy(s => s.ParentCentroid.Value.DistanceTo(overrideValue.Identity.ParentCentroid)).First();
                        allSpaceBoundaries.Remove(matchingSb);
                        matchingSb.Remove();
                        var newZone = SplitZone(matchingSb, frontEdge, overrideValue.Identity.Boundary, out List<SpaceBoundary> remainderZones);
                        remainderZones.ForEach((z) =>
                        {
                            z.Level = matchingSb.Level;
                        });
                        allSpaceBoundaries.AddRange(remainderZones);
                        Identity.AddOverrideIdentity(newZone, "Program Assignments", overrideValue.Id, overrideValue.Identity);
                        newZone.SetProgram(overrideValue.Value.ProgramType);
                        newZone.Level = matchingSb.Level;
                        allSpaceBoundaries.Add(newZone);
                    }
                }
            }

            // assignment overrides for regular spaces
            if (input.Overrides != null && input.Overrides.ProgramAssignments != null && input.Overrides.ProgramAssignments.Count > 0)
            {
                // overrides where it is its own parent (which should be all of them now that we don't have a "split" override)
                foreach (var overrideValue in input.Overrides.ProgramAssignments.Where(o => !o.Identity.AutoPlaced && o.Identity.IndividualCentroid.IsAlmostEqualTo(o.Identity.ParentCentroid)))
                {
                    var centroid = overrideValue.Identity.ParentCentroid;
                    var matchingSB = allSpaceBoundaries
                        .OrderBy(sb => sb.IndividualCentroid.Value.DistanceTo(centroid))
                        .FirstOrDefault(sb => sb.IndividualCentroid.Value.DistanceTo(centroid) < 2.0);
                    // var allMatchingSBs = spaceBoundaries
                    //     .OrderBy(sb => sb.boundary.Transform.OfPoint(sb.boundary.Boundary.Perimeter.Centroid()).DistanceTo(centroid))
                    //     .Where(sb => sb.boundary.Transform.OfPoint(sb.boundary.Boundary.Perimeter.Centroid()).DistanceTo(centroid) < 2.0);
                    if (matchingSB != null)
                    {
                        if (overrideValue.Value.Split <= 1)
                        {
                            matchingSB.SetProgram(overrideValue.Value.ProgramType ?? input.DefaultProgramAssignment);
                            Identity.AddOverrideIdentity(matchingSB, "Program Assignments", overrideValue.Id, overrideValue.Identity);
                        }
                        else // Split input on overrides is now deprecated
                        {
                            var level = matchingSB.Level;
                            matchingSB.Remove();
                            var boundaries = new List<Polygon>(matchingSB.Boundary.Voids) { matchingSB.Boundary.Perimeter };
                            var guideVector = GetDominantAxis(boundaries.SelectMany(b => b.Segments()));
                            var alignmentXform = new Transform(boundaries[0].Start, guideVector, Vector3.ZAxis);
                            var grid = new Grid2d(boundaries, alignmentXform);
                            grid.U.DivideByCount(Math.Max(overrideValue.Value.Split, 1));
                            foreach (var cell in grid.GetCells().SelectMany(c => c.GetTrimmedCellGeometry()))
                            {
                                var rep = matchingSB.Representation.SolidOperations.OfType<Extrude>().First();
                                var newCellSb = SpaceBoundary.Make(cell as Polygon, overrideValue.Value.ProgramType ?? input.DefaultProgramAssignment, matchingSB.Transform, rep.Height, matchingSB.ParentCentroid);
                                Identity.AddOverrideIdentity(newCellSb, "Program Assignments", overrideValue.Id, overrideValue.Identity);
                                newCellSb.AdditionalProperties["Split"] = overrideValue.Value.Split;
                                SubdividedBoundaries.Add(newCellSb);
                                newCellSb.Level = level;
                            }
                        }
                    }
                }
                // overrides where it's not its own parent
                foreach (var overrideValue in input.Overrides.ProgramAssignments.Where(o => !o.Identity.AutoPlaced && !o.Identity.IndividualCentroid.IsAlmostEqualTo(o.Identity.ParentCentroid)))
                {
                    var matchingCell = SubdividedBoundaries.FirstOrDefault(b => (b.IndividualCentroid as Vector3?)?.DistanceTo(overrideValue.Identity.IndividualCentroid) < 0.01);
                    if (matchingCell != null)
                    {
                        Identity.AddOverrideIdentity(matchingCell, "Program Assignments", overrideValue.Id, overrideValue.Identity);
                        matchingCell.SetProgram(overrideValue.Value.ProgramType);
                    }
                }
            }

            Dictionary<string, AreaTally> areas = new Dictionary<string, AreaTally>();

            // AutoLayout
            if (hasProgramRequirements)
            {
                AutoLayoutProgram(allSpaceBoundaries, programReqs, input.DefaultProgramAssignment, output.Model);
            }


            foreach (var sb in allSpaceBoundaries)
            {
                // ignore skinny spaces
                var minDepth = 1.0;
                if ((sb.Depth ?? 10) < minDepth || (sb.Length ?? 10) < minDepth)
                {
                    continue;
                }
                // set levels
                sb.Level.Elements.Add(sb);
                // we have to make the internal level to be null to avoid a recursive infinite loop when we serialize
                sb.Level = null;
            }

            foreach (var sb in allSpaceBoundaries)
            {
                var area = sb.Boundary.Area();
                if (sb.Name == null)
                {
                    continue;
                }
                if (!areas.ContainsKey(sb.ProgramName))
                {
                    var areaTarget = SpaceBoundary.Requirements.TryGetValue(sb.ProgramName, out var req) ? req.AreaPerSpace * req.SpaceCount : 0.0;
                    areas[sb.Name] = new AreaTally(sb.ProgramName, sb.Material.Color, areaTarget, area, 1, null, Guid.NewGuid(), sb.Name);
                }
                else
                {
                    var existingTally = areas[sb.Name];
                    existingTally.AchievedArea += area;
                    existingTally.DistinctAreaCount++;
                }
                output.Model.AddElements(sb.Boundary.ToModelCurves(sb.Transform.Concatenated(new Transform(0, 0, 0.03))));
            }
            // count corridors in area
            var circulationKey = "Circulation";
            var circReq = SpaceBoundary.Requirements.ToList().FirstOrDefault(k => k.Value.Name == "Circulation");
            if (circReq.Key != null)
            {
                circulationKey = circReq.Value.ProgramName;
            }

            foreach (var corridorFloor in levels.SelectMany(lev => lev.Elements.OfType<Floor>()))
            {
                if (!areas.ContainsKey(circulationKey))
                {
                    areas[circulationKey] = new AreaTally(circulationKey, corridorFloor.Material.Color, circReq.Value?.AreaPerSpace ?? 0, corridorFloor.Area(), 1, null, Guid.NewGuid(), circulationKey);
                }
            }

            output.Model.AddElements(areas.Select(kvp => kvp.Value).OrderByDescending(a => a.AchievedArea));
            output.Model.AddElements(levels);

            return output;
        }

        private static void AutoLayoutProgram(List<SpaceBoundary> allSpaceBoundaries, IEnumerable<ProgramRequirement> programReqs, string defaultAssignment, Model model = null)
        {
            var boundaries = new List<SpaceBoundary>();
            foreach (var boundary in allSpaceBoundaries.Where(sb => sb.ProgramName == "unspecified" || sb.ProgramName == defaultAssignment))
            {
                var adjacentEdges = boundary.AdjacentCorridorEdges;
                Line alignmentEdge = null;
                // if we're not adjacent to a corridor, use the longest edge
                if (adjacentEdges == null || adjacentEdges.Count() == 0)
                {
                    alignmentEdge = boundary.Boundary.Perimeter.Segments().OrderBy(s => s.Length()).Last();
                }
                else
                {
                    // otherwise use the longest edge that is adjacent to a corridor
                    alignmentEdge = adjacentEdges.OrderBy(e => e.Length()).Last();
                }
                var alignmentVector = alignmentEdge.Direction();
                var alignmentTransform = new Transform(alignmentEdge.Start, alignmentVector, Vector3.ZAxis);
                var inverse = new Transform(alignmentTransform);
                inverse.Invert();
                var transformedProfile = boundary.Boundary.Perimeter.TransformedPolygon(inverse);
                var bbox = new BBox3(transformedProfile);
                var length = bbox.Max.X - bbox.Min.X;
                var depth = bbox.Max.Y - bbox.Min.Y;
                boundary.Length = length;
                boundary.Depth = depth;
                boundary.AvailableLength = length;
                inverse.Concatenate(new Transform(-bbox.Min.X, 0, 0));
                boundary.ToAlignmentEdge = new Transform(inverse);
                boundary.FromAlignmentEdge = new Transform(inverse);
                boundary.FromAlignmentEdge.Invert();
                boundary.AlignmentEdge = new Line(new Vector3(0, 0), new Vector3(length, 0)).TransformedLine(boundary.FromAlignmentEdge);
                boundaries.Add(boundary);
            }

            // just a preview arranging all the spaces with corridor edge along the x axis, for
            // debugging purposes
            // var runningXLocation = 100.0;

            // foreach (var sb in boundaries.OrderByDescending(sb => sb.Depth))
            // {
            //     var positionTransform = new Transform(runningXLocation, 0, 0);
            //     runningXLocation += sb.Length.Value;
            //     var transformedProfile = sb.Boundary.Perimeter.TransformedPolygon(sb.ToAlignmentEdge);
            //     model?.AddElement(transformedProfile.TransformedPolygon(positionTransform));
            //     model?.AddElement(new Panel(transformedProfile.TransformedPolygon(positionTransform), BuiltInMaterials.Glass, null, name: sb.Depth.ToString()));
            // }

            // make sure all reqs have depth and width
            foreach (var req in programReqs)
            {
                if (req.HyparSpaceType == "Circulation")
                {
                    continue;
                }
                if ((req.Width != null && req.Width != 0) && (req.Depth != null && req.Depth != 0))
                {
                    // just hold on to existing width / depth
                }
                else if ((req.Width == null || req.Width == 0) && (req.Depth == null || req.Depth == 0) && req.AreaPerSpace != 0)
                {
                    req.Depth = Math.Sqrt(req.AreaPerSpace);
                    req.Width = Math.Sqrt(req.AreaPerSpace);
                }
                else if ((req.Width != null && req.Width != 0) && req.AreaPerSpace != 0)
                {
                    req.Depth = req.AreaPerSpace / req.Width;
                }
                else if ((req.Depth != null && req.Depth != 0) && req.AreaPerSpace != 0)
                {
                    req.Width = req.AreaPerSpace / req.Depth;
                }
                else
                {
                    req.Width = 3;
                    req.Depth = 3;
                }
            }
            var minDimTolerance = 1.1; // allow spaces that are *slightly* too small for the program size.

            foreach (var req in programReqs.OrderByDescending(r => r.Depth))
            {
                var remainingToPlace = req.RemainingToPlace;
                for (int i = 0; i < remainingToPlace; i++)
                {
                    var candidateSpaces = boundaries.Where(b => b.Depth * minDimTolerance > req.Depth && b.AvailableLength * minDimTolerance > req.Width).OrderBy(b => Math.Abs(b.Depth.Value - req.Depth.Value));

                    if (candidateSpaces.Count() > 0)
                    {
                        candidateSpaces.First().Collect(req);
                    }
                }
            }
            foreach (var boundary in boundaries)
            {
                if (boundary.CollectedSpaces.Count > 0)
                {
                    var newSpaces = boundary.ResolveCollected();
                    allSpaceBoundaries.Remove(boundary);
                    allSpaceBoundaries.AddRange(newSpaces);
                    newSpaces.ForEach((sb) =>
                    {
                        sb.AutoPlaced = true;
                    });
                }
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
            var shortLength = (validLengths?.FirstOrDefault() ?? 35 / 1.2) * 1.2;
            var longLength = Math.Min(validLengths.Cast<double?>().SkipLast(1).LastOrDefault<double?>() ?? 50, 50);
            shortEdges = new List<Line>();
            shortEdgeIndices = new List<int>();
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
        }

        private static void GenerateEndZones(SpacePlanningZonesInputs input, double corridorWidth, LevelVolume lvl, List<Profile> corridorProfiles, Line[] perimeterSegments, List<Line> shortEdges, List<(Polygon hull, Line centerLine)> singleLoadedZones, out List<Polygon> thickenedEndsOut, out List<Profile> thickerOffsetProfiles, out IEnumerable<Polygon> innerOffsetMinusThickenedEnds, out IEnumerable<Polygon> exclusionRegions)
        {
            // separate out short and long perimeter edges
            var shortEdgesExtended = shortEdges.Select(l => new Line(l.Start - l.Direction() * 0.2, l.End + l.Direction() * 0.2));
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
            exclusionRegions = innerOffsetMinusThickenedEnds.SelectMany(r => r.Offset(2 * corridorWidth, EndType.Square));

            var corridorInset = innerOffsetMinusThickenedEnds.Select(p => new Profile(p, p.Offset(-corridorWidth), Guid.NewGuid(), "Corridor"));
            corridorProfiles.AddRange(corridorInset);
            thickenedEndsOut = thickenedEnds;
        }

        private static void CorridorsFromCore(double corridorWidth, List<Profile> corridorProfiles, Profile levelBoundary, List<ServiceCore> coresInBoundary, IEnumerable<Polygon> innerOffsetMinusThickenedEnds, IEnumerable<Polygon> exclusionRegions)
        {
            var coreSegments = coresInBoundary.SelectMany(c => c.Profile.Perimeter.Offset((corridorWidth / 2) * 0.999).FirstOrDefault()?.Segments());

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

                    if (difference.Count > 0 && difference.Sum(d => d.Perimeter.Area()) > 10)
                    {
                        corridorProfiles.AddRange(Profile.Intersection(thickenedCorridor.Select(c => new Profile(c)), new[] { levelBoundary }));
                    }
                }
            }
        }

        private static List<Element> SplitCornersAndGenerateSpaceBoundaries(List<Element> spaceBoundaries, SpacePlanningZonesInputs input, LevelVolume lvl, List<Profile> corridorProfiles, Profile levelBoundary, List<Profile> thickerOffsetProfiles = null)
        {
            var corridorSegments = corridorProfiles.SelectMany(c => c.Segments());
            var remainingSpaces = Profile.Difference(new[] { levelBoundary }, corridorProfiles);
            foreach (var remainingSpace in remainingSpaces)
            {
                try
                {
                    if (remainingSpace.Perimeter.Vertices.Any(v => v.DistanceTo(levelBoundary.Perimeter) < 0.1))
                    {
                        var linearZones = thickerOffsetProfiles == null ? new List<Profile> { remainingSpace } : Profile.Difference(new[] { remainingSpace }, thickerOffsetProfiles);
                        foreach (var linearZone in linearZones)
                        {
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
                            }
                            // Console.WriteLine(JsonConvert.SerializeObject(linearZone.Perimeter));
                            // Console.WriteLine(JsonConvert.SerializeObject(linearZone.Voids));
                            // Console.WriteLine(JsonConvert.SerializeObject(segmentsExtended));
                            var splits = Profile.Split(new[] { linearZone }, segmentsExtended, Vector3.EPSILON);
                            spaceBoundaries.AddRange(splits.Select(s => SpaceBoundary.Make(s, input.DefaultProgramAssignment, lvl.Transform, lvl.Height, corridorSegments: corridorSegments)));
                        }
                        if (thickerOffsetProfiles != null)
                        {
                            var endCapZones = Profile.Intersection(new[] { remainingSpace }, thickerOffsetProfiles);
                            spaceBoundaries.AddRange(endCapZones.Select(s => SpaceBoundary.Make(s, input.DefaultProgramAssignment, lvl.Transform, lvl.Height, corridorSegments: corridorSegments)));
                        }
                    }
                    else
                    {
                        spaceBoundaries.Add(SpaceBoundary.Make(remainingSpace, input.DefaultProgramAssignment, lvl.Transform, lvl.Height, corridorSegments: corridorSegments));
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("🚨");
                    Console.WriteLine(e.Message);
                    Console.WriteLine(e.StackTrace);
                    spaceBoundaries.Add(SpaceBoundary.Make(remainingSpace, input.DefaultProgramAssignment, lvl.Transform, lvl.Height, corridorSegments: corridorSegments));
                }
            }
            return spaceBoundaries;
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
            var distanceThreshold = 10.0;
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

            return allCenterLines;
        }

        private static List<(Polygon hull, Line centerLine)> CalculateSingleLoadedZones(SpacePlanningZonesInputs input, double corridorWidth, Line[] perimeterSegments, List<int> shortEdgeIndices)
        {
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

            return singleLoadedZones;
        }

        private static void SplitZones(SpacePlanningZonesInputs input, double corridorWidth, LevelVolume lvl, List<Element> spaceBoundaries, List<Profile> corridorProfiles, Vector3 pt, bool addCorridor = true)
        {
            // this is a hack — we're constructing a new SplitLocations w/ ZAxis as a sentinel meaning "null";
            SplitZones(input, corridorWidth, lvl, spaceBoundaries, corridorProfiles, new SplitLocations(pt, Vector3.ZAxis), addCorridor);
        }
        private static void SplitZones(SpacePlanningZonesInputs input, double corridorWidth, LevelVolume lvl, List<Element> spaceBoundaries, List<Profile> corridorProfiles, SplitLocations pt, bool addCorridor = true)
        {
            var corridorSegments = corridorProfiles.SelectMany(c => c.Segments());
            var containingBoundary = spaceBoundaries.OfType<SpaceBoundary>().FirstOrDefault(b => b.Boundary.Contains(pt.Position));
            if (containingBoundary != null)
            {
                if (input.Overrides?.ProgramAssignments != null)
                {
                    var spaceOverrides = input.Overrides.ProgramAssignments.FirstOrDefault(s => s.Identity.IndividualCentroid.IsAlmostEqualTo(containingBoundary.Boundary.Perimeter.Centroid()));
                    if (spaceOverrides != null)
                    {
                        containingBoundary.Name = spaceOverrides.Value.ProgramType;
                    }
                }
                spaceBoundaries.Remove(containingBoundary);
                containingBoundary.Remove();
                var perim = containingBoundary.Boundary.Perimeter;
                Line line;
                if (pt.Direction == Vector3.ZAxis)
                {
                    pt.Position.DistanceTo(perim as Polyline, out var cp);
                    line = new Line(pt.Position, cp);
                }
                else
                {
                    line = new Line(pt.Position, pt.Position + pt.Direction * 0.1);
                }
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
                spaceBoundaries.AddRange(newSbs.Select(p => SpaceBoundary.Make(p, containingBoundary.Name, containingBoundary.Transform, lvl.Height, corridorSegments: corridorSegments)));
            }
        }


        private static SpaceBoundary SplitZone(SpaceBoundary matchingSb, Line frontEdge, Profile splittingProfile, out List<SpaceBoundary> remainderZones)
        {
            var perpDir = frontEdge.Direction().Cross(Vector3.ZAxis);
            var splitLines = new List<Polyline> {
                new Line(frontEdge.Start - perpDir * 50,frontEdge.Start + perpDir * 50).ToPolyline(1),
                new Line(frontEdge.End - perpDir * 50,frontEdge.End + perpDir * 50).ToPolyline(1),
            };
            var profile = matchingSb.Boundary;
            var splitResults = Profile.Split(new[] { profile }, splitLines, Vector3.EPSILON);
            var midPt = frontEdge.PointAt(0.5);
            var thisSplit = splitResults.OrderBy(s => s.Perimeter.Centroid().DistanceTo(midPt)).First();
            var otherSplits = splitResults.Except(new[] { thisSplit });
            remainderZones = otherSplits.Select((p) =>
                 SpaceBoundary.Make(p, matchingSb.Name, matchingSb.Transform, matchingSb.Representation.SolidOperations.OfType<Extrude>().First().Height, corridorSegments: matchingSb.AdjacentCorridorEdges)
            ).ToList();
            return SpaceBoundary.Make(splittingProfile, matchingSb.Name, matchingSb.Transform, matchingSb.Representation.SolidOperations.OfType<Extrude>().First().Height, corridorSegments: new[] { frontEdge });
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
                }
                else
                {
                    var existingRecord = lengthByAngle[angle];
                    lengthByAngle[angle] = (existingRecord.length + line.Length(), existingRecord.angles.Union(new[] { trueAngle }));
                }
            }
            var dominantAngle = lengthByAngle.ToArray().OrderByDescending(kvp => kvp.Value).First().Value.angles.Average();
            var rotation = new Transform();
            rotation.Rotate(dominantAngle);
            return rotation.OfVector(refVec);
        }
    }
}