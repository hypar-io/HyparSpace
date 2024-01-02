using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Elements;
using Newtonsoft.Json;

namespace LayoutFunctionCommon
{
    public static class ContentManagement
    {
        public static bool LoadConfigAndCatalog<TProgramRequirement>(
            Dictionary<string, Model> inputModels,
            string programName,
            out string configPath,
            out string catalogPath,
            Guid? programId = null
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

        private static (string catalogPath, string configPath) WriteLayoutConfigs(IProgramRequirement req, Model programReqModel)
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
    }
}