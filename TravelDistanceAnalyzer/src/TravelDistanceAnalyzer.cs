using Elements;
using Elements.Geometry;
using Elements.Spatial.AdaptiveGrid;
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

                var builder = new AdaptiveGridBuilder();
                builder.Build(levelCorridors, levelRooms, walls, doors);

                var elevation = levelCorridors.First().Elevation;

                foreach (var config in walkingDistanceConfigs.Where(c => c.OnElevation(elevation)))
                {
                    output.Model.AddElements(config.Compute(builder));
                }

                foreach (var config in routeDistanceConfigs.Where(c => c.OnElevation(elevation)))
                {
                    output.Model.AddElements(config.Compute(builder));
                }
                
                //Grid visualization for debug purposes
                var a = new AdaptiveGraphRouting(builder.Grid, new RoutingConfiguration());
                var elements = a.RenderElements(
                   new List<RoutingHintLine>(),
                   new List<Vector3>());
                output.Model.AddElements(elements);
            }

            output.Model.AddElements(walkingDistanceConfigs);
            output.Model.AddElements(routeDistanceConfigs);
            return output;
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
    }
}