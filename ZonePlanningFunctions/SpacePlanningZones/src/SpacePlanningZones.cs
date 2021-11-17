using Elements;
using Elements.Geometry;
using Elements.Geometry.Solids;
using Elements.Spatial;
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

            // Get Circulation
            inputModels.TryGetValue("Circulation", out var circulationModel);

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
            CreateInitialSpaceBoundaries(input, output, levelVolumes, floorsModel, 
                                        circulationModel, cores, levels, walls, 
                                        allSpaceBoundaries);

            // process merge overrides
            ProcessMergeOverrides(input, allSpaceBoundaries);

            // process assignment overrides
            ProcessProgramAssignmentOverrides(input, allSpaceBoundaries);

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
                sb.LevelElements.Elements.Add(sb);

                // copy level volume properties
                var lvlVolume = levelsModel.Elements[sb.LevelElements.LevelVolumeId] as LevelVolume;
                sb.SetLevelProperties(lvlVolume);
                // we have to make the internal level to be null to avoid a recursive infinite loop when we serialize
                sb.LevelElements = null;
            }

            // calculate area tallies
            var areas = CalculateAreas(hasProgramRequirements, levels, allSpaceBoundaries);

            output.Model.AddElements(areas.Select(kvp => kvp.Value).OrderByDescending(a => a.AchievedArea));

            // adding levels also adds the space boundaries, since they're in the levels' own elements collections
            output.Model.AddElements(levels);
            foreach (var sb in output.Model.AllElementsOfType<SpaceBoundary>().ToList())
            {
                output.Model.AddElements(sb.Boundary.ToModelCurves(sb.Transform, sb.Material));
            }
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
                        .FirstOrDefault(sb => sb.IndividualCentroid.Value.DistanceTo(centroid) < 2.0);
                    if (matchingSB != null)
                    {
                        if (overrideValue.Value.Split <= 1)
                        {
                            matchingSB.SetProgram(overrideValue.Value.ProgramType ?? input.DefaultProgramAssignment);
                            Identity.AddOverrideIdentity(matchingSB, "Program Assignments", overrideValue.Id, overrideValue.Identity);
                        }
                        else // Split input on overrides is now deprecated — we shouldn't typically hit this code.
                        {
                            var level = matchingSB.LevelElements;
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
                                newCellSb.LevelElements = level;

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
                    var sbsByLevel = matchingSbs.GroupBy(sb => sb.LevelElements?.Id ?? Guid.Empty);
                    foreach (var lvlGrp in sbsByLevel)
                    {
                        var level = lvlGrp.First().LevelElements;
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
                            newSB.LevelElements = level;
                            allSpaceBoundaries.Add(newSB);
                        }
                    }
                }
            }
        }

        private static Dictionary<string, AreaTally> CalculateAreas(bool hasProgramRequirements, List<LevelElements> levels, List<SpaceBoundary> allSpaceBoundaries)
        {
            Dictionary<string, AreaTally> areas = new Dictionary<string, AreaTally>();
            Dictionary<string, ProgramRequirement> matchingReqs = new Dictionary<string, ProgramRequirement>();
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
                    var areaTarget = SpaceBoundary.TryGetRequirementsMatch(programName, out var req) ? req.GetAreaPerSpace() * req.SpaceCount : 0.0;
                    matchingReqs[programName] = req;
                    areas[programName] = new AreaTally()
                    {
                        ProgramType = programName,
                        ProgramColor = sb.Material.Color,
                        AreaTarget = areaTarget,
                        AchievedArea = area,
                        DistinctAreaCount = 1,
                        Name = sb.Name,
                        TargetCount = req?.SpaceCount ?? 0,
                    };
                    if (req != null && req.CountType == ProgramRequirementCountType.Area_Total && req.AreaPerSpace != 0)
                    {
                        sb.SpaceCount = (int)Math.Round(area / req.AreaPerSpace);
                    }
                }
                else
                {
                    var existingTally = areas[programName];
                    existingTally.AchievedArea += area;
                    existingTally.DistinctAreaCount += 1;
                }
            }

            // calculate achieved counts by "count type"
            foreach (var areakvp in areas)
            {
                var programName = areakvp.Key;
                var req = matchingReqs[programName];
                if (req == null || req.CountType == ProgramRequirementCountType.Item)
                {
                    areakvp.Value.AchievedCount = areakvp.Value.DistinctAreaCount;
                    continue;
                }
                // if the user specified a different "count type" for this requirement, adjust the achieved count accordingly.
                if (req.CountType == ProgramRequirementCountType.Area_Total)
                {
                    var areaPerSpace = req.GetAreaPerSpace();
                    if (areaPerSpace != 0)
                    {
                        areakvp.Value.AchievedCount = (int)Math.Round(areakvp.Value.AchievedArea / areaPerSpace);

                    }
                    else
                    {
                        areakvp.Value.AchievedCount = null;
                    }
                }

            }

            // count corridors in area
            var circulationKey = "Circulation";
            var circReq = SpaceBoundary.Requirements.ToList().FirstOrDefault(k => k.Value.Name == "Circulation");
            if (circReq.Key != null)
            {
                circulationKey = circReq.Value.ProgramName;
            }

            // calculate circulation areas (stored as floors, not space boundaries)

            foreach (var corridorFloor in levels.SelectMany(lev => lev.Elements.OfType<Floor>()))
            {
                if (!areas.ContainsKey(circulationKey))
                {
                    areas[circulationKey] = new AreaTally()
                    {
                        ProgramType = circulationKey,
                        ProgramColor = corridorFloor.Material.Color,
                        AreaTarget = circReq.Value?.AreaPerSpace ?? 0,
                        AchievedArea = corridorFloor.Area(),
                        DistinctAreaCount = 1,
                        Name = circulationKey,
                        TargetCount = 1,
                    };
                }
                else
                {
                    areas[circulationKey].AchievedArea += corridorFloor.Area();
                    areas[circulationKey].DistinctAreaCount += 1;
                }
            }

            // create area tallies for unfulfilled requirements, with achieved areas of 0

            if (hasProgramRequirements)
            {
                foreach (var req in SpaceBoundary.Requirements)
                {
                    var r = req.Value;
                    var name = r.QualifiedProgramName;
                    var type = r.CountType;
                    if (!areas.ContainsKey(name))
                    {
                        areas[name] = new AreaTally()
                        {
                            ProgramType = name,
                            ProgramColor = r.Color,
                            AreaTarget = r.GetAreaPerSpace() * r.SpaceCount,
                            AchievedArea = 0,
                            DistinctAreaCount = 0,
                            TargetCount = r.SpaceCount
                        };
                    }
                }
            }

            return areas;
        }

        private static void CreateInitialSpaceBoundaries(
                                                            SpacePlanningZonesInputs input,
                                                            SpacePlanningZonesOutputs output,
                                                            IEnumerable<LevelVolume> levelVolumes,
                                                            Model floorsModel,
                                                            Model circulationModel,
                                                            IEnumerable<ServiceCore> cores,
                                                            List<LevelElements> levels,
                                                            IEnumerable<Element> walls,
                                                            List<SpaceBoundary> allSpaceBoundaries
                                                        )
        {
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
                    var newLevelBoundary = AttemptToSplitWallsAndYieldLargestZone(levelBoundary, wallsInBoundary, 
                                                                                  out var centerlines, out interiorZones, 
                                                                                  output.Model);
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

                var corridorProfiles = circulationModel.AllElementsOfType<Floor>().Select(corridor => corridor.Profile);

                List<Profile> thickerOffsetProfiles = null;

                // Generate space boundaries from level boundary and corridor locations
                SplitCornersAndGenerateSpaceBoundaries(spaceBoundaries, 
                                                       input, 
                                                       lvl, 
                                                       corridorProfiles, 
                                                       levelBoundary, 
                                                       thickerOffsetProfiles, 
                                                       angleCheckForWallExtensions: walls != null && walls.Count() > 0);

                // Construct LevelElements to contain space boundaries
                var level = new LevelElements()
                {
                    Name = lvl.Name,
                    Elements = new List<Element>()
                };
                level.LevelVolumeId = lvl.Id;
                levels.Add(level);

                // Create snapping geometry for corridors
                CreateSnappingGeometry(output, spaceBoundaries, "corridors");

                //Create snapping geometry for splits
                CreateSnappingGeometry(output, spaceBoundaries, "splits");


                // Manual Split Locations
                // foreach (var pt in input.SplitZones.SplitLocations)
                // {
                //     SplitZones(input, corridorWidth, lvl, spaceBoundaries, corridorProfiles, pt, false);
                // }
                var splitLocations = input.SplitZones.SplitLocations;
                var levelProxy = lvl.Proxy("Levels");
                lvl.Proxy = levelProxy;
                // if we've overridden splits per-level, use those split locations.
                if (input.Overrides?.SplitZones != null && input.Overrides?.SplitZones.Count > 0)
                {
                    var matchingOverride = input.Overrides.SplitZones.FirstOrDefault(o => o.Identity.BuildingName == lvl.BuildingName && o.Identity.Name == lvl.Name);
                    if (matchingOverride != null)
                    {
                        splitLocations = matchingOverride.Value.Splits.SplitLocations;

                        Identity.AddOverrideIdentity(levelProxy, matchingOverride);
                        Identity.AddOverrideValue(levelProxy, matchingOverride.GetName(), matchingOverride.Value);
                        output.Model.AddElement(levelProxy);
                    }
                }

                SplitZonesMultiple(input, lvl, spaceBoundaries, corridorProfiles, splitLocations, output.Model);

                var corridorWidth = input.CorridorWidth;
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
                    b.LevelElements = level;
                }

                allSpaceBoundaries.AddRange(spaceBoundaries.OfType<SpaceBoundary>());
            }
        }

        private static void CreateSnappingGeometry(SpacePlanningZonesOutputs output, List<SpaceBoundary> spaceBoundaries, string type)
        {
            foreach (var sb in spaceBoundaries)
            {
                var boundary = sb as SpaceBoundary;
                output.Model.AddElement(new PolygonReference() { Boundary = boundary.Boundary.Perimeter, Name = type });
                if ((boundary.Boundary.Voids?.Count() ?? 0) > 0)
                {
                    boundary.Boundary.Voids.ToList().ForEach(v => output.Model.AddElement(new PolygonReference() { Boundary = v, Name = type }));
                }
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

        private static void SplitCornersAndGenerateSpaceBoundaries(
                                                                        List<SpaceBoundary> spaceBoundaries, 
                                                                        SpacePlanningZonesInputs input, 
                                                                        LevelVolume lvl, 
                                                                        IEnumerable<Profile> corridorProfiles, 
                                                                        Profile levelBoundary, 
                                                                        List<Profile> thickerOffsetProfiles = null, 
                                                                        bool angleCheckForWallExtensions = false
                                                                    )
        {
            // subtract corridors from level boundary
            var remainingSpaces = new List<Profile>();
            try
            {
                remainingSpaces = Profile.Difference(new[] { levelBoundary }, corridorProfiles);
            }
            catch
            {
                remainingSpaces.Add(levelBoundary);
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
                        var MIN_EXTENTION = 0.1;

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
                                    if (startDistance > MIN_EXTENTION && startDistance < maxExtension && (!angleCheckForWallExtensions || startAngleValid))
                                    {
                                        var startLine = new Line(extended.Start, line.Start);
                                        segmentsExtended.Add(startLine.ToPolyline(1));
                                    }
                                    if (endDistance > MIN_EXTENTION && endDistance < maxExtension && (!angleCheckForWallExtensions || endAngleValid))
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

        private static void SplitZonesDeprecated(
                                                    SpacePlanningZonesInputs input, 
                                                    double corridorWidth, 
                                                    LevelVolume lvl, 
                                                    List<SpaceBoundary> spaceBoundaries, 
                                                    IEnumerable<Profile> corridorProfiles, 
                                                    Vector3 pt, 
                                                    bool addCorridor = true
                                                )
        {
            // this is a hack — we're constructing a new SplitLocations w/ ZAxis as a sentinel meaning "null";
            SplitZones(input, corridorWidth, lvl, spaceBoundaries, corridorProfiles, new SplitLocations(pt, Vector3.ZAxis), addCorridor);
        }
        private static void SplitZones(
                                            SpacePlanningZonesInputs input, 
                                            double corridorWidth, 
                                            LevelVolume lvl, 
                                            List<SpaceBoundary> spaceBoundaries,
                                            IEnumerable<Profile> corridorProfiles, 
                                            SplitLocations pt, 
                                            bool addCorridor = true
                                        )
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
                    corridorProfiles.Concat(corridorShapesIntersected);
                    newSbs = Profile.Difference(new[] { containingBoundary.Boundary }, csAsProfiles);
                }
                else
                {
                    newSbs = Profile.Split(new[] { containingBoundary.Boundary }, new[] { extension.ToPolyline(1) }, Vector3.EPSILON);
                }
                spaceBoundaries.AddRange(newSbs.Select(p => SpaceBoundary.Make(p, containingBoundary.Name, containingBoundary.Transform, lvl.Height)));
            }
        }

        private static void SplitZonesMultiple(
                                                    SpacePlanningZonesInputs input, 
                                                    LevelVolume lvl, 
                                                    List<SpaceBoundary> spaceBoundaries, 
                                                    IEnumerable<Profile> corridorProfiles, 
                                                    IEnumerable<SplitLocations> pts, 
                                                    Model m = null
                                                )
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
                        var offsetProfiles = Profile.Offset(new[] { containingBoundary.Boundary }, -0.02);
                        var insetProfiles = Profile.Offset(offsetProfiles, 0.02);
                        newSbs = Profile.Split(insetProfiles, extensionsAsPolylines, Vector3.EPSILON);
                    }
                    catch
                    {
                        Console.WriteLine("A split failed.");
                        newSbs = new[] { containingBoundary.Boundary }.ToList();
                    }
                }
                spaceBoundaries.AddRange(newSbs.Select(p => SpaceBoundary.Make(p, containingBoundary.ProgramName, containingBoundary.Transform, lvl.Height, corridorSegments: corridorSegments)));
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