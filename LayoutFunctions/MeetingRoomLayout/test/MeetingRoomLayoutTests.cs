using Elements;
using Xunit;
using System.IO;
using System.Collections.Generic;
using Elements.Serialization.glTF;
using Newtonsoft.Json;
using Elements.Components;
using System.Linq;
using Elements.Geometry;

namespace MeetingRoomLayout.Tests
{
    public class MeetingRoomLayoutTests
    {
        private const string INPUT = "../../../_input/";
        private const string OUTPUT = "../../../_output/";

        [Fact]
        public void MeetingRoomConfigurations()
        {
            // test with one room for each configuration
            var testName = "Configurations";
            var configs = GetConfigurations("MeetingRoomConfigurations.json");

            var (output, spacePlanningModel) = MeetingRoomLayoutTest(testName);
            var elements = output.Model.AllElementsOfType<ElementInstance>();
            var boundaries = spacePlanningModel.AllElementsOfType<SpaceBoundary>().Where(z => z.Name == "Meeting Room");

            foreach (var boundary in boundaries)
            {
                var depth = boundary.Bounds.XSize;
                var width = boundary.Bounds.YSize;
                var configName = MeetingRoomLayout.OrderedKeys.FirstOrDefault(c => configs[c].Width < width && configs[c].Depth < depth);
                var config = configs[MeetingRoomLayout.OrderedKeys.FirstOrDefault(c => configs[c].Width < width && configs[c].Depth < depth)];
                Assert.NotNull(config);

                var offsetedBox = boundary.Bounds.Offset(0.1);
                offsetedBox.Extend(new Vector3(offsetedBox.Min.X, offsetedBox.Min.Y, offsetedBox.Min.Z - 1));
                var boundaryElements = elements.Where(e => offsetedBox.Contains(e.Transform.Origin)).ToList();
                
                foreach (var contentItem in config.ContentItems)
                {
                    var boundaryElement = boundaryElements.FirstOrDefault(be => be.Name == contentItem.Url);
                    Assert.NotNull(boundaryElement);
                    boundaryElements.Remove(boundaryElement);
                }
            }
        }

        private (MeetingRoomLayoutOutputs output, Model spacePlanningModel) MeetingRoomLayoutTest(string testName)
        {
            var spacePlanningModel = Model.FromJson(System.IO.File.ReadAllText($"{INPUT}/{testName}/Space Planning Zones.json"));
            var levelsModel = Model.FromJson(System.IO.File.ReadAllText($"{INPUT}/{testName}/Levels.json"));
            var input = GetInput(testName);
            var output = MeetingRoomLayout.Execute(
                new Dictionary<string, Model>
                {
                    {"Space Planning Zones", spacePlanningModel},
                    {"Levels", levelsModel}
                }, input);

            System.IO.File.WriteAllText($"{OUTPUT}/{testName}/MeetingRoomLayout.json", output.Model.ToJson());
            output.Model.AddElements(spacePlanningModel.Elements.Values);
            output.Model.AddElements(levelsModel.Elements.Values);
            output.Model.ToGlTF($"{OUTPUT}/{testName}/MeetingRoomLayout.glb");

            return (output, spacePlanningModel);
        }

        private MeetingRoomLayoutInputs GetInput(string testName)
        {
            var json = File.ReadAllText($"{INPUT}/{testName}/inputs.json");
            return Newtonsoft.Json.JsonConvert.DeserializeObject<MeetingRoomLayoutInputs>(json);
        }

        private SpaceConfiguration GetConfigurations(string configsName)
        {
            var dir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            var configJson = File.ReadAllText(Path.Combine(dir, "ConferenceRoomConfigurations.json"));
            var configs = JsonConvert.DeserializeObject<SpaceConfiguration>(configJson);
            return configs;
        }
    }
}