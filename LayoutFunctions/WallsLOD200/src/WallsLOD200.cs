using Elements;
using System.Linq;
using Elements.Search;
using Elements.Geometry;
using System.Collections.Generic;
using System.Diagnostics;

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
            List<Line> dedupedlines = null;

            // Merge collinear lines that are touching or overlapping
            List<Line> mergedLines = null;

            // Create stopwatch for timing
            Stopwatch stopwatch = new Stopwatch();

            // Number of iterations
            int iterations = 1;

            // Lists to store elapsed times for each method
            List<TimeSpan> removeDuplicateLinesTimes = new List<TimeSpan>();
            List<TimeSpan> mergeCollinearLinesTimes = new List<TimeSpan>();
            List<TimeSpan> removeDuplicateLines2Times = new List<TimeSpan>();
            List<TimeSpan> mergeCollinearLines2Times = new List<TimeSpan>();

            for (int i = 0; i < iterations; i++)
            {

                // Timing for RemoveDuplicateLines
                stopwatch.Restart();
                dedupedlines = RemoveDuplicateLines(lines);
                stopwatch.Stop();
                removeDuplicateLinesTimes.Add(stopwatch.Elapsed);

                // Timing for MergeCollinearLines2
                stopwatch.Restart();
                mergedLines = MergeCollinearLines(dedupedlines);
                stopwatch.Stop();
                mergeCollinearLinesTimes.Add(stopwatch.Elapsed);
            }

            // Calculate min, max, and average times for each method
            TimeSpan removeDuplicateLinesMin = removeDuplicateLinesTimes.Min();
            TimeSpan removeDuplicateLinesMax = removeDuplicateLinesTimes.Max();
            TimeSpan removeDuplicateLinesAverage = TimeSpan.FromTicks((long)removeDuplicateLinesTimes.Average(t => t.Ticks));

            TimeSpan mergeCollinearLinesMin = mergeCollinearLinesTimes.Min();
            TimeSpan mergeCollinearLinesMax = mergeCollinearLinesTimes.Max();
            TimeSpan mergeCollinearLinesAverage = TimeSpan.FromTicks((long)mergeCollinearLinesTimes.Average(t => t.Ticks));

            // Print results
            Console.WriteLine($"RemoveDuplicateLines: Min - {removeDuplicateLinesMin}, Max - {removeDuplicateLinesMax}, Average - {removeDuplicateLinesAverage}");
            Console.WriteLine($"MergeCollinearLines: Min -{mergeCollinearLinesMin}, Max - {mergeCollinearLinesMax}, Average - {mergeCollinearLinesAverage}");
            Console.WriteLine($"Merged {lines.Count} Lines into {mergedLines.Count}");

            return mergedLines;
        }

        private static List<Line> RemoveDuplicateLines(List<Line> lines)
        {
            HashSet<Line> uniqueLines = new HashSet<Line>(new LineEqualityComparer());

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

                            if (line.IsCollinear(otherLine) && (line.TryGetOverlap(otherLine, out var overlap) || line.DistanceTo(otherLine) < 0.0001))
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

        private class LineEqualityComparer : IEqualityComparer<Line>
        {
            public bool Equals(Line x, Line y)
            {
                // Check if start and end points of lines are equal
                return (x.Start == y.Start && x.End == y.End) || (x.Start == y.End && x.End == y.Start);
            }

            public int GetHashCode(Line obj)
            {
                // Compute hash code based on start and end points
                unchecked
                {
                    int hash = 17;
                    hash = hash * 23 + obj.Start.GetHashCode();
                    hash = hash * 23 + obj.End.GetHashCode();
                    return hash;
                }
            }
        }
    }
}