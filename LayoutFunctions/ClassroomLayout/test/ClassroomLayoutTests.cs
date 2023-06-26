using Elements;
using Xunit;
using System.IO;
using System.Collections.Generic;
using Elements.Serialization.glTF;
using Newtonsoft.Json;
using Elements.Components;
using System.Linq;
using LayoutFunctionCommon;

namespace ClassroomLayout.Tests
{
    public class ClassroomLayoutTests
    {
        private const string INPUT = "../../../_input/";
        private const string OUTPUT = "../../../_output/";
        private string[] orderedKeys = new[] { "Classroom-A", "Classroom-B", "Classroom-C" };

        [Fact]
        public void ClassroomConfigurations()
        {
            // test with one room for each configuration
            var testName = "Configurations";
            var configs = LayoutStrategies.GetConfigurations("ClassroomConfigurations.json");

            var (output, spacePlanningModel) = ClassroomLayoutTest(testName);
            var elements = output.Model.AllElementsOfType<ElementInstance>();
            var boundaries = spacePlanningModel.AllElementsOfType<SpaceBoundary>().Where(z => z.Name == "Classroom").OrderBy(b => b.Bounds.Center().Y).ToList();

            for (int i = 0; i < orderedKeys.Count(); i++)
            {
                var boundary = boundaries[i];
                var config = configs.FirstOrDefault(c => c.Key == orderedKeys[i]).Value;
                Assert.True(config.Depth < boundary.Bounds.XSize);

                var offsetedBox = boundary.Bounds.Offset(0.1);
                var boundaryElements = elements.Where(e => offsetedBox.Contains(e.Transform.Origin)).ToList();

                foreach (var contentItem in config.ContentItems)
                {
                    var boundaryElement = boundaryElements.FirstOrDefault(be => be.Name == contentItem.Name || be.Name == contentItem.Url);
                    Assert.NotNull(boundaryElement);
                    boundaryElements.Remove(boundaryElement);
                }
            }

            // room with 3 desks
            var roomWithDesks = boundaries.Last();
            var offsetedBoxWithDesks = roomWithDesks.Bounds.Offset(0.1);
            var boundaryElementsWithDesks = elements.Where(e => offsetedBoxWithDesks.Contains(e.Transform.Origin)).ToList();
            Assert.Equal(9, boundaryElementsWithDesks.Where(be => be.Name == "Desk").Count());
        }

        private (ClassroomLayoutOutputs output, Model spacePlanningModel) ClassroomLayoutTest(string testName)
        {
            var spacePlanningModel = Model.FromJson(System.IO.File.ReadAllText($"{INPUT}/{testName}/Space Planning Zones.json"));
            var levelsModel = Model.FromJson(System.IO.File.ReadAllText($"{INPUT}/{testName}/Levels.json"));
            var circulationModel = Model.FromJson(System.IO.File.ReadAllText($"{INPUT}/{testName}/Circulation.json"));
            var input = GetInput(testName);
            var output = ClassroomLayout.Execute(
                new Dictionary<string, Model>
                {
                    {"Space Planning Zones", spacePlanningModel},
                    {"Levels", levelsModel},
                    {"Circulation", circulationModel},
                }, input);

            System.IO.File.WriteAllText($"{OUTPUT}/{testName}/ClassroomLayout.json", output.Model.ToJson());
            output.Model.AddElements(spacePlanningModel.Elements.Values);
            output.Model.AddElements(levelsModel.Elements.Values);
            output.Model.AddElements(circulationModel.Elements.Values);
            output.Model.ToGlTF($"{OUTPUT}/{testName}/ClassroomLayout.glb");
            output.Model.ToGlTF($"{OUTPUT}/{testName}/ClassroomLayout.gltf", false);

            return (output, spacePlanningModel);
        }

        private ClassroomLayoutInputs GetInput(string testName)
        {
            var json = File.ReadAllText($"{INPUT}/{testName}/inputs.json");
            return Newtonsoft.Json.JsonConvert.DeserializeObject<ClassroomLayoutInputs>(json);
        }
    }
}