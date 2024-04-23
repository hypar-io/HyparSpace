using Elements;
using Elements.Geometry;

namespace WallsLOD200
{
    public static partial class WallsLOD200
    {
        /// <summary>
        /// The WallsLOD200 function.
        /// </summary>
        /// <param name="model">The input model.</param>
        /// <param name="input">The arguments to the execution.</param>
        /// <returns>A WallsLOD200Outputs instance containing computed results and the model with any new elements.</returns>
        public static WallsLOD200Outputs Execute(Dictionary<string, Model> inputModels, WallsLOD200Inputs input)
        {
            Random random = new Random(21);
            var output = new WallsLOD200Outputs();

            if (inputModels.TryGetValue("Walls", out var wallsModel))
            {
                var walls = wallsModel.AllElementsOfType<StandardWall>();
                var wallGroups = walls.GroupBy(w => w.AdditionalProperties["Level"] ?? w.Transform.Origin.Z);

                var levels = new List<Level>();
                if (inputModels.TryGetValue("Levels", out var levelsModel))
                {
                    levels = levelsModel.AllElementsOfType<Level>().ToList();
                }

                foreach (var group in wallGroups)
                {
                    var level = levels.FirstOrDefault(l => l.Id.ToString() == group.Key.ToString()) ?? new Level(0, 3, null);
                    var lines = UnifyLines(group.ToList().Select(g => g.CenterLine).ToList());
                    var newwalls = lines.Select(mc => new StandardWall(mc, 0.1, level.Height ?? 3, random.NextMaterial(), new Transform().Moved(0, 0, level.Elevation)));
                    output.Model.AddElements(newwalls);
                }
            }

            return output;
        }

        public static List<Line> UnifyLines(List<Line> lines)
        {
            // Remove duplicate lines based on their hash codes
            List<Line> dedupedlines = RemoveDuplicateLines(lines);
            // Merge collinear lines that are touching or overlapping
            List<Line> mergedLines = MergeCollinearLines(dedupedlines);

            return mergedLines;
        }

        private static List<Line> RemoveDuplicateLines(List<Line> lines)
        {
            HashSet<Line> uniqueLines = new(new LineEqualityComparer());

            foreach (Line line in lines)
            {
                uniqueLines.Add(line);
            }

            return uniqueLines.ToList();
        }

        static List<List<Line>> GroupLinesByCollinearity(List<Line> lines)
        {
            Dictionary<int, Line> collinearGroups = new Dictionary<int, Line>();
            List<List<Line>> lineGroups = new List<List<Line>>();
            int groupId = 0;

            foreach (var line in lines)
            {
                bool addedToGroup = false;
                foreach (var kvp in collinearGroups)
                {
                    if (line.IsCollinear(kvp.Value))
                    {
                        // Add line to existing group
                        lineGroups[kvp.Key].Add(line);
                        addedToGroup = true;
                        break;
                    }
                }

                if (!addedToGroup)
                {
                    // Create new group
                    collinearGroups.Add(groupId, line);
                    lineGroups.Add(new List<Line>() { line });
                    groupId++;
                }
            }

            return lineGroups;
        }

        private static List<Line> MergeCollinearLines(List<Line> lines)
        {
            var groupedLines = GroupLinesByCollinearity(lines);

            List<Line> merged = new List<Line>();

            foreach (var group in groupedLines)
            {
                List<Line> mergedLines = new List<Line>(group);

                bool linesMerged;
                do
                {
                    linesMerged = false;
                    for (int i = 0; i < mergedLines.Count; i++)
                    {
                        Line line = mergedLines[i];
                        for (int j = i + 1; j < mergedLines.Count; j++)
                        {
                            Line otherLine = mergedLines[j];

                            if (line.TryGetOverlap(otherLine, out var overlap) || line.DistanceTo(otherLine) < 0.0001)
                            {
                                // Merge collinear lines
                                Line mergedLine = line.MergedCollinearLine(otherLine);

                                // Update the list with the merged line
                                mergedLines.RemoveAt(j);
                                mergedLines[i] = mergedLine;

                                linesMerged = true;
                                break; // Exit the inner loop as we have merged the lines
                            }
                        }
                        if (linesMerged)
                            break; // Exit the outer loop to restart the merging process
                    }
                } while (linesMerged);
                merged.AddRange(mergedLines);
            }
            return merged;
        }
    }
}