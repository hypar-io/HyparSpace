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
            var testName = "Configurations";
            var configs = LayoutStrategies.GetConfigurations("OpenCollaborationConfigurations.json");
            var levelsModel = Model.FromJson(System.IO.File.ReadAllText($"{INPUT}/{testName}/Levels.json"));
            var circulationModel = Model.FromJson(System.IO.File.ReadAllText($"{INPUT}/{testName}/Circulation.json"));
            var input = GetInput(testName);

            foreach (var config in configs)
            {
                var (output, spacePlanningModel) = OpenCollaborationLayoutTest(testName, config.Key, levelsModel, circulationModel, input);
                var elements = output.Model.AllElementsOfType<ElementInstance>();
                var boundary = spacePlanningModel.AllElementsOfType<SpaceBoundary>().Where(z => z.Name == "Open Collaboration").OrderBy(b => b.Boundary.Perimeter.Center().Y).First();
                
                Assert.True(config.Value.Depth < boundary.Bounds.XSize && config.Value.Width < boundary.Bounds.YSize);

                var offsetedBox = boundary.Bounds.Offset(0.1);
                var boundaryElements = elements.Where(e => offsetedBox.Contains(e.Transform.Origin)).ToList();

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