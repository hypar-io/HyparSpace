using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Elements;
using Elements.Components;
using Newtonsoft.Json;

namespace LayoutFunctionCommon
{
    public static class ContentManagement
    {
        public static bool LoadConfigAndCatalog<TProgramRequirement>(
            Dictionary<string, Model> inputModels,
            string programName,
            out string configPath,
            out string catalogPath
            ) where TProgramRequirement : Element, IProgramRequirement
        {
            configPath = null;
            catalogPath = null;
            if (!inputModels.TryGetValue("Program Requirements", out var programReqModel))
            {
                Console.WriteLine("Program Requirements model not found");
                return false;
            }
            var programReqs = programReqModel.AllElementsAssignableFromType<TProgramRequirement>();
            var req = programReqs?.FirstOrDefault(r => r.HyparSpaceType == programName);
            if (req == null)
            {
                Console.WriteLine($"No Program Requirement found for {programName}.");
                return false;
            }
            var (catPath, confPath) = WriteLayoutConfigs(req, programReqModel);
            if (catPath == null || confPath == null)
            {
                Console.WriteLine($"No Space information for {programName}.");
                return false;
            }
            configPath = confPath;
            catalogPath = catPath;
            return true;
        }

        public static (string catalogPath, string configPath) WriteLayoutConfigs(IProgramRequirement req, Model programReqModel)
        {
            if (!req.SpaceConfig.HasValue || !req.Catalog.HasValue)
            {
                return (null, null);
            }

            var tempDir = Path.GetTempPath();
            var catalogPath = Path.Combine(tempDir, $"{req.Id}_catalog.json");
            var configPath = Path.Combine(tempDir, $"{req.Id}_config.json");
            if (File.Exists(catalogPath) && File.Exists(configPath))
            {
                return (catalogPath, configPath);
            }

            var catalogWrapper = programReqModel.GetElementOfType<CatalogWrapper>(req.Catalog.Value);
            var catalogStringBase64 = catalogWrapper.CatalogString;
            var bytes = Convert.FromBase64String(catalogStringBase64);
            if (bytes != null)
            {
                File.WriteAllBytes(catalogPath, bytes);
            }
            var spaceConfig = programReqModel.GetElementOfType<SpaceConfigurationElement>(req.SpaceConfig.Value);
            var config = spaceConfig.SpaceConfiguration;
            File.WriteAllText(configPath, JsonConvert.SerializeObject(config));
            return (catalogPath, configPath);
        }

        public static SpaceConfiguration GetSpaceConfiguration<TProgramRequirement>(Dictionary<string, Model> inputModels, string configJsonPath, string programName) where TProgramRequirement : Element, IProgramRequirement
        {
            if (LoadConfigAndCatalog<TProgramRequirement>(inputModels, programName, out var configPath, out var catPath))
            {
                configJsonPath = configPath;
                ContentCatalogRetrieval.SetCatalogFilePath(catPath);
            }

            var configJson = File.ReadAllText(configJsonPath);
            var configs = JsonConvert.DeserializeObject<SpaceConfiguration>(configJson);
            return configs;
        }
    }
}