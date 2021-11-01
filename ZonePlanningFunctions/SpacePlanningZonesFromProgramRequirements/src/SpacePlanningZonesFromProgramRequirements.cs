using Elements;
using Elements.Geometry;
using Elements.Geometry.Solids;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SpacePlanningZonesFromProgramRequirements
{
    public static class SpacePlanningZonesFromProgramRequirements
    {
        /// <summary>
        /// The SpacePlanningZonesFromProgramRequirements function.
        /// </summary>
        /// <param name="model">The input model.</param>
        /// <param name="input">The arguments to the execution.</param>
        /// <returns>A SpacePlanningZonesFromProgramRequirementsOutputs instance containing computed results and the model with any new elements.</returns>
        public static SpacePlanningZonesFromProgramRequirementsOutputs Execute(Dictionary<string, Model> inputModels, SpacePlanningZonesFromProgramRequirementsInputs input)
        {
            var output = new SpacePlanningZonesFromProgramRequirementsOutputs();
            var reqsModel = inputModels["Program Requirements"];
            var programReqs = reqsModel.AllElementsOfType<ProgramRequirement>().ToList();

            var hasFloors = inputModels.TryGetValue("Floors", out var floorsModel);
            var hasCores = inputModels.TryGetValue("Core", out var coresModel);
            var hasLevels = inputModels.TryGetValue("Levels", out var levelsModel);
            var floorEdges = floorsModel?.AllElementsOfType<Floor>().SelectMany(f => f.Profile.Segments().Select(s => s.TransformedLine(f.Transform.Concatenated(new Transform(0, 0, f.Thickness)))));

            SpaceBoundary.Reset();
            if (programReqs != null && programReqs.Count() > 0)
            {
                SpaceBoundary.SetRequirements(programReqs);
            }

            var defaultAspectRatio = input.DefaultAspectRatio;
            var fixedDepths = new Dictionary<string, double>() {
                {"Phone Booth", 2}
            };
            var autoAlign = true;
            var runningXPosition = 0.0;
            var spacing = 1;
            var xyPlane = new Plane(Vector3.Origin, Vector3.ZAxis);


            // get floors or levels, if present
            IEnumerable<FloorOrLevel> floorsOrLevels = new List<FloorOrLevel>();
            if (hasLevels)
            {
                floorsOrLevels = levelsModel.AllElementsOfType<LevelVolume>().Select(FloorOrLevel.FromLevelVolume);
            }
            else if (hasFloors)
            {
                floorsOrLevels = floorsModel.AllElementsOfType<Floor>().Select(FloorOrLevel.FromFloor);
            }

            var positionTransform = new Transform(input.UnplacedSpaceLocation);
            if (floorsOrLevels.Count() > 0 && input.UnplacedSpaceLocation.DistanceTo(new Vector3()) < 0.1)
            {
                var bbox = new BBox3(floorsOrLevels.SelectMany(f => f.Profile.Perimeter.Vertices).ToList());
                positionTransform = new Transform(bbox.Min.X, bbox.Max.Y + 10, 0);
            }

            var groupedProgramReqs = programReqs.GroupBy(p => p.ProgramGroup);
            var texts = new List<(Vector3 location, Vector3 facingDirection, Vector3 lineDirection, string text, Color? color)>();

            foreach (var group in groupedProgramReqs)
            {
                var coords = new List<Vector3>();
                if (runningXPosition != 0)
                {
                    runningXPosition += spacing;
                }
                var reqsInGroup = group.ToList();
                // create primary space boundaries
                for (int j = 0; j < reqsInGroup.Count(); j++)
                {
                    var req = reqsInGroup[j];
                    if (req.ProgramName == input.DefaultProgramAssignment && hasFloors)
                    {
                        continue;
                    }
                    var area = req.AreaPerSpace;
                    if (area == 0 && req.Width == null && req.Depth == null)
                    {
                        continue;
                    }
                    var width = 0.0;
                    var depth = 0.0;
                    if (req.Width != null && req.Width != 0 && req.Depth != null && req.Depth != 0)
                    {
                        width = req.Width.Value;
                        depth = req.Depth.Value;
                    }
                    else if (req.Width != null && req.Width != 0 && area != 0)
                    {
                        width = req.Width.Value;
                        depth = area / width;
                    }
                    else if (req.Depth != null && req.Depth != 0 && area != 0)
                    {
                        depth = req.Depth.Value;
                        width = area / depth;
                    }
                    else if (area != 0)
                    {
                        width = Math.Sqrt(area * defaultAspectRatio);
                        depth = Math.Sqrt(area / defaultAspectRatio);
                        if (fixedDepths.ContainsKey(req.HyparSpaceType))
                        {
                            depth = fixedDepths[req.HyparSpaceType];
                            width = area / depth;
                        }
                    }

                    var color = req.Color;
                    color.Alpha = 0.5;
                    var mat = new Material(req.ProgramName, color);
                    var runningYPosition = 0.0;
                    for (int i = 0; i < req.SpaceCount; i++)
                    {
                        if (width == 0 || depth == 0)
                        {
                            continue;
                        }
                        var profile = Polygon.Rectangle(width, depth);
                        var parentCentroid = profile.Centroid();
                        var transform = positionTransform.Concatenated(new Transform(runningXPosition + width / 2, runningYPosition + depth / 2, 0));
                        var identifier = $"{req.ProgramName}: {i}";
                        ArrangeSpacesOverride match = null;
                        if (input.Overrides?.ArrangeSpaces != null)
                        {
                            match = input.Overrides.ArrangeSpaces.FirstOrDefault((ov) => ov.Identity.Identifier == identifier);
                            transform = match?.Value?.Transform ?? transform;
                            if (autoAlign && hasFloors)
                            {
                                var closestEdgeTransform = FindTransformFromClosestEdge(floorEdges, transform);
                                transform = closestEdgeTransform.Concatenated(transform);
                            }
                        }
                        MassBoundariesOverride boundaryMatch = null;
                        if (input.Overrides?.MassBoundaries != null)
                        {
                            boundaryMatch = input.Overrides.MassBoundaries.FirstOrDefault((ov) => ov.Identity.Identifier == identifier);
                            if (boundaryMatch != null)
                            {
                                var previousTransform = boundaryMatch.Identity.EditBoundaryTransform ?? transform;
                                var inverse = new Transform(previousTransform);
                                inverse.Invert();
                                var unTransformedBoundary = boundaryMatch.Value?.EditBoundary?.TransformedPolygon(inverse) ?? profile;
                                profile = unTransformedBoundary.Project(xyPlane);
                            }
                        }
                        SpacePropertiesOverride propMatch = null;
                        var height = input.DefaultHeight;
                        if (input.Overrides?.SpaceProperties != null)
                        {
                            propMatch = input.Overrides.SpaceProperties.FirstOrDefault((ov) => ov.Identity.Identifier == identifier);
                            if (propMatch != null)
                            {
                                height = propMatch.Value.Height;
                            }
                        }
                        // var rep = new Representation(new[] { new Extrude(profile, 3, Vector3.ZAxis, false) });
                        // var sb = new SpaceBoundary(profile, null, transform, mat, rep, false, Guid.NewGuid(), req.HyparSpaceType);
                        var sb = SpaceBoundary.Make(profile, req.ProgramName, transform, height, parentCentroid, parentCentroid);
                        sb.AdditionalProperties["Identifier"] = identifier;
                        sb.AdditionalProperties["Program Group"] = req.ProgramGroup;
                        if (match != null)
                        {
                            Identity.AddOverrideIdentity(sb, match);
                        }
                        if (boundaryMatch != null)
                        {
                            Identity.AddOverrideIdentity(sb, boundaryMatch);
                        }
                        if (propMatch != null)
                        {
                            Identity.AddOverrideIdentity(sb, propMatch);
                        }
                        sb.AdditionalProperties["EditBoundary"] = profile.TransformedPolygon(transform);
                        sb.AdditionalProperties["EditBoundaryTransform"] = transform;
                        output.Model.AddElement(sb);
                        if (match == null)
                        {
                            coords.AddRange(sb.Boundary.Perimeter.Vertices.Select(v => sb.Transform.OfPoint(v)));
                        }
                        runningYPosition += (depth + spacing);
                    }
                    runningXPosition += (width + spacing);
                }

                // create group boundary
                if (coords.Count > 0)
                {
                    var bbox = new BBox3(coords);
                    var inflatedBox = new BBox3(bbox.Min + new Vector3(-1, -1), bbox.Max + new Vector3(1, 1));
                    output.Model.AddElements(inflatedBox.ToModelCurves());
                    var textPt = ((inflatedBox.Max.X + inflatedBox.Min.X) / 2, inflatedBox.Max.Y + 0.5, 0);
                    texts.Add((textPt, Vector3.ZAxis, Vector3.XAxis, group.Key ?? "Program Requirements", Colors.Black));
                }
            }

            if (texts.Count() > 0)
            {
                var grouplabels = new ModelText(texts, FontSize.PT48, 50);
                output.Model.AddElement(grouplabels);
            }
            var corridorMat = SpaceBoundary.MaterialDict["Circulation"];

            // create level elements
            var levels = new List<LevelElements>();
            foreach (var floor in floorsOrLevels)
            {
                var levelElementList = new List<Element>();
                var levelElement = new LevelElements(levelElementList, Guid.NewGuid(), floor.Name);
                levels.Add(levelElement);
                // create corridors
                if (input.Corridors != null && input.Corridors.Count() > 0)
                {
                    var corridorProfiles = ProcessManualCirculation(input);
                    foreach (var p in corridorProfiles)
                    {
                        corridorProfiles.Select(p => new Floor(p, 0.1, floor.Transform, corridorMat)).ToList().ForEach(f => levelElement.Elements.Add(f));
                    }
                }
            }


            // create "default" boundary from levels or floors, if present.
            // Also associate space boundaries with level elements

            var defaultReq = programReqs.FirstOrDefault(r => r.ProgramName == input.DefaultProgramAssignment);
            if (input.DefaultProgramAssignment != null && input.DefaultProgramAssignment != "unspecified" && defaultReq == null)
            {
                output.Warnings.Add($"You have selected {input.DefaultProgramAssignment} as your default program assignment, but it is not present in the program requirements. This choice will be ignored.");
            }
            if (hasFloors || hasLevels)
            {
                foreach (var floor in floorsOrLevels)
                {
                    var levelElement = levels.First((l) => l.Name == floor.Name);
                    var levelElementList = levelElement.Elements as List<Element>;
                    var otherSpaceBoundariesAtFloorLevel = FindOtherSpaceBoundariesAtFloorLevel(output.Model.AllElementsOfType<SpaceBoundary>(), floor);
                    levelElementList.AddRange(otherSpaceBoundariesAtFloorLevel);
                    // don't try to subtract anything if we had no default space
                    if (defaultReq == null)
                    {
                        continue;
                    }
                    var shapesToSubtract = new List<Profile>(otherSpaceBoundariesAtFloorLevel.Select(sb => sb.Boundary.TransformedProfile(sb.Transform)));
                    if (hasCores)
                    {
                        shapesToSubtract.AddRange(coresModel.AllElementsOfType<ServiceCore>().Select(sc => sc.Profile.TransformedProfile(sc.Transform)));
                    }
                    var profileDifference = Profile.Difference(new[] { floor.Profile }, shapesToSubtract);

                    for (int i = 0; i < profileDifference.Count; i++)
                    {

                        var currProfile = profileDifference[i];
                        try
                        {
                            var offsetProfile = Profile.Offset(new[] { currProfile }, -1).OrderBy(p => p.Area()).Last();
                            output.Model.AddElements(offsetProfile.ToModelCurves());
                            var offsetOut = Profile.Offset(new[] { offsetProfile }, 1).OrderBy(p => p.Area()).Last();
                            currProfile = offsetOut;
                        }
                        catch
                        {

                        }

                        var sb = SpaceBoundary.Make(currProfile, defaultReq.ProgramName, floor.Transform, 2.9, floor.Profile.Perimeter.Centroid());
                        sb.AdditionalProperties["Identifier"] = $"{defaultReq.ProgramName}: {i}";
                        sb.AdditionalProperties["DefaultType"] = true;
                        output.Model.AddElement(sb);
                        levelElementList.Add(sb);
                    }
                }
            }

            // if we weren't able to populate levels before, let's generate new ones on the fly.
            if (levels.Count == 0)
            {
                var allHeights = output.Model.AllElementsOfType<SpaceBoundary>().Select((sb) => sb.Transform.Origin.Z);
                var uniqueHeights = allHeights.Distinct().OrderBy(v => v);
                int levelCounter = 1;
                foreach (var height in uniqueHeights)
                {
                    var elements = output.Model.AllElementsOfType<SpaceBoundary>().Where((sb) => sb.Transform.Origin.Z == height);
                    var levelElement = new LevelElements(new List<Element>(elements), Guid.NewGuid(), $"Level {levelCounter++}");
                    levels.Add(levelElement);
                }
            }

            output.Model.AddElements(levels);

            // tally up areas
            Dictionary<string, AreaTally> areas = new Dictionary<string, AreaTally>();
            var sbsInLevels = levels.SelectMany(lev => lev.Elements.OfType<SpaceBoundary>());
            var sbs = sbsInLevels.Count() > 0 ? sbsInLevels : output.Model.AllElementsOfType<SpaceBoundary>();
            foreach (var sb in sbs)
            {
                var area = sb.Boundary.Area();
                if (sb.ProgramName == null)
                {
                    continue;
                }
                if (!areas.ContainsKey(sb.ProgramName))
                {
                    var areaTarget = SpaceBoundary.Requirements.TryGetValue(sb.ProgramName, out var requirement) ? requirement.AreaPerSpace * requirement.SpaceCount : 0.0;
                    areas[sb.ProgramName] = new AreaTally(sb.ProgramName, sb.Material.Color, areaTarget, area, 1, null, 1, null, Guid.NewGuid(), sb.ProgramName);
                }
                else
                {
                    var existingTally = areas[sb.ProgramName];
                    existingTally.AchievedArea += area;
                    existingTally.DistinctAreaCount++;
                    existingTally.AchievedCount++;
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
                    areas[circulationKey] = new AreaTally(circulationKey, corridorFloor.Material.Color, circReq.Value?.AreaPerSpace ?? 0, corridorFloor.Area(), 1, null, 1, null, Guid.NewGuid(), circulationKey);
                }
            }

            output.Model.AddElements(areas.Select(kvp => kvp.Value).OrderByDescending(a => a.AchievedArea));


            return output;
        }

        public struct FloorOrLevel
        {
            public Profile Profile;
            public Transform Transform;

            public string Name;

            public static FloorOrLevel FromFloor(Floor f)
            {
                return new FloorOrLevel { Profile = f.Profile, Transform = f.Transform, Name = f.Name };
            }

            public static FloorOrLevel FromLevelVolume(LevelVolume f)
            {
                return new FloorOrLevel { Profile = f.Profile, Transform = f.Transform, Name = f.Name };
            }
        }

        public static Profile TransformedProfile(this Profile profile, Transform t)
        {
            return new Profile(profile.Perimeter.TransformedPolygon(t), new List<Polygon>(profile.Voids.Select((v) => v.TransformedPolygon(t))), Guid.NewGuid(), profile.Name);
        }
        private static IEnumerable<SpaceBoundary> FindOtherSpaceBoundariesAtFloorLevel(IEnumerable<SpaceBoundary> allBoundaries, FloorOrLevel floor)
        {
            return allBoundaries.Where((sb) =>
            {
                return Math.Abs(sb.Transform.Origin.Z - floor.Transform.Origin.Z) < 2 &&
                floor.Profile.Contains(sb.Transform.OfPoint(sb.Boundary.Perimeter.Centroid()));
            });
        }

        public static Transform FindTransformFromClosestEdge(IEnumerable<Line> edges, Transform startingXForm)
        {
            Line closestEdge = null;
            var minDist = 4.0;
            var origin = startingXForm.Origin;
            foreach (var edge in edges)
            {
                var cp = origin.ClosestPointOn(edge);
                var dist = cp.DistanceTo(origin);
                if (dist < minDist)
                {
                    minDist = dist;
                    closestEdge = edge;
                }
            }
            var transform = new Transform();
            if (closestEdge != null)
            {
                var dir = closestEdge.Direction();
                var angle = startingXForm.XAxis.PlaneAngleTo(dir);
                transform.Rotate(Vector3.ZAxis, angle);
                var heightAdjustment = closestEdge.Start.Z - startingXForm.Origin.Z;
                transform.Move(0, 0, heightAdjustment);
            }
            return transform;
        }

        private static List<Profile> ProcessManualCirculation(SpacePlanningZonesFromProgramRequirementsInputs input)
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
    }
}