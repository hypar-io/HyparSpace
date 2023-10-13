using Elements;
using Xunit;
using System.IO;
using System.Collections.Generic;
using Elements.Serialization.glTF;
using Newtonsoft.Json;
using Elements.Components;
using System.Linq;
using LayoutFunctionCommon;

namespace OpenCollaborationLayout.Tests
{
    public class OpenCollaborationLayoutTests
    {
        private const string INPUT = "../../../_input/";
        private const string OUTPUT = "../../../_output/";

        [Fact]
        public void OpenCollaborationConfigurations()
        {
            // Test with a separate "Space Planning Zones" model for each configuration
            // to check that every piece of expected content exists in a room of a matching size
            var testName = "Configurations";
            var configs = LayoutStrategies.GetConfigurations("OpenCollaborationConfigurations.json");
            var levelsModel = Model.FromJson(System.IO.File.ReadAllText($"{INPUT}/{testName}/Levels.json"));
            var circulationModel = Model.FromJson(System.IO.File.ReadAllText($"{INPUT}/{testName}/Circulation.json"));
            var input = GetInput(testName);

            // Check each configuration separately due to the impossibility of predicting a specific order of configurations for multiple rooms in the same building
            foreach (var config in configs)
            {
                // Get the result of OpenCollaborationLayout.Execute()
                var (output, spacePlanningModel) = OpenCollaborationLayoutTest(testName, config.Key, levelsModel, circulationModel, input);
                var elements = output.Model.AllElementsOfType<ElementInstance>();

                // Confirm that the size of the room covers the size of the corresponding configuration
                var boundary = spacePlanningModel.AllElementsOfType<SpaceBoundary>().Where(z => z.Name == "Open Collaboration").OrderBy(b => b.Boundary.Perimeter.Center().Y).First();
                Assert.True(config.Value.Depth < boundary.Bounds.XSize && config.Value.Width < boundary.Bounds.YSize);

                // Look for all the furniture placed within the room
                var offsetedBox = boundary.Bounds.Offset(0.1);
                var boundaryElements = elements.Where(e => offsetedBox.Contains(e.Transform.Origin)).ToList();

                // Check that the room has all furniture that are in the appropriate configuration
                foreach (var contentItem in config.Value.ContentItems)
                {
                    var boundaryElement = boundaryElements.FirstOrDefault(be => be.AdditionalProperties.TryGetValue("gltfLocation", out var gltfLocation) && gltfLocation.ToString() == contentItem.Url);
                    Assert.NotNull(boundaryElement);
                    boundaryElements.Remove(boundaryElement);
                }
            }
        }

        private (OpenCollaborationLayoutOutputs output, Model spacePlanningModel) OpenCollaborationLayoutTest(
            string testName,
            string configName,
            Model levelsModel,
            Model circulationModel,
            OpenCollaborationLayoutInputs input)
        {
            ElementProxy.ClearCache();

            var spacePlanningModel = Model.FromJson(System.IO.File.ReadAllText($"{INPUT}/{testName}/Space Planning Zones_" + configName + ".json"));
            var output = OpenCollaborationLayout.Execute(
                new Dictionary<string, Model>
                {
                    {"Space Planning Zones", spacePlanningModel},
                    {"Levels", levelsModel},
                    {"Circulation", circulationModel},
                }, input);

            System.IO.File.WriteAllText($"{OUTPUT}/{testName}/OpenCollaborationLayout_" + configName + ".json", output.Model.ToJson());
            output.Model.AddElements(spacePlanningModel.Elements.Values);
            output.Model.AddElements(levelsModel.Elements.Values);
            output.Model.AddElements(circulationModel.Elements.Values);
            output.Model.ToGlTF($"{OUTPUT}/{testName}/OpenCollaborationLayout_" + configName + ".glb");
            output.Model.ToGlTF($"{OUTPUT}/{testName}/OpenCollaborationLayout_" + configName + ".gltf", false);

            return (output, spacePlanningModel);
        }

        private OpenCollaborationLayoutInputs GetInput(string testName)
        {
            var json = File.ReadAllText($"{INPUT}/{testName}/inputs.json");
            return Newtonsoft.Json.JsonConvert.DeserializeObject<OpenCollaborationLayoutInputs>(json);
        }
    }
}