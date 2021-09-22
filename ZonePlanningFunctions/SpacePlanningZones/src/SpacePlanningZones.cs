using Elements;
using Elements.Geometry;
using Elements.Geometry.Solids;
using Elements.Serialization.JSON;
using Elements.Spatial;
using Hypar.Optimization;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SpacePlanningZones
{
    public static class SpacePlanningZones
    {
        private static List<string> Warnings = new List<string>();
        /// <summary>
        /// The SpacePlanningZones function.
        /// </summary>
        /// <param name="model">The input model.</param>
        /// <param name="input">The arguments to the execution.</param>
        /// <returns>A SpacePlanningZonesOutputs instance containing computed results and the model with any new elements.</returns>
        public static SpacePlanningZonesOutputs Execute(Dictionary<string, Model> inputModels, SpacePlanningZonesInputs input)
        {
            Warnings.Clear();
            #region Gather Inputs

            // Set up output object
            var output = new SpacePlanningZonesOutputs();

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
            var programReqs = programReqsModel?.AllElementsOfType<ProgramRequirement>();
            var adjacencies = programReqsModel?.AllElementsOfType<ProgramAdjacencyMatrix>();

            // Reset static properties on SpaceBoundary
            SpaceBoundary.Reset();

            // Populate SpaceBoundary's program requirement dictionary with loaded requirements
            if (programReqs != null && programReqs.Count() > 0)
            {
                SpaceBoundary.SetRequirements(programReqs);
            }

            #endregion

            // create a collection of LevelElements (which contain other elements)
            // to add to the model
            var levels = new List<LevelElements>();

            // create a collection of all the final space boundaries we'll pass to the model
            var allSpaceBoundaries = new List<SpaceBoundary>();

            // For every level volume, create space boundaries with corridors and splits
            CreateInitialSpaceBoundaries(input, output, levelVolumes, floorsModel, cores, levels, walls, allSpaceBoundaries);

            // process merge overrides
            ProcessMergeOverrides(input, allSpaceBoundaries);

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


            // process assignment overrides
            ProcessProgramAssignmentOverrides(input, allSpaceBoundaries);

            if (hasProgramRequirements && input.AutomaticallyPlaceProgram)
            {
                foreach (var req in programReqs)
                {
                    if (req.Width == 0)
                    {
                        req.Width = null;
                    }
                    if (req.Depth == 0)
                    {
                        req.Depth = null;
                    }
                }
                AutoLayoutProgram(allSpaceBoundaries, programReqs, adjacencies, input.DefaultProgramAssignment, out var messages, output.Model);
                output.Warnings.AddRange(messages);
            }
            else
            {
                allSpaceBoundaries.ForEach((sb) =>
                {
                    sb.CalculateDimensions();
                });
            }

            // exclude bad spaces and add boundaries to their levels.
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

            // calculate area tallies
            var areas = CalculateAreas(hasProgramRequirements, levels, allSpaceBoundaries);

            output.Model.AddElements(areas.Select(kvp => kvp.Value).OrderByDescending(a => a.AchievedArea));

            // adding levels also adds the space boundaries, since they're in the levels' own elements collections
            output.Model.AddElements(levels);

            output.Warnings.AddRange(Warnings);
            return output;
        }

        private static void ProcessProgramAssignmentOverrides(SpacePlanningZonesInputs input, List<SpaceBoundary> allSpaceBoundaries)
        {
            if (input.Overrides != null && input.Overrides.ProgramAssignments != null && input.Overrides.ProgramAssignments.Count > 0)
            {
                List<SpaceBoundary> SubdividedBoundaries = new List<SpaceBoundary>();
                // overrides where it is its own parent
                foreach (var overrideValue in input.Overrides.ProgramAssignments.Where(o => o.Identity.IndividualCentroid.IsAlmostEqualTo(o.Identity.ParentCentroid)))
                {
                    var centroid = overrideValue.Identity.ParentCentroid;
                    var matchingSB = allSpaceBoundaries
                        .OrderBy(sb => sb.IndividualCentroid.Value.DistanceTo(centroid))
                        .FirstOrDefault(); //sb => sb.IndividualCentroid.Value.DistanceTo(centroid) < 2.0);
                    if (matchingSB != null)
                    {
                        if (overrideValue.Value.Split <= 1)
                        {
                            matchingSB.SetProgram(overrideValue.Value.ProgramType ?? input.DefaultProgramAssignment);
                            Identity.AddOverrideIdentity(matchingSB, "Program Assignments", overrideValue.Id, overrideValue.Identity);
                        }
                        else // Split input on overrides is now deprecated — we shouldn't typically hit this code.
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
                foreach (var overrideValue in input.Overrides.ProgramAssignments.Where(o => !o.Identity.IndividualCentroid.IsAlmostEqualTo(o.Identity.ParentCentroid)))
                {
                    var matchingCell = SubdividedBoundaries.FirstOrDefault(b => b.IndividualCentroid?.DistanceTo(overrideValue.Identity.IndividualCentroid) < 0.01);
                    if (matchingCell != null)
                    {
                        Identity.AddOverrideIdentity(matchingCell, "Program Assignments", overrideValue.Id, overrideValue.Identity);
                        matchingCell.SetProgram(overrideValue.Value.ProgramType);
                    }
                }
            }
        }

        private static void ProcessMergeOverrides(SpacePlanningZonesInputs input, List<SpaceBoundary> allSpaceBoundaries)
        {
            if (input.Overrides != null && input.Overrides.MergeZones != null && input.Overrides.MergeZones.Count > 0)
            {
                foreach (var mz in input.Overrides.MergeZones)
                {
                    var identitiesToMerge = mz.Identities;
                    var matchingSbs = identitiesToMerge.Select(mzI => allSpaceBoundaries.FirstOrDefault(
                        sb => ((Vector3)sb.ParentCentroid).DistanceTo(mzI.ParentCentroid) < 1.0)).Where(s => s != null).ToList();
                    foreach (var msb in matchingSbs)
                    {
                        allSpaceBoundaries.Remove(msb);
                    }
                    var sbsByLevel = matchingSbs.GroupBy(sb => sb.Level?.Id ?? Guid.Empty);
                    foreach (var lvlGrp in sbsByLevel)
                    {
                        var level = lvlGrp.First().Level;
                        var profiles = lvlGrp.Select(sb => sb.Boundary);
                        var baseobj = lvlGrp.FirstOrDefault(n => n.Name != null && n.Name != "unspecified");
                        if (baseobj == default)
                        {
                            baseobj = lvlGrp.First();
                        }
                        var baseSB = baseobj;
                        var union = Profile.UnionAll(profiles);
                        foreach (var newProfile in union)
                        {
                            var rep = baseSB.Representation.SolidOperations.OfType<Extrude>().First();

                            var newSB = SpaceBoundary.Make(newProfile, baseSB.Name, baseSB.Transform, rep.Height, (Vector3)baseSB.ParentCentroid, (Vector3)baseSB.ParentCentroid);
                            newSB.SetProgram(baseSB.Name);
                            Identity.AddOverrideIdentity(newSB, "Merge Zones", mz.Id, mz.Identities[0]);
                            newSB.Level = level;
                            allSpaceBoundaries.Add(newSB);
                        }
                    }
                }
            }
        }

        private static Dictionary<string, AreaTally> CalculateAreas(bool hasProgramRequirements, List<LevelElements> levels, List<SpaceBoundary> allSpaceBoundaries)
        {
            Dictionary<string, AreaTally> areas = new Dictionary<string, AreaTally>();
            foreach (var sb in allSpaceBoundaries)
            {
                var area = sb.Boundary.Area();
                var programName = sb.ProgramName ?? sb.Name;
                if (programName == null)
                {
                    continue;
                }
                if (!areas.ContainsKey(programName))
                {
                    var areaTarget = SpaceBoundary.TryGetRequirementsMatch(programName, out var req) ? req.AreaPerSpace * req.SpaceCount : 0.0;
                    areas[programName] = new AreaTally(programName, sb.Material.Color, areaTarget, area, 1, null, Guid.NewGuid(), sb.Name);
                }
                else
                {
                    var existingTally = areas[programName];
                    existingTally.AchievedArea += area;
                    existingTally.DistinctAreaCount += 1;
                }
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
                else
                {
                    areas[circulationKey].AchievedArea += corridorFloor.Area();
                    areas[circulationKey].DistinctAreaCount += 1;
                }
            }

            if (hasProgramRequirements)
            {
                foreach (var req in SpaceBoundary.Requirements)
                {
                    var r = req.Value;
                    var name = r.QualifiedProgramName;
                    if (!areas.ContainsKey(name))
                    {
                        areas[name] = new AreaTally(name, r.Color, r.AreaPerSpace * r.SpaceCount, 0, 0, null, Guid.NewGuid());
                    }
                }
            }

            return areas;
        }

        private static void CreateInitialSpaceBoundaries(SpacePlanningZonesInputs input,
                                                        SpacePlanningZonesOutputs output,
                                                        IEnumerable<LevelVolume> levelVolumes,
                                                        Model floorsModel,
                                                        IEnumerable<ServiceCore> cores,
                                                        List<LevelElements> levels,
                                                        IEnumerable<Element> walls,
                                                        List<SpaceBoundary> allSpaceBoundaries)
        {
            var corridorWidth = input.CorridorWidth;
            var corridorMat = SpaceBoundary.MaterialDict["Circulation"];
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

                var spaceBoundaries = new List<SpaceBoundary>();

                // take enclosed zones generated from the walls process, if any, and generate these as "unspecified" zones, regardless
                // of the program default.
                interiorZones.ForEach((z) =>
                {
                    var zone = SpaceBoundary.Make(z, "unspecified", lvl.Transform, lvl.Height);
                    spaceBoundaries.Add(zone);
                });

                List<Profile> corridorProfiles = new List<Profile>();

                List<Profile> thickerOffsetProfiles = null;

                // Process circulation
                if (input.CirculationMode == SpacePlanningZonesInputsCirculationMode.Automatic)
                {
                    thickerOffsetProfiles = GenerateAutomaticCirculation(input, corridorWidth, lvl, levelBoundary, coresInBoundary, corridorProfiles);
                }
                else if (input.CirculationMode == SpacePlanningZonesInputsCirculationMode.Manual && input.Corridors != null && input.Corridors.Count > 0)
                {
                    corridorProfiles = ProcessManualCirculation(input);
                }

                // Generate space boundaries from level boundary and corridor locations
                SplitCornersAndGenerateSpaceBoundaries(spaceBoundaries, input, lvl, corridorProfiles, levelBoundary, thickerOffsetProfiles, angleCheckForWallExtensions: walls != null && walls.Count() > 0);

                // Construct LevelElements to contain space boundaries
                var level = new LevelElements(new List<Element>(), Guid.NewGuid(), lvl.Name);
                level.LevelVolumeId = lvl.Id;
                levels.Add(level);

                // Create snapping geometry for corridors
                CreateSnappingGeometry(output, spaceBoundaries, "corridors");

                // These are the new, correct methods using the split inputs: 
                //Manual Corridor Splits
                // foreach (var pt in input.AddCorridors.SplitLocations)
                // {
                //     SplitZones(input, corridorWidth, lvl, spaceBoundaries, corridorProfiles, pt);
                // }
                SplitZonesMultiple(input, corridorWidth, lvl, spaceBoundaries, corridorProfiles, input.AddCorridors.SplitLocations, true, output.Model);

                //Create snapping geometry for splits
                CreateSnappingGeometry(output, spaceBoundaries, "splits");


                // Manual Split Locations
                // foreach (var pt in input.SplitZones.SplitLocations)
                // {
                //     SplitZones(input, corridorWidth, lvl, spaceBoundaries, corridorProfiles, pt, false);
                // }
                SplitZonesMultiple(input, corridorWidth, lvl, spaceBoundaries, corridorProfiles, input.SplitZones.SplitLocations, false, output.Model);


                // These are the old style methods, just left in place for backwards compatibility. 
                // Most of the time we expect these values to be empty.
                // Manual Corridor Splits
                foreach (var pt in input.AdditionalCorridorLocations)
                {
                    SplitZonesDeprecated(input, corridorWidth, lvl, spaceBoundaries, corridorProfiles, pt);
                }

                // Manual Split Locations
                foreach (var pt in input.ManualSplitLocations)
                {
                    SplitZonesDeprecated(input, corridorWidth, lvl, spaceBoundaries, corridorProfiles, pt, false);
                }

                foreach (SpaceBoundary b in spaceBoundaries)
                {
                    b.Level = level;
                }

                allSpaceBoundaries.AddRange(spaceBoundaries.OfType<SpaceBoundary>());
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

        private static void CreateSnappingGeometry(SpacePlanningZonesOutputs output, List<SpaceBoundary> spaceBoundaries, string type)
        {
            foreach (var sb in spaceBoundaries)
            {
                var boundary = sb as SpaceBoundary;
                output.Model.AddElement(new PolygonReference(boundary.Boundary.Perimeter, Guid.NewGuid(), type));
                if ((boundary.Boundary.Voids?.Count() ?? 0) > 0)
                {
                    boundary.Boundary.Voids.ToList().ForEach(v => output.Model.AddElement(new PolygonReference(v, Guid.NewGuid(), type)));
                }
            }
        }

        private static List<Profile> ProcessManualCirculation(SpacePlanningZonesInputs input)
        {
            List<Profile> corridorProfiles;
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
            return corridorProfiles;
        }

        private static List<Profile> GenerateAutomaticCirculation(SpacePlanningZonesInputs input, double corridorWidth, LevelVolume lvl, Profile levelBoundary, List<ServiceCore> coresInBoundary, List<Profile> corridorProfiles)
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
            return thickerOffsetProfiles;
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

        private static Profile AttemptToSplitWallsAndYieldLargestZone(Profile levelBoundary, List<Element> wallsInBoundary, out IEnumerable<Polyline> wallCenterlines, out List<Profile> otherProfiles, Model m = null)
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

        private static Line Extend(this Line l, double amt)
        {
            var dir = l.Direction().Unitized();
            return new Line(l.Start - dir * amt, l.End + dir * amt);
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


        private static void AutoLayoutProgram(List<SpaceBoundary> allSpaceBoundaries, IEnumerable<ProgramRequirement> programReqs, IEnumerable<ProgramAdjacencyMatrix> adjacencies, string defaultAssignment, out List<string> messages, Model model = null)
        {
            var boundaries = new List<SpaceBoundary>();
            messages = new List<string>();

            // calculate existing boundary properties
            foreach (var boundary in allSpaceBoundaries.Where(sb => sb.ProgramName == "unspecified" || sb.ProgramName == defaultAssignment))
            {
                boundary.CalculateDimensions();
                boundaries.Add(boundary);
            }
            if (programReqs == null || programReqs.Count() == 0)
            {
                return;
            }

            // make sure all reqs have depth and width
            foreach (var req in programReqs)
            {
                if (req.HyparSpaceType == "Circulation" || req.HyparSpaceType == defaultAssignment)
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


            // filter out boundaries that are too small to be viable
            var minDepth = programReqs.Min(p => p.Depth);
            var minWidth = programReqs.Min(p => p.Width);

            boundaries = boundaries.Where(b => b.Depth > minDepth * 0.75 && b.AvailableLength > minWidth * 0.75).ToList();

            var optimizationRequest = new OptimizationInputData
            {
                Paradigm = Paradigm.Bucketing
            };

            var fitObjective = new FitObjective();
            optimizationRequest.Objectives.Add(fitObjective);

            if (adjacencies != null && adjacencies.Count() > 0)
            {
                Dictionary<int, Dictionary<int, double>> adjacencyMap = new Dictionary<int, Dictionary<int, double>>();

                var adjacencyPreferences = new Dictionary<string, Dictionary<string, double>>();

                // populate adjacency preferences 
                var adjacencyObjective = new AdjacencyObjective
                {
                    Name = "adjacencies",
                    Adjacencies = "connectivity",
                    Affinity = adjacencyPreferences,
                };
                optimizationRequest.Objectives.Add(adjacencyObjective);
            }

            var reqsAsList = new List<ProgramRequirement>();

            foreach (var boundary in boundaries)
            {
                optimizationRequest.Buckets.Add(new Bucket { Size = boundary.Length.Value });
                fitObjective.BucketValues.Add(boundary.Depth.Value);
            }
            foreach (var req in programReqs)
            {
                if (req.Width.HasValue && req.Depth.HasValue)
                {
                    for (int i = 0; i < req.SpaceCount; i++)
                    {
                        reqsAsList.Add(req);
                        optimizationRequest.BucketItems.Add(new BucketItem { Size = req.Width.Value });
                        fitObjective.BucketItemTargets.Add(req.Depth.Value);
                    }
                }
            }

            var url = "https://ah-sand.api.hypar.io/optimize";
            BucketingResultData result = null;
            try
            {
                result = OptimizationRequest.GetResult<BucketingResultData>(optimizationRequest, url);
            }
            catch (Exception e)
            {
                messages.Add($"The space optimization server returned an invalid result: {e.Message}");
            }
            if (result == null)
            {
                return;
            }
            var bestResult = result.Results.First();
            for (int i = 0; i < bestResult.Result.Count; i++)
            {
                var boundary = boundaries[i];
                List<int> bucketResult = bestResult.Result[i];
                foreach (var j in bucketResult)
                {
                    var spaceToPlace = reqsAsList[j];
                    boundary.Collect(spaceToPlace);
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

        private static void SplitCornersAndGenerateSpaceBoundaries(List<SpaceBoundary> spaceBoundaries, SpacePlanningZonesInputs input, LevelVolume lvl, List<Profile> corridorProfiles, Profile levelBoundary, List<Profile> thickerOffsetProfiles = null, bool angleCheckForWallExtensions = false)
        {
            // subtract corridors from level boundary
            var remainingSpaces = new List<Profile>();
            try
            {
                remainingSpaces = Profile.Difference(new[] { levelBoundary }, corridorProfiles);
            }
            catch
            {
                // last ditch attempt to save the profile difference by force-cleaning
                // the corridor profiles
                try
                {
                    // Elements.Validators.Validator.DisableValidationOnConstruction = true;
                    var insetProfiles = ForceCleanProfiles(corridorProfiles, 0.03);
                    var levelBoundaryCleaned = ForceCleanProfiles(new[] { levelBoundary }, 0.03);
                    JsonInheritanceConverter.ElementwiseSerialization = true;
                    var path = "/Users/andrewheumann/Desktop/profiles_we_got.json";
                    File.WriteAllText(path, JsonConvert.SerializeObject(new Dictionary<string, IEnumerable<Profile>> { { "levelBoundaryCleaned", levelBoundaryCleaned }, { "insetProfiles", insetProfiles } }));
                    JsonInheritanceConverter.ElementwiseSerialization = false;
                    remainingSpaces = ForceCleanProfiles(Profile.Difference(levelBoundaryCleaned, insetProfiles).Where(p => p.Area() > 0.1));
                    // Elements.Validators.Validator.DisableValidationOnConstruction = false;
                }
                catch
                {
                    Warnings.Add("Unable to subtract corridors from spaces. Try adjusting your corridor geometry — watch out for points that are too close together.");
                    remainingSpaces.Add(levelBoundary);
                }
            }
            // for every space that's left
            foreach (var remainingSpace in remainingSpaces)
            {
                try
                {
                    // if we're along the boundary of the level, try to look for linear zones to add corner splits
                    if (remainingSpace.Perimeter.Vertices.Any(v => v.DistanceTo(levelBoundary.Perimeter) < 0.1))
                    {
                        var linearZones = thickerOffsetProfiles == null ? new List<Profile> { remainingSpace } : Profile.Difference(new[] { remainingSpace }, thickerOffsetProfiles);
                        var maxExtension = Math.Max(input.OuterBandDepth, input.DepthAtEnds) * 1.6;

                        // these zones are long, skinny, and at the edge of the floorplate, typically, 
                        // so we try to split them at their corners, so we don't
                        // have linear zones wrapping a corner.

                        foreach (var linearZone in linearZones)
                        {
                            var segmentsExtended = new List<Polyline>();
                            foreach (var line in linearZone.Segments())
                            {
                                // consider only longer lines
                                if (line.Length() < 2) continue;
                                try
                                {
                                    // extend a line
                                    var l = new Line(line.Start - line.Direction() * 0.1, line.End + line.Direction() * 0.1);
                                    var extended = l.ExtendToWithEndInfo(linearZone.Segments(), double.MaxValue, out var dirAtStart, out var dirAtEnd);

                                    // check distance extended
                                    var endDistance = extended.End.DistanceTo(l.End);
                                    var startDistance = extended.Start.DistanceTo(l.Start);

                                    // check the angle of the other lines we hit. we only want to extend if we have something close to a right angle. 
                                    var startDot = Math.Abs(dirAtStart.Dot(l.Direction()));
                                    var endDot = Math.Abs(dirAtEnd.Dot(l.Direction()));
                                    var minAngleTolerance = 0.2;
                                    var maxAngleTolerance = 0.8;
                                    var startAngleValid = (startDot < minAngleTolerance || startDot > maxAngleTolerance);
                                    var endAngleValid = (endDot < minAngleTolerance || endDot > maxAngleTolerance);


                                    // if it went a reasonable amount — more than 0.1 and less than maxExtension
                                    // add it to our split candidates
                                    if (startDistance > 0.1 && startDistance < maxExtension && (!angleCheckForWallExtensions || startAngleValid))
                                    {
                                        var startLine = new Line(extended.Start, line.Start);
                                        segmentsExtended.Add(startLine.ToPolyline(1));
                                    }
                                    if (endDistance > 0.1 && endDistance < maxExtension && (!angleCheckForWallExtensions || endAngleValid))
                                    {
                                        var endLine = new Line(extended.End, line.End);
                                        segmentsExtended.Add(endLine.ToPolyline(1));
                                    }
                                }
                                catch (Exception e)
                                {
                                    Console.WriteLine("Exception extending lines");
                                    Console.WriteLine(e.Message);
                                    // just ignore
                                }
                            }
                            // split the linear zone with these segments, and add the results as spaces. 
                            var splits = Profile.Split(new[] { linearZone }, segmentsExtended, Vector3.EPSILON);
                            spaceBoundaries.AddRange(splits.Select(s => SpaceBoundary.Make(s, input.DefaultProgramAssignment, lvl.Transform, lvl.Height)));
                        }
                        // if we had "thicker end zones" — these are only added with automatic circulation
                        // at "squarish" ends of hthe floorplate — subtract these from the zones we just made and add the "end zones"
                        // as spaces directly
                        if (thickerOffsetProfiles != null)
                        {
                            var endCapZones = Profile.Intersection(new[] { remainingSpace }, thickerOffsetProfiles);
                            spaceBoundaries.AddRange(endCapZones.Select(s => SpaceBoundary.Make(s, input.DefaultProgramAssignment, lvl.Transform, lvl.Height)));
                        }
                    }
                    else // otherwise just add each chunk cut by the circulation (non-exterior) as a boundary
                    {
                        spaceBoundaries.Add(SpaceBoundary.Make(remainingSpace, input.DefaultProgramAssignment, lvl.Transform, lvl.Height));
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("🚨");
                    Console.WriteLine(e.Message);
                    Console.WriteLine(e.StackTrace);
                    spaceBoundaries.Add(SpaceBoundary.Make(remainingSpace, input.DefaultProgramAssignment, lvl.Transform, lvl.Height));
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

        private static void SplitZonesDeprecated(SpacePlanningZonesInputs input, double corridorWidth, LevelVolume lvl, List<SpaceBoundary> spaceBoundaries, List<Profile> corridorProfiles, Vector3 pt, bool addCorridor = true)
        {
            // this is a hack — we're constructing a new SplitLocations w/ ZAxis as a sentinel meaning "null";
            SplitZones(input, corridorWidth, lvl, spaceBoundaries, corridorProfiles, new SplitLocations(pt, Vector3.ZAxis), addCorridor);
        }
        private static void SplitZones(SpacePlanningZonesInputs input, double corridorWidth, LevelVolume lvl, List<SpaceBoundary> spaceBoundaries, List<Profile> corridorProfiles, SplitLocations pt, bool addCorridor = true)
        {
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
                spaceBoundaries.AddRange(newSbs.Select(p => SpaceBoundary.Make(p, containingBoundary.Name, containingBoundary.Transform, lvl.Height)));
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
        private static void SplitZonesMultiple(SpacePlanningZonesInputs input, double corridorWidth, LevelVolume lvl, List<SpaceBoundary> spaceBoundaries, List<Profile> corridorProfiles, IEnumerable<SplitLocations> pts, bool addCorridor = true, Model m = null)
        {
            var corridorSegments = corridorProfiles.SelectMany(c => c.Segments());
            var allBoundaries = spaceBoundaries.OfType<SpaceBoundary>();
            var boundariesByPoint = pts.Select((pt) =>
            {
                var bd = allBoundaries.FirstOrDefault(b => b.Boundary.Contains(pt.Position));
                var id = bd?.Id ?? default(Guid);
                return (pt, bd, id);
            }).GroupBy((i) => i.id);
            foreach (var grp in boundariesByPoint)
            {
                if (grp.First().id == default(Guid))
                {
                    continue;
                }
                var containingBoundary = grp.First().bd;

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
                List<Line> extensions = new List<Line>();
                foreach (var grpItem in grp)
                {
                    var pt = grpItem.pt;
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
                    var extension = line.ExtendTo(containingBoundary.Boundary.Segments().Union(extensions));
                    // var extension = line.ExtendTo(containingBoundary.Boundary);
                    extensions.Add(extension);
                }
                List<Profile> newSbs = new List<Profile>();
                if (addCorridor)
                {
                    var corridorShapesIntersected = new List<Profile>();
                    var csAsProfiles = new List<Profile>();
                    extensions.ForEach((extension) =>
                    {
                        var corridorShape = extension.ToPolyline(1).Offset(corridorWidth / 2, EndType.Square);
                        var csAsProfilesLocal = corridorShape.Select(s => new Profile(s));
                        csAsProfiles.AddRange(csAsProfilesLocal);
                        corridorShapesIntersected.AddRange(Profile.Intersection(new[] { containingBoundary.Boundary }, csAsProfilesLocal));
                    });
                    corridorProfiles.AddRange(corridorShapesIntersected);
                    newSbs = Profile.Difference(new[] { containingBoundary.Boundary }, csAsProfiles);
                }
                else
                {
                    var extensionsAsPolylines = extensions.Select(e => e.ToPolyline(1));
                    try
                    {

                        newSbs = Profile.Split(new[] { containingBoundary.Boundary }, extensionsAsPolylines, Vector3.EPSILON);
                    }
                    catch (Exception e)
                    {
                        // last ditch attempt
                        Console.WriteLine("A split failed - trying again");
                        Console.WriteLine(e.Message);
                        try
                        {
                            var insetProfiles = ForceCleanProfiles(new[] { containingBoundary.Boundary });
                            newSbs = Profile.Split(insetProfiles, extensionsAsPolylines, Vector3.EPSILON);
                        }
                        catch
                        {
                            Console.WriteLine("A split failed.");
                            newSbs = new[] { containingBoundary.Boundary }.ToList();
                        }
                    }
                }
                spaceBoundaries.AddRange(newSbs.Select(p => SpaceBoundary.Make(p, containingBoundary.ProgramName, containingBoundary.Transform, lvl.Height, corridorSegments: corridorSegments)));
            }
        }

        private static List<Profile> ForceCleanProfiles(IEnumerable<Profile> profiles, double dist = 0.02)
        {
            var offsetProfiles = Profile.Offset(profiles, -dist);
            var insetProfiles = Profile.Offset(offsetProfiles, dist);
            return insetProfiles.Select(p => p.RemoveDuplicateVertices()).ToList();
        }

        private static Profile RemoveDuplicateVertices(this Profile profile)
        {
            var perimeter = profile.Perimeter.RemoveDuplicateVertices();
            var voids = profile.Voids.Select(v => v.RemoveDuplicateVertices()).ToList();
            return new Profile(perimeter, voids);
        }

        private static Polygon RemoveDuplicateVertices(this Polygon polygon)
        {
            return new Polygon(polygon.Vertices.RemoveSequentialDuplicates(true));
        }

        // copy-paste from elements, b/c internal
        internal static IList<Vector3> RemoveSequentialDuplicates(this IList<Vector3> vertices, bool wrap = false, double tolerance = Vector3.EPSILON)
        {
            List<Vector3> newList = new List<Vector3> { vertices[0] };
            for (int i = 1; i < vertices.Count; i++)
            {
                var vertex = vertices[i];
                var prevVertex = newList[newList.Count - 1];
                if (!vertex.IsAlmostEqualTo(prevVertex, tolerance))
                {
                    // if we wrap, and we're at the last vertex, also check for a zero-length segment between first and last.
                    if (wrap && i == vertices.Count - 1)
                    {
                        if (!vertex.IsAlmostEqualTo(vertices[0], tolerance))
                        {
                            newList.Add(vertex);
                        }
                    }
                    else
                    {
                        newList.Add(vertex);
                    }
                }
            }
            return newList;
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
    }
}