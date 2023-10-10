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
            var output = new DefineProgramRequirementsOutputs();
            if (input.ProgramRequirements.Select(p => p.ProgramName + p.ProgramGroup).Distinct().Count() != input.ProgramRequirements.Count)
            {
                output.Errors.Add("No two programs can have the same Program Name. Please remove one of the duplicates.");
            }
            foreach (var pr in input.ProgramRequirements)
            {
                pr.AdditionalProperties.Clear();
            }
            var sum = input.ProgramRequirements.Sum(p => p.AreaPerSpace * p.SpaceCount);
            // output.Model.AddElements(input.ProgramRequirements);
            var colorScheme = ColorScheme.ProgramColors;

            Dictionary<string, CatalogWrapper> wrappers = new Dictionary<string, CatalogWrapper>();
            Dictionary<string, SpaceConfigurationElement> spaceConfigurations = new Dictionary<string, SpaceConfigurationElement>();

            foreach (var req in input.ProgramRequirements)
            {
                colorScheme.Mapping[req.QualifiedProgramName] = req.Color;
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
                            var contentConfiguration = JsonConvert.DeserializeObject<SpaceConfiguration>(File.ReadAllText(file.LocalFilePath));
                            spaceConfigElem = new SpaceConfigurationElement
                            {
                                SpaceConfiguration = contentConfiguration
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
            return output;
        }
    }
}