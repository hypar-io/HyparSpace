using Elements;
using Xunit;
using System.IO;
using System.Collections.Generic;
using Elements.Serialization.glTF;
using Newtonsoft.Json;
using Elements.Components;
using System.Linq;

namespace OpenOfficeLayout.Tests
{
    public class OpenOfficeLayoutTests
    {
        private const string INPUT = "../../../_input/";
        private const string OUTPUT = "../../../_output/";

        [Fact]
        public void OpenOfficeConfigurations()
        {
            // test with one room for each configuration
            var testName = "Configurations";
            var configs = GetConfigurations("OpenOfficeConfigurations.json");

            var (output, spacePlanningModel) = OpenOfficeLayoutTest(testName);
            var elements = output.Model.AllElementsOfType<ElementInstance>();
            var boundaries = spacePlanningModel.AllElementsOfType<SpaceBoundary>().Where(z => z.Name == "Open Office");

            foreach (var boundary in boundaries)
            {
                var depth = boundary.Bounds.XSize;
                var config = configs.FirstOrDefault(c => c.Value.Depth.ApproximatelyEquals(depth, 0.3) && c.Value.Depth < depth).Value;
                Assert.NotNull(config);

                var OffsetedBox = boundary.Bounds.Offset(0.1);
                var boundaryElements = elements.Where(e => OffsetedBox.Contains(e.Transform.Origin)).ToList();
                
                foreach (var contentItem in config.ContentItems)
                {
                    var boundaryElement = boundaryElements.FirstOrDefault(be => be.Name == contentItem.Name);
                    Assert.NotNull(boundaryElement);
                    boundaryElements.Remove(boundaryElement);
                }
            }
        }

        private (OpenOfficeLayoutOutputs output, Model spacePlanningModel) OpenOfficeLayoutTest(string testName)
        {
            var spacePlanningModel = Model.FromJson(System.IO.File.ReadAllText($"{INPUT}/{testName}/Space Planning Zones.json"));
            var levelsModel = Model.FromJson(System.IO.File.ReadAllText($"{INPUT}/{testName}/Levels.json"));
            var input = GetInput(testName);
            var output = OpenOfficeLayout.Execute(
                new Dictionary<string, Model>
                {
                    {"Space Planning Zones", spacePlanningModel},
                    {"Levels", levelsModel}
                }, input);

            System.IO.File.WriteAllText($"{OUTPUT}/{testName}/OpenOfficeLayout.json", output.Model.ToJson());
            output.Model.AddElements(spacePlanningModel.Elements.Values);
            output.Model.AddElements(levelsModel.Elements.Values);
            output.Model.ToGlTF($"{OUTPUT}/{testName}/OpenOfficeLayout.glb");

            return (output, spacePlanningModel);
        }

        private OpenOfficeLayoutInputs GetInput(string testName)
        {
            var json = File.ReadAllText($"{INPUT}/{testName}/inputs.json");
            return Newtonsoft.Json.JsonConvert.DeserializeObject<OpenOfficeLayoutInputs>(json);
        }

        private SpaceConfiguration GetConfigurations(string configsName)
        {
            var dir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            var configJson = File.ReadAllText(Path.Combine(dir, "OpenOfficeConfigurations.json"));
            var configs = JsonConvert.DeserializeObject<SpaceConfiguration>(configJson);
            return configs;
        }
    }
}