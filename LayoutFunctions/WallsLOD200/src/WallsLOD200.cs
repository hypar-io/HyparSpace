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

            var unitSystem = "metric";

            if (inputModels.TryGetValue("Project Settings", out var settingsModel))
            {
                var unitSystemObject = settingsModel.Elements.FirstOrDefault(e => e.Value.AdditionalProperties["discriminator"].ToString() == "Elements.ProjectSettings");

                if (unitSystemObject.Key != Guid.Empty &&
                    unitSystemObject.Value.AdditionalProperties.TryGetValue("UnitSystem", out var unitSystemValue) &&
                    unitSystemValue.ToString() != null)
                {
                    unitSystem = unitSystemValue.ToString() ?? "metric";
                }
            }

            if (!inputModels.TryGetValue("Walls", out var wallsModel))
            {
                return output;
            }
            var walls = wallsModel.AllElementsOfType<StandardWall>();

            // if the unit system is metric, convert all 0.13335 thick walls to 0.135
            // if the unit system is imperial, convert all 0.135 thick walls to 0.13335
            walls
                .Where(w => (unitSystem.Equals("metric") && w.Thickness == 0.13335) ||
                            (unitSystem.Equals("imperial") && w.Thickness == 0.135))
                .ToList()
                .ForEach(w => w.Thickness = unitSystem.Equals("metric") ? 0.135 : 0.13335);

            var levels = new List<Level>();
            if (inputModels.TryGetValue("Levels", out var levelsModel))
            {
                levels = levelsModel.AllElementsOfType<Level>().DistinctBy((x) => x.Elevation).ToList();
            }

            walls = SplitWallsByLevels(walls, levels, random);

            var wallsByLevel = walls.GroupBy(w => w.AdditionalProperties["Level"] ?? w.Transform.Origin.Z);

            var newWallsByLevel = wallsByLevel.SelectMany((wallsOnLevel) =>
            {
                var level = levels.FirstOrDefault(l => l.Id.ToString() == wallsOnLevel.Key.ToString()) ?? new Level(0, 3, null);
                var levelHeight = ComputeLevelHeight(level, levels);
                var newWalls = new List<StandardWall>();

                var idx = new OverlapIndex<StandardWall>(-0.001, wallsOnLevel.Max(w => w.Thickness));
                foreach (var wall in wallsOnLevel)
                {
                    try
                    {
                        // We sometimes get exceptions in TransformedLine if the transform is bad,
                        var transformedLine = wall.CenterLine.TransformedLine(wall.Transform);
                        idx.AddItem(wall, transformedLine, wall.Thickness);
                    }
                    catch (Exception e)
                    {
                        // Handle the exception if needed
                        Console.WriteLine($"Error adding wall to index: {e.Message}");
                    }
                }

                var groups = idx.GetOverlapGroups();

                var mergedWalls = groups.SelectMany((g) =>
                {
                    return g.FatLines.Select((wall) =>
                    {
                        var mergedWall = new StandardWall(wall.Centerline, wall.Thickness, levelHeight ?? 3, random.NextMaterial(), new Transform().Moved(0, 0, level.Elevation));
                        mergedWall.AdditionalProperties["Level"] = level.Id.ToString();
                        return mergedWall;
                    });
                });

                return mergedWalls;
            });

            output.Model.AddElements(newWallsByLevel);

            return output;
        }

        private static double? ComputeLevelHeight(Level level, List<Level> levels)
        {
            var sortedLevels = levels.OrderBy(level => level.Elevation).ToList();
            var wallLevelIndex = sortedLevels.FindIndex(l => l.Id == level.Id);
            if (wallLevelIndex == -1)
            {
                return null;
            }
            var wallLevel = sortedLevels[wallLevelIndex];
            var levelAboveIndex = wallLevelIndex + 1;
            if (levelAboveIndex >= sortedLevels.Count)
            {
                return null;
            }
            var levelAbove = sortedLevels[levelAboveIndex];
            return levelAbove.Elevation - wallLevel.Elevation;
        }

        public static List<StandardWall> SplitWallsByLevels(IEnumerable<StandardWall> walls, List<Level> levels, Random random)
        {
            List<Level> sortedLevels = levels.OrderBy(level => level.Elevation).ToList();
            var newWalls = new List<StandardWall>();

            foreach (var wall in walls)
            {
                // If there is only one level, no splitting is required
                if (sortedLevels.Count == 1)
                {
                    newWalls.Add(wall);
                    continue;
                }

                if (!(wall.AdditionalProperties.TryGetValue("Level", out var levelIdObj) && Guid.TryParse(levelIdObj.ToString(), out var levelId)))
                {
                    // If we can't get the walls level then we can't reasonably split the wall by other levels
                    newWalls.Add(wall);
                    continue;
                }

                var wallLevel = sortedLevels.FirstOrDefault(level => level.Id == levelId);
                if (wallLevel == null)
                {
                    // If we can't find the walls level
                    newWalls.Add(wall);
                    continue;
                }
                var wallLevelHeight = ComputeLevelHeight(wallLevel, sortedLevels);
                if (wallLevelHeight == null)
                {
                    newWalls.Add(wall);
                    continue;
                }

                if (wall.Height < wallLevelHeight)
                {
                    // if the wall is shorter than the level height then it doesn't need to split
                    newWalls.Add(wall);
                    continue;
                }

                double remainingHeight = wall.Height;

                for (int i = sortedLevels.IndexOf(wallLevel); i < sortedLevels.Count - 1; i++)
                {
                    if (remainingHeight <= 0) break;

                    double segmentHeight = sortedLevels[i + 1].Elevation - sortedLevels[i].Elevation;

                    if (segmentHeight > remainingHeight)
                    {
                        segmentHeight = remainingHeight;
                    }

                    var newWall = new StandardWall(
                        wall.CenterLine,
                        wall.Thickness,
                        segmentHeight,
                        random.NextMaterial(),
                        wall.Transform.Moved(0, 0, sortedLevels[i].Elevation - wallLevel.Elevation))
                    {
                        AdditionalProperties = new Dictionary<string, object>(wall.AdditionalProperties)
                    };

                    newWall.AdditionalProperties["Level"] = sortedLevels[i].Id.ToString();
                    newWalls.Add(newWall);

                    remainingHeight -= segmentHeight;
                }
            }

            return newWalls;
        }
    }
}