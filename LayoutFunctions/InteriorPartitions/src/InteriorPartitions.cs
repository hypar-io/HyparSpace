using Elements;
using Elements.Geometry;
using System.Collections.Generic;

namespace InteriorPartitions
{
    public static class InteriorPartitions
    {
        /// <summary>
        /// The InteriorPartitions function.
        /// </summary>
        /// <param name="model">The input model.</param>
        /// <param name="input">The arguments to the execution.</param>
        /// <returns>A InteriorPartitionsOutputs instance containing computed results and the model with any new elements.</returns>
        public static InteriorPartitionsOutputs Execute(Dictionary<string, Model> inputModels, InteriorPartitionsInputs input)
        {
            var interiorPartitionCandidates = new List<InteriorPartitionCandidate>();
            if (inputModels.TryGetValue("Private Office Layout", out var privateOfficeLayoutModel))
            {
                interiorPartitionCandidates.AddRange(privateOfficeLayoutModel?.AllElementsOfType<InteriorPartitionCandidate>());
            }
            if (inputModels.TryGetValue("Phone Booth Layout", out var phoneBoothLayoutModel))
            {
                interiorPartitionCandidates.AddRange(phoneBoothLayoutModel?.AllElementsOfType<InteriorPartitionCandidate>());
            }
            if (inputModels.TryGetValue("Classroom Layout", out var classroomLayoutModel))
            {
                interiorPartitionCandidates.AddRange(classroomLayoutModel?.AllElementsOfType<InteriorPartitionCandidate>());
            }
            if (inputModels.TryGetValue("Meeting Room Layout", out var meetingRoomLayoutModel))
            {
                interiorPartitionCandidates.AddRange(meetingRoomLayoutModel?.AllElementsOfType<InteriorPartitionCandidate>());
            }

            var output = new InteriorPartitionsOutputs();
            foreach (var interiorPartitionCandidate in interiorPartitionCandidates)
            {
                WallGeneration.GenerateWalls(output.Model, interiorPartitionCandidate.WallCandidateLines, interiorPartitionCandidate.Height, interiorPartitionCandidate.LevelTransform);
            }

            return output;
        }
    }
}