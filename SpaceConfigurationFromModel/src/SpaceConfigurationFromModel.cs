using Elements;
using Elements.Components;
using Elements.Geometry;
using LayoutFunctionCommon;
using static Elements.Components.ContentConfiguration;

namespace SpaceConfigurationFromModel
{
    public static class SpaceConfigurationFromModel
    {
        private class SpaceConfigurationFromModelLayoutGeneration : LayoutGeneration<LevelElements, LevelVolume, SpaceBoundary, CirculationSegment, IOverride, ISpaceSettingsOverrideValue>
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

            var elementInstances = model.AllElementsOfType<ElementInstance>();
            var spaceConfiguration = new SpaceConfiguration();
            foreach (var space in model.AllElementsOfType<SpaceBoundary>())
            {
                var bbox = new BBox3(space.Boundary);
                var boundaryDefinition = new BoundaryDefinition()
                {
                    Min = new Vector3(),
                    Max = new Vector3(bbox.XSize, bbox.YSize)
                };
                var contentItems = CreateContentItems(elementInstances, space, boundaryDefinition);
                var contentConfiguration = new ContentConfiguration
                {
                    ContentItems = contentItems,
                    CellBoundary = boundaryDefinition
                };

                spaceConfiguration.Add(space.Name, contentConfiguration);
            }

            var programName = input.Program ?? "Open Office";
            var layoutGeneration = new SpaceConfigurationFromModelLayoutGeneration(spaceConfiguration);
            var result = layoutGeneration.StandardLayoutOnAllLevels(programName, inputModels, null, false, null, input.ModelFile.LocalFilePath);
            output.Model = result.OutputModel;
            return output;
        }

        private static List<ContentItem> CreateContentItems(IEnumerable<ElementInstance> elementInstances, SpaceBoundary space, BoundaryDefinition boundaryDefinition)
        {
            var bbox = new BBox3(space.Boundary);
            var t = new Transform(bbox.Min).Inverted();
            var contentItems = new List<ContentItem>();
            var spaceElementInstances = elementInstances.Where(ei => space.Boundary.Contains(ei.Transform.Origin));
            foreach (var elementInstance in spaceElementInstances)
            {
                var contentItem = new ContentItem()
                {
                    Anchor = new Vector3(boundaryDefinition.Width / 2, boundaryDefinition.Depth / 2),
                    Url = (elementInstance.BaseDefinition as ContentElement)?.GltfLocation,
                    Name = elementInstance.Name,
                    Transform = elementInstance.Transform.Concatenated(t)
                };
                contentItems.Add(contentItem);
            }

            return contentItems;
        }
    }
}