using Elements;
using Elements.Geometry;
using System.Collections.Generic;
using LayoutFunctionCommon;

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
            var modelDependences = new[] {
                "Private Office Layout",
                "Phone Booth Layout",
                "Classroom Layout",
                "Meeting Room Layout",
                "Space Planning Zones",
                "Bedroom Layout",
                "Living Room Layout",
                "Kitchen Layout",
                "Workshop Layout",
                "Home Office Layout",
                "Bathroom Layout",
                "Restroom Layout",
                "Laundry Room Layout",
                "Entertainment Room Layout"
                 };
            foreach (var md in modelDependences)
            {
                if (inputModels.TryGetValue(md, out var mdModel))
                {
                    interiorPartitionCandidates.AddRange(mdModel?.AllElementsOfType<InteriorPartitionCandidate>());
                }

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