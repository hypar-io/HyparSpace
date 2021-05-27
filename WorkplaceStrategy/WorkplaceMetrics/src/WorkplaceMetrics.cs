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

            var deskCount = 0;
            if (inputModels.TryGetValue("Open Office Layout", out var openOfficeModel))
            {
                deskCount += openOfficeModel.AllElementsOfType<WorkpointCount>().Sum(wc => wc.Count);
            }

            var meetingRoomCount = zonesModel.AllElementsOfType<SpaceBoundary>().Count(sb => sb.Name == "Meeting Room");

            var headcount = -1;
            var deskSharingRatio = 1.0;

            if (input.CalculationMode == WorkplaceMetricsInputsCalculationMode.Fixed_Headcount)
            {
                headcount = input.TotalHeadcount;
                deskSharingRatio = headcount / (double)deskCount;
            }
            else // fixed sharing ratio
            {
                deskSharingRatio = input.DeskSharingRatio;
                headcount = (int)Math.Round(deskCount * deskSharingRatio);

            }
            var areaPerPerson = totalFloorArea / headcount;
            var totalDeskCount = deskCount;
            var areaPerDesk = totalFloorArea / deskCount;
            var meetingRoomRatio = meetingRoomCount == 0 ? 0 : (int)Math.Round(headcount / (double)meetingRoomCount);
            var output = new WorkplaceMetricsOutputs(totalFloorArea, areaPerPerson, totalDeskCount, headcount, areaPerDesk, deskSharingRatio, meetingRoomRatio);
            return output;
        }
    }
}