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

            var totalDeskCount        = CountWorkplaceTyped(inputModels, "Open Office Layout",        "Desk");
            var totalMeetingRoomSeats = CountWorkplaceTyped(inputModels, "Meeting Room Layout",       "Meeting Room Seat");
            var totalClassroomSeats   = CountWorkplaceTyped(inputModels, "Classroom Layout",          "Classroom Seat");
            var totalPhoneBooths      = CountWorkplaceTyped(inputModels, "Phone Booth Layout",        "Phone Booth");
            var totalOpenCollabSeats  = CountWorkplaceTyped(inputModels, "Open Collaboration Layout", "Collaboration seat");

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
                                                    meetingRoomRatio
                                                    );
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
    }
}