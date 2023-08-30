using Elements;
using Xunit;
using System.IO;
using System.Collections.Generic;
using Elements.Serialization.glTF;
using Newtonsoft.Json;
using Elements.Components;
using System.Linq;
using LayoutFunctionCommon;

namespace PhoneBoothLayout.Tests
{
    public class PhoneBoothLayoutTests
    {
        private const string INPUT = "../../../_input/";
        private const string OUTPUT = "../../../_output/";
        private string[] orderedKeys = new[] { "Configuration B" };

        [Fact]
        public void PhoneBoothConfigurations()
        {
            // Test with one room for each configuration to check that every piece of expected content exists in a room of a matching size
            var testName = "Configurations";
            var configs = LayoutStrategies.GetConfigurations("PhoneBoothConfigurations.json");

            // Get the result of PhoneBoothLayout.Execute()
            var (output, spacePlanningModel) = PhoneBoothLayoutTest(testName);
            var elements = output.Model.AllElementsOfType<ElementInstance>();

            // Get the rooms created according to the orderedKeys order 
            var boundaries = spacePlanningModel.AllElementsOfType<SpaceBoundary>().Where(z => z.Name == "Phone Booth").OrderBy(b => b.Boundary.Perimeter.Center().Y).ToList();

            // Check each configuration separately
            for (int i = 0; i < orderedKeys.Count(); i++)
            {
                // Confirm that the size of the room covers the size of the corresponding configuration
                var boundary = boundaries[i];
                var config = configs.FirstOrDefault(c => c.Key == orderedKeys[i]).Value;
                Assert.True(config.Width < boundary.Bounds.YSize);

                // Look for all the furniture placed within the room
                var offsetedBox = boundary.Bounds.Offset(0.1);
                var boundaryElements = elements.Where(e => offsetedBox.Contains(e.Transform.Origin)).ToList();
                
                // Check that the room has all furniture that are in the appropriate configuration
                foreach (var contentItem in config.ContentItems)
                {
                    var boundaryElement = boundaryElements.FirstOrDefault(be => be.Name == contentItem.Name);
                    Assert.NotNull(boundaryElement);
                    boundaryElements.Remove(boundaryElement);
                }
            }
        }

        private (PhoneBoothLayoutOutputs output, Model spacePlanningModel) PhoneBoothLayoutTest(string testName)
        {
            var spacePlanningModel = Model.FromJson(System.IO.File.ReadAllText($"{INPUT}/{testName}/Space Planning Zones.json"));
            var levelsModel = Model.FromJson(System.IO.File.ReadAllText($"{INPUT}/{testName}/Levels.json"));
            var circulationModel = Model.FromJson(System.IO.File.ReadAllText($"{INPUT}/{testName}/Circulation.json"));
            var input = GetInput(testName);
            var output = PhoneBoothLayout.Execute(
                new Dictionary<string, Model>
                {
                    {"Space Planning Zones", spacePlanningModel},
                    {"Levels", levelsModel},
                    {"Circulation", circulationModel},
                }, input);

            System.IO.File.WriteAllText($"{OUTPUT}/{testName}/PhoneBoothLayout.json", output.Model.ToJson());
            output.Model.AddElements(spacePlanningModel.Elements.Values);
            output.Model.AddElements(levelsModel.Elements.Values);
            output.Model.AddElements(circulationModel.Elements.Values);
            output.Model.ToGlTF($"{OUTPUT}/{testName}/PhoneBoothLayout.glb");
            output.Model.ToGlTF($"{OUTPUT}/{testName}/PhoneBoothLayout.gltf", false);

            return (output, spacePlanningModel);
        }

        private PhoneBoothLayoutInputs GetInput(string testName)
        {
            var json = File.ReadAllText($"{INPUT}/{testName}/inputs.json");
            return Newtonsoft.Json.JsonConvert.DeserializeObject<PhoneBoothLayoutInputs>(json);
        }
    }
}