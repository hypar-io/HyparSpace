using DotLiquid.Tags;
using Elements;
using Elements.Geometry;

namespace WallsLOD200
{
    public static partial class WallsLOD200
    {
        public static double tolerance = 0.0001;
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

                var levels = new List<Level>();
                if (inputModels.TryGetValue("Levels", out var levelsModel))
                {
                    levels = levelsModel.AllElementsOfType<Level>().ToList();
                }

                levels.Sort((level1, level2) => level1.Elevation.CompareTo(level2.Elevation));
                walls = SplitWallsByLevels(walls.ToList(), levels, random);

                var wallGroups = walls.GroupBy(w => w.AdditionalProperties["Level"] ?? w.Transform.Origin.Z);

                foreach (var group in wallGroups)
                {
                    var level = levels.FirstOrDefault(l => l.Id.ToString() == group.Key.ToString()) ?? new Level(0, 3, null);
                    var lines = UnifyLines(group.ToList().Select(wall =>
                    {
                        var transform = new Transform(wall.Transform);
                        transform.Move(0, 0, -level.Elevation); // To keep the level.Elevation logic below, negate the wall's Z-position.
                        return wall.CenterLine.TransformedLine(transform);
                    }).ToList());
                    var roundedZLines = lines.Select(l =>
                        {
                            var roundedStart = new Vector3(l.Start.X, l.Start.Y, Math.Round(l.Start.Z, 5));
                            var roundedEnd = new Vector3(l.End.X, l.End.Y, Math.Round(l.End.Z, 5));
                            return new Line(roundedStart, roundedEnd);
                        }
                    );
                    var newWalls = roundedZLines.Select(mc => new StandardWall(mc, 0.1, level.Height ?? 3, random.NextMaterial(), new Transform().Moved(0, 0, level.Elevation)));
                    output.Model.AddElements(newWalls);
                }
            }

            return output;
        }

        public static List<StandardWall> SplitWallsByLevels(List<StandardWall> walls, List<Level> levels, Random random)
        {
            var newWalls = new List<StandardWall>();

            foreach (var wall in walls)
            {
                if (wall.AdditionalProperties.TryGetValue("Level", out var levelIdObj) && Guid.TryParse(levelIdObj.ToString(), out var levelId))
                {
                    var wallLevel = levels.FirstOrDefault(level => level.Id == levelId);

                    if (wallLevel != null && wall.Height > wallLevel.Height)
                    {
                        double remainingHeight = wall.Height;

                        for (int i = levels.IndexOf(wallLevel); i < levels.Count - 1; i++)
                        {
                            if (remainingHeight <= 0) break;

                            double segmentHeight = levels[i + 1].Elevation - levels[i].Elevation;

                            if (segmentHeight > remainingHeight)
                            {
                                segmentHeight = remainingHeight;
                            }

                            var newWall = new StandardWall(
                                wall.CenterLine,
                                0.1,
                                segmentHeight,
                                random.NextMaterial(),
                                wall.Transform.Moved(0, 0, levels[i].Elevation - wallLevel.Elevation))
                            {
                                AdditionalProperties = new Dictionary<string, object>(wall.AdditionalProperties)
                            };

                            newWall.AdditionalProperties["Level"] = levels[i].Id.ToString();
                            newWalls.Add(newWall);

                            remainingHeight -= segmentHeight;
                        }
                    }
                    else
                    {
                        newWalls.Add(wall);
                    }
                }
                else
                {
                    newWalls.Add(wall);
                }
            }

            return newWalls;
        }

        public static List<Line> UnifyLines(List<Line> lines)
        {
            // Remove duplicate lines
            List<Line> dedupedlines = RemoveDuplicateLines(lines);
            // Merge collinear lines that are touching, overlapping or nearly so
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
                        lineGroups[kvp.Key].Add(line);
                        addedToGroup = true;
                        break;
                    }
                }

                if (!addedToGroup)
                {
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

                            if (line.TryGetOverlap(otherLine, out var overlap) || line.DistanceTo(otherLine) < tolerance)
                            {
                                // we project the lines because line.IsCollinear resolves to true on
                                // near 0 differences which MergedCollinearLine does not tolerate
                                // originally we validated if projection was necessary using line.DistanceTo,
                                // but it is similarly fuzzy and resolves to 0 on near (but greater than epsilon) distances
                                otherLine = otherLine.Projected(line);
                                Line mergedLine = line.MergedCollinearLine(otherLine);

                                mergedLines.RemoveAt(j);
                                mergedLines[i] = mergedLine;

                                linesMerged = true;
                                break;
                            }
                        }
                        if (linesMerged)
                            break;
                    }
                } while (linesMerged);
                merged.AddRange(mergedLines);
            }
            return merged;
        }
    }
}