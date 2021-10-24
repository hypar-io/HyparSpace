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
            var floorsModel = inputModels["Floors"];
            var zonesModel = inputModels["Space Planning Zones"];
            var allFloors = floorsModel.AllElementsOfType<Floor>();
            var totalFloorArea = 0.0;
            foreach (var floor in allFloors)
            {
                var difference = Profile.Difference(new[] { floor.Profile }, input.USFExclusions.Select(s => new Profile(s)));
                totalFloorArea += difference.Sum(d => d.Area());
            }

            var totalDeskCount = 0;
            if (inputModels.TryGetValue("Open Office Layout", out var openOfficeModel))
            {
                totalDeskCount += openOfficeModel.AllElementsOfType<WorkpointCount>()
                                                .Sum((wc => (wc.Type != null && wc.Type.Contains("Desk"))?wc.Count:0));
            }
            var totalMeetingRoomSeats = 0;
            if (inputModels.TryGetValue("Meeting Room Layout", out var meetingRoomModel))
            {
                totalMeetingRoomSeats += meetingRoomModel.AllElementsOfType<WorkpointCount>()
                                                        .Sum((wc => (wc.Type != null && wc.Type.Contains("Meeting Room Seat")) ? wc.Count : 0));
            }

            var meetingRoomCount = zonesModel.AllElementsOfType<SpaceBoundary>().Count(sb => sb.Name == "Meeting Room");

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
            var output = new WorkplaceMetricsOutputs(totalFloorArea, areaPerPerson, totalDeskCount, totalMeetingRoomSeats, 
                                                    headcount, areaPerDesk, deskSharingRatio, meetingRoomRatio);
            return output;
        }
    }
}