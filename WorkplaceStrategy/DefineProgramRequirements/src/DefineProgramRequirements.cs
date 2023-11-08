using Elements;
using Elements.Geometry;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Elements.Components;

namespace DefineProgramRequirements
{
    public static class DefineProgramRequirements
    {
        /// <summary>
        /// The DefineProgramRequirements function.
        /// </summary>
        /// <param name="model">The input model.</param>
        /// <param name="input">The arguments to the execution.</param>
        /// <returns>A DefineProgramRequirementsOutputs instance containing computed results and the model with any new elements.</returns>
        public static DefineProgramRequirementsOutputs Execute(Dictionary<string, Model> inputModels, DefineProgramRequirementsInputs input)
        {
            var programRequirements = input.ProgramRequirements.ToList();
            var output = new DefineProgramRequirementsOutputs();
            if (programRequirements.Select(p => p.ProgramName + p.ProgramGroup).Distinct().Count() != programRequirements.Count)
            {
                output.Errors.Add("No two programs can have the same Program Name. Please remove one of the duplicates.");
            }
            foreach (var pr in programRequirements)
            {
                pr.AdditionalProperties.Clear();
            }
            var sum = programRequirements.Sum(p => p.AreaPerSpace * p.SpaceCount);
            var colorScheme = ColorScheme.ProgramColors;

            Dictionary<string, CatalogWrapper> wrappers = new Dictionary<string, CatalogWrapper>();
            Dictionary<string, SpaceConfigurationElement> spaceConfigurations = new Dictionary<string, SpaceConfigurationElement>();

            // AddDefaultProgramTypes(programRequirements);

            foreach (var req in programRequirements)
            {
                colorScheme.Mapping[req.QualifiedProgramName] = req.Color ?? Colors.Magenta;
                var layoutType = req.LayoutType;
                CatalogWrapper catalogWrapper = null;
                SpaceConfigurationElement spaceConfigElem = null;
                if (req.LayoutType?.Name != null)
                {
                    req.HyparSpaceType = req.LayoutType.Name;
                }
                if (req.LayoutType != null && req.LayoutType.Files != null && req.LayoutType.Files.Count > 0)
                {
                    var configFile = req.LayoutType.Files;
                    foreach (var file in configFile)
                    {
                        Console.WriteLine("\t" + file.FileName);
                        Console.WriteLine("\t\t" + file.LocalFilePath);
                    }
                    foreach (var file in configFile)
                    {
                        Console.WriteLine(file.FileName);
                        if (file.FileName.EndsWith(".hycatalog"))
                        {
                            if (wrappers.ContainsKey(file.LocalFilePath))
                            {
                                catalogWrapper = wrappers[file.LocalFilePath];
                                continue;
                            }
                            if (!File.Exists(file.LocalFilePath))
                            {
                                Console.WriteLine($"Could not find catalog file {file.LocalFilePath}.");
                                continue;
                            }

                            var catalogBytes = File.ReadAllBytes(file.LocalFilePath);
                            // encode bytes as base64 string
                            var catalogString = Convert.ToBase64String(catalogBytes);
                            catalogWrapper = new CatalogWrapper
                            {
                                CatalogString = catalogString
                            };
                            output.Model.AddElement(catalogWrapper);
                            wrappers[file.LocalFilePath] = catalogWrapper;
                        }
                        if (file.FileName.EndsWith(".hyspacetype"))
                        {
                            if (spaceConfigurations.ContainsKey(file.LocalFilePath))
                            {
                                spaceConfigElem = spaceConfigurations[file.LocalFilePath];
                                continue;
                            }
                            if (!File.Exists(file.LocalFilePath))
                            {
                                Console.WriteLine($"Could not find space configuration file {file.LocalFilePath}.");
                                continue;
                            }
                            var contentConfigText = File.ReadAllText(file.LocalFilePath);
                            var contentConfiguration = JsonConvert.DeserializeObject<SpaceConfiguration>(File.ReadAllText(file.LocalFilePath));
                            spaceConfigElem = new SpaceConfigurationElement
                            {
                                SpaceConfiguration = contentConfiguration,
                                Name = $"{req.ProgramName}: {file.FolderId}/{file.FileRefId}"
                            };
                            output.Model.AddElement(spaceConfigElem);
                            spaceConfigurations[file.LocalFilePath] = spaceConfigElem;
                        }
                    }
                    if (catalogWrapper == null)
                    {
                        output.Warnings.Add($"No catalog found for {req.QualifiedProgramName}.");
                    }
                    if (spaceConfigElem == null)
                    {
                        output.Warnings.Add($"No space configuration found for {req.QualifiedProgramName}.");
                    }
                }

                output.Model.AddElement(req.ToElement(catalogWrapper, spaceConfigElem));

            }
            output.Model.AddElement(colorScheme);
            output.TotalProgramArea = sum;
            Elements.Serialization.JSON.JsonInheritanceConverter.ElementwiseSerialization = true;
            var idsToDelete = new List<Guid>();
            foreach (var elem in output.Model.Elements)
            {
                try
                {
                    var json = Newtonsoft.Json.JsonConvert.SerializeObject(elem.Value);
                }
                catch
                {
                    idsToDelete.Add(elem.Key);
                    Console.WriteLine("Failed to serialize element.");
                    output.Errors.Add("Failed to serialize element.");
                    output.Errors.Add(elem.Value.Name);
                    if (elem.Value is SpaceConfigurationElement sce)
                    {
                        var sc = sce.SpaceConfiguration;
                        foreach (var kvp in sc)
                        {
                            output.Errors.Add("processing " + kvp.Key);
                            try
                            {
                                var cbWidth = kvp.Value.Width;
                                var cbDepth = kvp.Value.Depth;
                                output.Errors.Add($"CB Width: {cbWidth}");
                                output.Errors.Add($"CB Depth: {cbDepth}");
                                var json = Newtonsoft.Json.JsonConvert.SerializeObject(kvp.Value);
                            }
                            catch (Exception e)
                            {
                                output.Errors.Add($"Failed to serialize space configuration element {kvp.Key}.");
                                output.Errors.Add(e.Message);
                            }
                        }
                    }
                }
            }
            Elements.Serialization.JSON.JsonInheritanceConverter.ElementwiseSerialization = false;
            foreach (var id in idsToDelete)
            {
                output.Model.Elements.Remove(id);
            }
            return output;
        }

        private static void AddDefaultProgramTypes(List<ProgramRequirements> programRequirements)
        {
            var existingNames = programRequirements.Select(pr => pr.ProgramName).Distinct();
            if (!existingNames.Contains("Circulation"))
            {
                programRequirements.Add(new ProgramRequirements(
                    null,
                    "Circulation",
                    CORRIDOR_MATERIAL_COLOR,
                    0,
                    0,
                    null,
                    null,
                    null,
                    "Circulation",
                    ProgramRequirementsCountType.Area_Total,
                    null,
                    false,
                    ProgramRequirementsDefaultWallType.None
                ));
            }
            if (!existingNames.Contains("Core"))
            {
                programRequirements.Add(new ProgramRequirements(
                    null,
                    "Core",
                    CORE_MATERIAL_COLOR,
                    0,
                    0,
                    null,
                    null,
                    null,
                    "Core",
                    ProgramRequirementsCountType.Area_Total,
                    null,
                    true,
                    ProgramRequirementsDefaultWallType.None
                ));
            }
        }

        private static Color CORRIDOR_MATERIAL_COLOR = new(0.996, 0.965, 0.863, 1.0);
        private static Color CORE_MATERIAL_COLOR = new(0.5f, 0.5f, 0.5f, 1.0f);
    }
}