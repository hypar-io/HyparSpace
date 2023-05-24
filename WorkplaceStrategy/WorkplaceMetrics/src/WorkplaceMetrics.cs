using Elements;
using Elements.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace WorkplaceMetrics
{
    public static class WorkplaceMetrics
    {
        /// <summary>
        /// The WorkplaceMetrics function.
        /// </summary>
        /// <param name="model">The input model.</param>
        /// <param name="input">The arguments to the execution.</param>
        /// <returns>A WorkplaceMetricsOutputs instance containing computed results and the model with any new elements.</returns>
        public static WorkplaceMetricsOutputs Execute(Dictionary<string, Model> inputModels, WorkplaceMetricsInputs input)
        {
            var warnings = new List<string>();
            var outputModel = new Model();
            var zonesModel = inputModels["Space Planning Zones"];
            var hasFloors = inputModels.TryGetValue("Floors", out var floorsModel);
            var hasMass = inputModels.TryGetValue("Conceptual Mass", out var massModel);
            var hasCirculation = inputModels.TryGetValue("Circulation", out var circulationModel);

            // Get program requirements
            var hasProgramRequirements = inputModels.TryGetValue("Program Requirements", out var programReqsModel);
            var programReqs = programReqsModel?.AllElementsOfType<ProgramRequirement>();

            // Populate SpaceBoundary's program requirement dictionary with loaded requirements
            if (programReqs != null && programReqs.Any())
            {
                SpaceBoundary.SetRequirements(programReqs);
            }

            // figure out the special key containing circulation
            var circulationKey = "Circulation";
            var circReq = SpaceBoundary.Requirements.Values.ToList().FirstOrDefault(k => k.ProgramName == "Circulation" || k.Name == "Circulation" || k.HyparSpaceType == "Circulation");
            if (circReq != null)
            {
                circulationKey = circReq.QualifiedProgramName;
            }

            // calc total floor area
            var totalFloorArea = 0.0;
            if (hasFloors)
            {
                var allFloors = floorsModel?.AllElementsOfType<Floor>();
                foreach (var floor in allFloors)
                {
                    var difference = Profile.Difference(new[] { floor.Profile }, input.USFExclusions.Select(s => new Profile(s)));
                    totalFloorArea += difference.Sum(d => d.Area());
                }
            }
            else if (hasMass)
            {
                var allLevels = massModel.AllElementsOfType<LevelVolume>();
                foreach (var lvl in allLevels)
                {
                    var difference = Profile.Difference(new[] { lvl.Profile }, input.USFExclusions.Select(s => new Profile(s)));
                    totalFloorArea += difference.Sum(d => d.Area());
                }
            }
            else
            {
                warnings.Add("This function expects either floors or conceptual mass. If not provided, some calculations may be incorrect.");
            }

            var settings = new MetricsSettings
            {
                UsableArea = totalFloorArea,
                RentableArea = totalFloorArea * 1.2,
                Name = "Metrics Settings"
            };

            if (input.Overrides.Settings?.Any() == true)
            {
                var first = input.Overrides.Settings.First();
                settings.UsableArea = first.Value.UsableArea;
                settings.RentableArea = first.Value.RentableArea;
            }
            outputModel.AddElement(settings);

            var allSpaceBoundaries = zonesModel.AllElementsAssignableFromType<SpaceBoundary>().ToList();

            // convert circulation to space boundaries
            if (hasCirculation)
            {
                var allCirculation = circulationModel.AllElementsOfType<CirculationSegment>();
                var circByLevel = allCirculation.GroupBy(c => c.Level);

                foreach (var grp in circByLevel)
                {
                    var profiles = grp.Select(g => g.Profile);
                    try
                    {
                        profiles = Profile.UnionAll(profiles);
                    }
                    catch
                    {
                        // swallow
                    }
                    foreach (var profile in profiles)
                    {
                        var color = circReq?.Color ?? new Color(0.5, 0.5, 0.5, 1.0);
                        var mat = new Material("Circulation", color);
                        var sb = new SpaceBoundary()
                        {
                            Boundary = profile,
                            Area = profile.Area(),
                            ProgramGroup = circReq?.ProgramGroup ?? "Circulation",
                            Name = circReq?.ProgramName ?? "Circulation",
                            ProgramType = circulationKey,
                            Material = mat,
                        };
                        allSpaceBoundaries.Add(sb);
                    }
                }

            }

            var totalDeskCount = CountWorkplaceTyped(inputModels, "Open Office Layout", "Desk");
            var totalMeetingRoomSeats = CountWorkplaceTyped(inputModels, "Meeting Room Layout", "Meeting Room Seat");
            var totalClassroomSeats = CountWorkplaceTyped(inputModels, "Classroom Layout", "Classroom Seat");
            var totalPhoneBooths = CountWorkplaceTyped(inputModels, "Phone Booth Layout", "Phone Booth");
            var totalOpenCollabSeats = CountWorkplaceTyped(inputModels, "Open Collaboration Layout", "Collaboration seat");
            var totalPrivateOffices = CountWorkplaceTyped(inputModels, "Private Office Layout", "Private Office");

            var meetingRoomCount = allSpaceBoundaries.Count(sb => sb.Name == "Meeting Room");

            var headcount = -1;
            var deskSharingRatio = 1.0;

            if (input.CalculationMode == WorkplaceMetricsInputsCalculationMode.Fixed_Headcount)
            {
                headcount = input.TotalHeadcount;
                deskSharingRatio = headcount / (double)totalDeskCount;
            }
            else // fixed sharing ratio
            {
                deskSharingRatio = input.DeskSharingRatio;
                headcount = (int)Math.Round(totalDeskCount * deskSharingRatio);

            }
            var areaPerPerson = settings.UsableArea / headcount;
            var rentableAreaPerPerson = settings.RentableArea / headcount;
            var areaPerDesk = settings.UsableArea / totalDeskCount;
            var rentableAreaPerDesk = settings.RentableArea / totalDeskCount;
            var meetingRoomRatio = meetingRoomCount == 0 ? 0 : (int)Math.Round(headcount / (double)meetingRoomCount);

            var areaTallies = zonesModel.AllElementsOfType<AreaTally>();
            if (!areaTallies.Any())
            {
                var areas = CalculateAreas(hasProgramRequirements, allSpaceBoundaries);
                areaTallies = areas.Values;
            }
            foreach (var at in areaTallies)
            {
                at.Id = Guid.NewGuid();
                if (at.Name == "Private Office")
                {
                    at.AchievedCount = totalPrivateOffices;
                }
                else if (at.Name == "Phone Booth")
                {
                    at.AchievedCount = totalPhoneBooths;
                }
                else if (at.Name == "Open Collaboration")
                {
                    at.SeatCount = totalOpenCollabSeats;
                }
                else if (at.Name == "Classroom")
                {
                    at.SeatCount = totalClassroomSeats;
                }
                else if (at.Name == "Meeting Room")
                {
                    at.SeatCount = totalMeetingRoomSeats;
                }
                else if (at.Name == "Open Office")
                {
                    at.SeatCount = totalDeskCount;
                }
            }

            var totalCirculationArea = allSpaceBoundaries.Where(sb => sb.ProgramType == circulationKey).Sum(sb => sb.Area);

            var output = new WorkplaceMetricsOutputs
            {
                TotalUsableFloorArea = settings.UsableArea,
                TotalRentableFloorArea = settings.RentableArea,
                AreaPerPerson = areaPerPerson,
                RentableAreaPerPerson = rentableAreaPerPerson,
                AreaPerDesk = areaPerDesk,
                RentableAreaPerDesk = rentableAreaPerDesk,
                TotalDeskCount = totalDeskCount,
                MeetingRoomSeats = totalMeetingRoomSeats,
                ClassroomSeats = totalClassroomSeats,
                PhoneBooths = totalPhoneBooths,
                CollaborationSeats = totalOpenCollabSeats,
                TotalHeadcount = headcount,
                DeskSharingRatio = deskSharingRatio,
                MeetingRoomRatio = meetingRoomRatio,
                PrivateOfficeCount = totalPrivateOffices,
                CirculationUSFRatio = totalCirculationArea / settings.UsableArea,
                CirculationRSFRatio = totalCirculationArea / settings.RentableArea,
                Model = outputModel
            };

            output.Model.AddElements(areaTallies);

            if (warnings.Count > 0)
            {
                output.Warnings = warnings;
            }
            return output;
        }

        private static int CountWorkplaceTyped(Dictionary<string, Model> inputModels, string layoutName, string seatType)
        {
            var count = 0;
            if (inputModels.TryGetValue(layoutName, out var layoutModel))
            {
                count += layoutModel.AllElementsOfType<WorkpointCount>()
                                                .Sum((wc => (wc.Type != null && wc.Type.Contains(seatType)) ? wc.Count : 0));
            }
            return count;
        }


        private static Dictionary<string, AreaTally> CalculateAreas(bool hasProgramRequirements, IEnumerable<SpaceBoundary> allSpaceBoundaries)
        {
            Dictionary<string, AreaTally> areas = new Dictionary<string, AreaTally>();
            Dictionary<string, ProgramRequirement> matchingReqs = new Dictionary<string, ProgramRequirement>();
            foreach (var sb in allSpaceBoundaries)
            {
                var area = sb.Boundary.Area();
                var programName = sb.ProgramType ?? sb.Name;
                if (programName == null)
                {
                    continue;
                }
                SpaceBoundary.TryGetRequirementsMatch(programName, out var req);
                if (!areas.ContainsKey(programName))
                {
                    var areaTarget = req != null ? req.GetAreaPerSpace() * req.SpaceCount : 0.0;
                    matchingReqs[programName] = req;
                    areas[programName] = new AreaTally()
                    {
                        ProgramType = programName,
                        ProgramColor = req?.Color ?? sb.Material.Color,
                        AreaTarget = areaTarget,
                        AchievedArea = area,
                        DistinctAreaCount = 1,
                        Name = sb.Name,
                        TargetCount = req?.SpaceCount ?? 0,
                    };
                }
                else
                {
                    var existingTally = areas[programName];
                    existingTally.AchievedArea += area;
                    existingTally.DistinctAreaCount += 1;
                }
                if (req != null && req.CountType == ProgramRequirementCountType.Area_Total && req.AreaPerSpace != 0)
                {
                    sb.SpaceCount = (int)Math.Round(area / req.AreaPerSpace);
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

            // // calculate circulation areas (stored as floors, not space boundaries)

            // foreach (var corridorFloor in levels.SelectMany(lev => lev.Elements.OfType<Floor>()))
            // {
            //     if (!areas.ContainsKey(circulationKey))
            //     {
            //         areas[circulationKey] = new AreaTally()
            //         {
            //             ProgramType = circulationKey,
            //             ProgramColor = corridorFloor.Material.Color,
            //             AreaTarget = circReq.Value?.AreaPerSpace ?? 0,
            //             AchievedArea = corridorFloor.Area(),
            //             DistinctAreaCount = 1,
            //             Name = circulationKey,
            //             TargetCount = 1,
            //         };
            //     }
            //     else
            //     {
            //         areas[circulationKey].AchievedArea += corridorFloor.Area();
            //         areas[circulationKey].DistinctAreaCount += 1;
            //     }
            // }

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

    }
}