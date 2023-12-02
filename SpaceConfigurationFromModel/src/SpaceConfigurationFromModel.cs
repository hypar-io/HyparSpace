using Elements;
using Elements.Components;
using Elements.Geometry;
using LayoutFunctionCommon;

namespace SpaceConfigurationFromModel
{
    public static class SpaceConfigurationFromModel
    {
        private class SpaceConfigurationFromModelLayoutGeneration : LayoutGeneration<LevelElements, LevelVolume, SpaceBoundary, CirculationSegment>
        {
            private readonly SpaceConfiguration spaceConfig;

            internal SpaceConfigurationFromModelLayoutGeneration(SpaceConfiguration spaceConfig)
            {
                this.spaceConfig = spaceConfig;
            }

            protected override SpaceConfiguration DeserializeConfigJson(string configJson)
            {
                return spaceConfig;
            }
        }

        /// <summary>
        /// The SpaceConfigurationFromModel function.
        /// </summary>
        /// <param name="model">The input model.</param>
        /// <param name="input">The arguments to the execution.</param>
        /// <returns>A SpaceConfigurationFromModelOutputs instance containing computed results and the model with any new elements.</returns>
        public static SpaceConfigurationFromModelOutputs Execute(Dictionary<string, Model> inputModels, SpaceConfigurationFromModelInputs input)
        {
            Elements.Serialization.glTF.GltfExtensions.UseReferencedContentExtension = true;
            var output = new SpaceConfigurationFromModelOutputs();
            if (string.IsNullOrWhiteSpace(input?.ModelFile?.LocalFilePath))
            {
                return output;
            }
            Model model;
            try
            {
                var json = File.ReadAllText(input.ModelFile.LocalFilePath);
                model = Model.FromJson(json, out var modelLoadErrors);
            }
            catch
            {
                output.Warnings.Add("Can't load model from input file.");
                return output;
            }

            var spaceConfiguration = SpaceConfigurationCreator.CreateSpaceConfigurationFromModel(model);

            var programName = input.Program ?? "Open Office";
            var layoutGeneration = new SpaceConfigurationFromModelLayoutGeneration(spaceConfiguration);
            var result = layoutGeneration.StandardLayoutOnAllLevels(programName, inputModels, null, false, null, input.ModelFile.LocalFilePath);
            output.Model = result.OutputModel;
            return output;
        }
    }
}