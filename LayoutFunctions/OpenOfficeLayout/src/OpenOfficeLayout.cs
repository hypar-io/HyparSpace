using Elements;
using Elements.Geometry;
using Elements.Spatial;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Elements.Components;
using Elements.Geometry.Solids;
using LayoutFunctionCommon;
using Hypar.Model;

namespace OpenOfficeLayout
{
    public static class OpenOfficeLayout
    {
        static string[] _columnSources = new[] { "Columns", "Structure" };

        static string[] _doubleDeskTypes = new[] { "Double Desk", "120° Workstations - Pairs", "120° Workstations - Continuous" };

        static Dictionary<string, int> _desksPerConfig = new Dictionary<string, int>
        {
            { "Double Desk", 2 },
            { "120° Workstations - Pairs", 6 },
            { "120° Workstations - Continuous", 6 },
            {"Enclosed Pair", 2}
        };

        /// <summary>
        /// The OpenOfficeLayout function.
        /// </summary>
        /// <param name="model">The input model.</param>
        /// <param name="input">The arguments to the execution.</param>
        /// <returns>A OpenOfficeLayoutOutputs instance containing computed results and the model with any new elements.</returns>
        public static OpenOfficeLayoutOutputs Execute(Dictionary<string, Model> inputModels, OpenOfficeLayoutInputs input)
        {
            Elements.Serialization.glTF.GltfExtensions.UseReferencedContentExtension = true;
            // var catalog = JsonConvert.DeserializeObject<ContentCatalog>(File.ReadAllText("./catalog.json"));
            string configJsonPath = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "OpenOfficeDeskConfigurations.json");
            var spacePlanningZones = inputModels["Space Planning Zones"];
            var levels = spacePlanningZones.AllElementsOfType<LevelElements>();
            if (inputModels.TryGetValue("Circulation", out var circModel))
            {
                var circSegments = circModel.AllElementsOfType<CirculationSegment>();
                foreach (var cs in circSegments)
                {
                    var matchingLevel = levels.FirstOrDefault(l => l.Level == cs.Level);
                    if (matchingLevel != null)
                    {
                        matchingLevel.Elements.Add(cs);
                    }
                }
            }

            var configJson = File.ReadAllText(configJsonPath);
            var configs = JsonConvert.DeserializeObject<SpaceConfiguration>(configJson);

            if (input.CustomWorkstationProperties == null)
            {
                input.CustomWorkstationProperties = new CustomWorkstationProperties(2, 2);
            }
            configs["Custom"] = new ContentConfiguration()
            {
                CellBoundary = new ContentConfiguration.BoundaryDefinition()
                {
                    Min = (0, 0, 0),
                    Max = (input.CustomWorkstationProperties.Width, input.CustomWorkstationProperties.Length, 0)
                },
                ContentItems = new List<ContentConfiguration.ContentItem>()
            };

            var defaultCustomDesk = new CustomWorkstation(input.CustomWorkstationProperties.Width, input.CustomWorkstationProperties.Length);
            var defaultDeskTypeName = Hypar.Model.Utilities.GetStringValueFromEnum(input.DeskType);
            var defaultConfig = configs[defaultDeskTypeName];

            var output = new OpenOfficeLayoutOutputs();

            var avoidanceStrat = input.ColumnAvoidanceStrategy;
            var overridesBySpaceBoundaryId = LayoutStrategies.GetOverridesBySpaceBoundaryId<SpaceSettingsOverride, SpaceBoundary, LevelElements>(input.Overrides?.SpaceSettings, (ov) => ov.Identity.ParentCentroid, levels);

            var columnSearchTree = new SearchablePointCollection<Profile>();
            if (avoidanceStrat != OpenOfficeLayoutInputsColumnAvoidanceStrategy.None)
            {
                // Get column locations from model
                List<(Vector3, Profile)> modelColumnLocations = new List<(Vector3, Profile)>();
                foreach (var source in _columnSources)
                {
                    if (inputModels.ContainsKey(source))
                    {
                        var sourceData = inputModels[source];
                        modelColumnLocations.AddRange(LayoutStrategies.GetColumnLocations(sourceData));
                    }
                }
                columnSearchTree.AddRange(modelColumnLocations);
            }


            var defaultAisleWidth = double.IsNaN(input.AisleWidth) ? 1 : input.AisleWidth;
            var defaultBackToBackWidth = double.IsNaN(input.BackToBackWidth) ? 1 : input.BackToBackWidth;
            var totalDeskCount = 0;
            foreach (var lvl in levels)
            {
                var corridors = lvl.Elements.OfType<CirculationSegment>();
                var corridorSegments = corridors.SelectMany(p => p.Profile.Segments());
                var officeBoundaries = lvl.Elements.OfType<SpaceBoundary>().Where(z => z.Name == "Open Office");
                foreach (var ob in officeBoundaries)
                {
                    var proxy = LayoutStrategies.CreateSettingsProxy(input.IntegratedCollaborationSpaceDensity, input.GridRotation, defaultAisleWidth, ob, Utilities.GetStringValueFromEnum(input.DeskType));
                    output.Model.AddElement(proxy);
                    var isCustom = defaultDeskTypeName == "Custom";
                    CustomWorkstation customDesk = null;
                    var (selectedConfig,
                        rotation,
                        collabDensity,
                        aisleWidth,
                        backToBackWidth,
                        deskTypeName) = LayoutStrategies.GetSpaceSettings<SpaceBoundary, SpaceSettingsOverride, SpaceSettingsValue>(
                            ob,
                            defaultConfig,
                            input.GridRotation,
                            input.IntegratedCollaborationSpaceDensity,
                            defaultAisleWidth,
                            defaultBackToBackWidth,
                            defaultDeskTypeName,
                            overridesBySpaceBoundaryId,
                            configs,
                            proxy, (SpaceSettingsOverride spaceOverride) =>
                            {
                                isCustom = spaceOverride.Value.DeskType == SpaceSettingsValueDeskType.Custom;
                                if (!isCustom)
                                {
                                    return null;
                                }
                                var selectedConfig = new ContentConfiguration()
                                {
                                    CellBoundary = new ContentConfiguration.BoundaryDefinition()
                                    {
                                        Min = (0, 0, 0),
                                        Max = (spaceOverride.Value.CustomWorkstationProperties.Width, spaceOverride.Value.CustomWorkstationProperties.Length, 0)
                                    },
                                    ContentItems = new List<ContentConfiguration.ContentItem>()
                                };
                                customDesk = new CustomWorkstation(spaceOverride.Value.CustomWorkstationProperties.Width, spaceOverride.Value.CustomWorkstationProperties.Length);
                                Identity.AddOverrideIdentity(ob, spaceOverride);
                                return selectedConfig;
                            });
                    var spaceBoundary = ob.Boundary;

                    var orientationTransform = LayoutStrategies.GetOrientationTransform(spaceBoundary, corridorSegments, rotation);

                    var validGrids = LayoutStrategies.GetValidGrids(spaceBoundary, orientationTransform, columnSearchTree, Utilities.GetStringValueFromEnum(avoidanceStrat));

                    foreach (var grid in validGrids)
                    {
                        try
                        {
                            var (desks, deskCount, collabProfiles) = LayoutStrategies.LayoutDesksInGrid(
                                grid,
                                ob,
                                selectedConfig,
                                aisleWidth,
                                backToBackWidth,
                                _doubleDeskTypes,
                                _desksPerConfig,
                                deskTypeName,
                                collabDensity,
                                Utilities.GetStringValueFromEnum(avoidanceStrat),
                                columnSearchTree,
                                orientationTransform,
                                isCustom ? customDesk : null);
                            output.Model.AddElements(desks);
                            totalDeskCount += deskCount;
                            foreach (var profile in collabProfiles)
                            {
                                var sb = SpaceBoundary.Make(profile, "Open Collaboration", ob.Transform.Concatenated(new Transform(0, 0, -0.03)), 3, profile.Perimeter.Centroid(), profile.Perimeter.Centroid());
                                sb.Representation = new Representation(new[] { new Lamina(profile.Perimeter, false) });
                                sb.AdditionalProperties.Add("Parent Level Id", lvl.Id);
                                output.Model.AddElement(sb);
                            }
                        }
                        catch (Exception e)
                        {
                            output.Warnings.Add($"Area skipped: Caught exception in desk layout: \"{e.Message}.");
                        }
                    }
                }
            }

            OverrideUtilities.InstancePositionOverrides(input.Overrides, output.Model);


            output.Model.AddElement(new WorkpointCount() { Count = totalDeskCount, Type = "Desk" });
            output.TotalDeskCount = totalDeskCount;
            return output;
        }
    }
}