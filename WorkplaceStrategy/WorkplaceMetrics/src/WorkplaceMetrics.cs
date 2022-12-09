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
            var zonesModel = inputModels["Space Planning Zones"];
            var hasFloors = inputModels.TryGetValue("Floors", out var floorsModel);
            var hasMass = inputModels.TryGetValue("Conceptual Mass", out var massModel);

            // Get program requirements
            var hasProgramRequirements = inputModels.TryGetValue("Program Requirements", out var programReqsModel);
            var programReqs = programReqsModel?.AllElementsOfType<ProgramRequirement>();

            // Populate SpaceBoundary's program requirement dictionary with loaded requirements
            if (programReqs != null && programReqs.Count() > 0)
            {
                SpaceBoundary.SetRequirements(programReqs);
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

            var allSpaceBoundaries = zonesModel.AllElementsOfType<SpaceBoundary>();

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
            var areaPerPerson = totalFloorArea / headcount;
            var areaPerDesk = totalFloorArea / totalDeskCount;
            var meetingRoomRatio = meetingRoomCount == 0 ? 0 : (int)Math.Round(headcount / (double)meetingRoomCount);

            var areaTallies = zonesModel.AllElementsOfType<AreaTally>();
            if (areaTallies.Count() == 0)
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

            var output = new WorkplaceMetricsOutputs(
                                                    totalFloorArea,
                                                    areaPerPerson,
                                                    totalDeskCount,
                                                    totalMeetingRoomSeats,
                                                    totalClassroomSeats,
                                                    totalPhoneBooths,
                                                    totalOpenCollabSeats,
                                                    headcount,
                                                    areaPerDesk,
                                                    deskSharingRatio,
                                                    meetingRoomRatio,
                                                    totalPrivateOffices
                                                    );
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
                        ProgramColor = sb.Material.Color,
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

            // count corridors in area
            var circulationKey = "Circulation";
            var circReq = SpaceBoundary.Requirements.ToList().FirstOrDefault(k => k.Value.Name == "Circulation");
            if (circReq.Key != null)
            {
                circulationKey = circReq.Value.ProgramName;
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