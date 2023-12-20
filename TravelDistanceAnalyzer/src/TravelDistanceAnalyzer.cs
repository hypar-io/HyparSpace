using Elements;
using Elements.Geometry;
using Elements.Spatial.AdaptiveGrid;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using AdaptiveGraphRouting = Elements.Spatial.AdaptiveGrid.AdaptiveGraphRouting;

namespace TravelDistanceAnalyzer
{
    public static class TravelDistanceAnalyzer
    {
        /// <summary>
        /// The TravelDistanceAnalyzer function.
        /// </summary>
        /// <param name="inputModels">The input model.</param>
        /// <param name="input">The arguments to the execution.</param>
        /// <returns>A TravelDistanceAnalyzerOutputs instance containing computed results and the model with any new elements.</returns>
        public static TravelDistanceAnalyzerOutputs Execute(Dictionary<string, Model> inputModels, TravelDistanceAnalyzerInputs input)
        {
            var output = new TravelDistanceAnalyzerOutputs();
            if (!inputModels.TryGetValue("Space Planning Zones", out var spaceZonesModel))
            {
                output.Errors.Add("The model output named 'Space Planning Zones' could not be found. Check the upstream functions for errors.");
                return output;
            }

            if (!inputModels.TryGetValue("Circulation", out var circulationModel))
            {
                output.Errors.Add("The model output named 'Circulation' could not be found. Check the upstream functions for errors.");
                return output;
            }

            var corridors = circulationModel.AllElementsOfType<CirculationSegment>();
            var rooms = spaceZonesModel.AllElementsOfType<SpaceBoundary>();

            List<Door>? doors = null;
            if (inputModels.TryGetValue("Doors", out var doorsModel))
            {
                doors = doorsModel.AllElementsOfType<Door>().ToList();
            }

            List<WallCandidate>? walls = null;
            if (inputModels.TryGetValue("Interior Partitions", out var wallsModel))
            {
                walls = wallsModel.AllElementsOfType<WallCandidate>().ToList();
            }

            List<Level>? levels = null;
            if (inputModels.TryGetValue("Levels", out var levelModel))
            {
                levels = levelModel.AllElementsOfType<Level>().ToList();
            }

            var corridorsByLevel = corridors.GroupBy(c => c.Level);
            var roomsByLevel = rooms.GroupBy(r => r.Level);

            var walkingDistanceConfigs = CreateWalkingDistanceConfigurations(input.Overrides);
            var routeDistanceConfigs = CreateRoutingDistanceConfigurations(input.Overrides);
            
            foreach (var levelRooms in roomsByLevel)
            {
                var levelCorridors = corridorsByLevel.Where(c => c.Key == levelRooms.Key).FirstOrDefault();
                if (levelCorridors == null || !levelCorridors.Any() ||
                    roomsByLevel == null || !roomsByLevel.Any())
                {
                    continue;
                }

                var level = levels?.FirstOrDefault(l => l.Id ==  levelRooms.Key);
                if (level == null)
                {
                    level = new Level(levelCorridors.First().Elevation, null, null);
                }

                var levelWalls = CollectWallsForLevel(walls, level);

                var builder = new AdaptiveGridBuilder();
                builder.Build(levelCorridors, levelRooms, levelWalls, doors);

                foreach (var config in walkingDistanceConfigs.Where(c => c.OnElevation(level.Elevation)))
                {
                    config.Compute(builder);
                }

                foreach (var config in routeDistanceConfigs.Where(c => c.OnElevation(level.Elevation)))
                {
                    config.Compute(builder);
                    output.Model.AddElement(config.GrawDestinationLabels());
                }
            }

            output.Model.AddElements(walkingDistanceConfigs);
            output.Model.AddElements(routeDistanceConfigs);
            return output;
        }

        private static List<WallCandidate>? CollectWallsForLevel(List<WallCandidate>? allWalls, Level level)
        {
            List<WallCandidate>? levelWalls = null;
            if (allWalls != null)
            {
                levelWalls = new List<WallCandidate>();
                foreach (var item in allWalls)
                {
                    // Ignore wall candidates with zero thickness as they produce no walls.
                    // Previously open walls did not have wall candidates, but now they are.
                    // Its either transition to use actual walls or do this thickness check.
                    // Thickness is only available in additional properties and require ugly code.
                    if (item.AdditionalProperties.TryGetValue("Thickness", out var obj))
                    {
                        if (obj is JObject jobj)
                        {
                            var tuple = jobj.ToObject<(double, double)?>();
                            if (tuple != null && 
                                tuple.Value.Item1.ApproximatelyEquals(0) &&
                                tuple.Value.Item2.ApproximatelyEquals(0))
                            {
                                continue;
                            }
                        }
                    }

                    if (item.Line.Start.Z.ApproximatelyEquals(level.Elevation))
                    {
                        levelWalls.Add(item);
                    }
                    // Some walls have their lines set to 0.
                    // This is hack not to ignore them while the issue is not fixed.
                    else if (item.AdditionalProperties.TryGetValue("Height", out var height))
                    {
                        if (height is double H && 
                            item.Line.Start.Z < level.Elevation && 
                            item.Line.Start.Z + H - Vector3.EPSILON > level.Elevation)
                        {
                            item.Line = new Line(new Vector3(item.Line.Start.X, item.Line.Start.Y, level.Elevation),
                                                 new Vector3(item.Line.End.X, item.Line.End.Y, level.Elevation));
                            levelWalls.Add(item);
                        }
                    }
                }
            }
            return levelWalls;
        }

        private static List<RouteDistanceConfiguration> CreateRoutingDistanceConfigurations(Overrides overrides)
        {
            var routeDistanceConfigs = RouteDistanceOverrideExtensions.CreateElements(
                overrides.RouteDistance,
                overrides.Additions.RouteDistance,
                overrides.Removals.RouteDistance,
            (add) =>
            {
                RouteDistanceConfiguration config = new RouteDistanceConfiguration(
                    add.Id, add.Value.Destinations);
                return config;
            },
            (elem, identity) =>
            {
                return elem.AddId == identity.AddId;
            },
            (elem, edit) =>
            {
                elem.Destinations = edit.Value.Destinations.ToList();
                elem.Color = edit.Value.Color;
                return elem;
            });
            return routeDistanceConfigs;
        }

        private static List<WalkingDistanceConfiguration> CreateWalkingDistanceConfigurations(Overrides overrides)
        {
            var walkingDistanceConfigs = WalkingDistanceOverrideExtensions.CreateElements(
                overrides.WalkingDistance,
                overrides.Additions.WalkingDistance,
                overrides.Removals.WalkingDistance,
            (add) =>
            {
                WalkingDistanceConfiguration config = new WalkingDistanceConfiguration(
                    add.Id, add.Value.ProgramTypes, add.Value.Transform);
                return config;
            },
            (elem, identity) =>
            {
                return elem.AddId == identity.AddId;
            },
            (elem, edit) =>
            {
                elem.Transform = edit.Value.Transform;
                elem.ProgramTypes = edit.Value.ProgramTypes.ToList();
                elem.Color = edit.Value.Color;
                return elem;
            });
            return walkingDistanceConfigs;
        }

        private static IList<Element> GetGridDebugVisualization(AdaptiveGrid grid)
        {
            var a = new AdaptiveGraphRouting(grid, new RoutingConfiguration());
            return a.RenderElements(new List<RoutingHintLine>(), new List<Vector3>());
        }
    }
}