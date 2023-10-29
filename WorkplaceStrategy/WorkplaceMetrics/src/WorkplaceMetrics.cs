using Elements;
using Elements.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace WorkplaceMetrics
{
    public static class WorkplaceMetrics
    {
        private const string _openOffice = "Open Office";
        private const string _meetingRoom = "Meeting Room";
        private const string _classroom = "Classroom";
        private const string _phoneBooth = "Phone Booth";
        private const string _openCollab = "Open Collaboration";
        private const string _privateOffice = "Private Office";
        private const string _lounge = "Lounge";
        private const string _pantry = "Pantry";
        private const string _reception = "Reception";

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
            inputModels.TryGetValue(_openOffice + " Layout", out var openOfficeModel);
            inputModels.TryGetValue(_openCollab + " Layout", out var openCollabModel);

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
                Name = "Metrics Settings"
            };

            if (input.Overrides.Settings?.Any() == true)
            {
                var first = input.Overrides.Settings.First();
                settings.UsableArea = first.Value.UsableArea;
            }
            outputModel.AddElement(settings);

            var allSpaceBoundaries = zonesModel.AllElementsAssignableFromType<SpaceBoundary>().ToList();
            var openOfficeBoundaries = openOfficeModel?.AllElementsAssignableFromType<SpaceBoundary>().ToList();
            var openCollabSpaceMetrics = openCollabModel?.AllElementsAssignableFromType<SpaceMetric>().ToList();

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

            var layoutNames = new string[] { _openOffice, _meetingRoom, _classroom, _phoneBooth, _openCollab, _privateOffice, _lounge, _reception, _pantry };
            var metricByLayouts = new Dictionary<string, SpaceMetric>();
            foreach (var layoutName in layoutNames)
            {
                metricByLayouts[layoutName] = CountWorkplaceTyped(inputModels, input, layoutName, allSpaceBoundaries, openOfficeBoundaries, openCollabSpaceMetrics);
            }

            var meetingRoomCount = allSpaceBoundaries.Count(sb => sb.Name == "Meeting Room");
            
            var desksMetric = metricByLayouts.Sum(m => m.Value.Desks);
            var collaborationSeatsMetric = metricByLayouts.Sum(m => m.Value.CollaborationSeats);

            var headcount = -1;
            var deskSharingRatio = 1.0;

            if (input.CalculationMode == WorkplaceMetricsInputsCalculationMode.Headcount)
            {
                headcount = (int)metricByLayouts.Sum(m => m.Value.Headcount);
                deskSharingRatio = headcount / desksMetric;
            }
            else if (input.CalculationMode == WorkplaceMetricsInputsCalculationMode.Fixed_Headcount)
            {
                headcount = input.TotalHeadcount;
                deskSharingRatio = headcount / desksMetric;
            }
            else // fixed sharing ratio
            {
                deskSharingRatio = input.DeskSharingRatio;
                headcount = (int)Math.Round(desksMetric * deskSharingRatio);
            }

            var areaPerPerson = settings.UsableArea / headcount;
            var areaPerDesk = settings.UsableArea / desksMetric;
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
                at.AchievedCount = 
                    at.Name == _privateOffice ? metricByLayouts[at.Name].Headcount : 
                    at.Name == _phoneBooth ? metricByLayouts[at.Name].Seats : at.AchievedCount;
                at.SeatCount = layoutNames.Contains(at.Name) ? metricByLayouts[at.Name].Seats : at.SeatCount;
            }

            var totalCirculationArea = allSpaceBoundaries.Where(sb => sb.ProgramType == circulationKey).Sum(sb => sb.Area);

            var output = new WorkplaceMetricsOutputs
            {
                TotalUsableFloorArea = settings.UsableArea,
                TotalHeadcount = headcount,
                TotalDeskCount = desksMetric,
                AreaPerPerson = areaPerPerson,
                AreaPerDesk = areaPerDesk,
                CollaborationSeats = collaborationSeatsMetric,
                MeetingRoomSeats = metricByLayouts[_meetingRoom].Seats,
                ClassroomSeats = metricByLayouts[_classroom].Seats,
                PhoneBooths = metricByLayouts[_phoneBooth].Seats,
                PrivateOfficeCount = metricByLayouts[_privateOffice].Headcount,
                DeskSharingRatio = deskSharingRatio,
                MeetingRoomRatio = meetingRoomRatio,
                CirculationUSFRatio = totalCirculationArea / settings.UsableArea,
                Model = outputModel
            };

            output.Model.AddElements(areaTallies);

            if (warnings.Count > 0)
            {
                output.Warnings = warnings;
            }
            return output;
        }

        private static SpaceMetric CountWorkplaceTyped(
            Dictionary<string, Model> inputModels,
            WorkplaceMetricsInputs input,
            string layoutName,
            List<SpaceBoundary> boundaries,
            List<SpaceBoundary> openOfficeBoundaries,
            List<SpaceMetric> openCollabSpaceMetrics)
        {
            var metric = new SpaceMetric();
            if (inputModels.TryGetValue(layoutName + " Layout", out var layoutModel))
            {
                foreach (var sm in layoutModel.AllElementsOfType<SpaceMetric>())
                {
                    var room = boundaries.FirstOrDefault(b => b.Id == sm.Space);
                    if (room != null)
                    {
                        if (layoutName == _openOffice && openOfficeBoundaries != null && openCollabSpaceMetrics != null)
                        {
                            var openCollabBoundaries = openOfficeBoundaries.Where(b => room.Boundary.Perimeter.Contains(b.Boundary.Perimeter.Centroid()));
                            foreach (var openCollabBoundary in openCollabBoundaries)
                            {
                                var openCollabSM = openCollabSpaceMetrics.FirstOrDefault(osm => osm.Space == openCollabBoundary.Id);
                                sm.Seats += openCollabSM.Seats;
                                sm.Headcount += openCollabSM.Headcount;
                                sm.Desks += openCollabSM.Desks;
                                sm.CollaborationSeats += openCollabSM.CollaborationSeats;
                            }
                        }

                        metric.Seats += sm.Seats;
                        metric.Headcount += sm.Headcount;
                        metric.Desks += sm.Desks;
                        metric.CollaborationSeats += sm.CollaborationSeats;
                    }
                }
            }
            return metric;
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