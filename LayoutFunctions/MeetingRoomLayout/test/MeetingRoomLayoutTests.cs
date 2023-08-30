using Elements;
using Xunit;
using System.IO;
using System.Collections.Generic;
using Elements.Serialization.glTF;
using System.Linq;
using Elements.Geometry;
using LayoutFunctionCommon;

namespace MeetingRoomLayout.Tests
{
    public class MeetingRoomLayoutTests
    {
        private const string INPUT = "../../../_input/";
        private const string OUTPUT = "../../../_output/";
        private string[] orderedKeys = new[] { "22P", "20P", "14P", "6P-B", "8P", "6P-A", "4P-A", "4P-B", "13P" };

        [Fact]
        public void MeetingRoomConfigurations()
        {
            // Test with one room for each configuration to check that every piece of expected content exists in a room of a matching size
            var testName = "Configurations";
            var configs = LayoutStrategies.GetConfigurations("ConferenceRoomConfigurations.json");

            // Get the result of MeetingRoomLayout.Execute()
            var (output, spacePlanningModel) = MeetingRoomLayoutTest(testName);
            var elements = output.Model.AllElementsOfType<ElementInstance>();

            // Get the rooms created according to the orderedKeys order 
            var boundaries = spacePlanningModel.AllElementsOfType<SpaceBoundary>().Where(z => z.Name == "Meeting Room").OrderBy(b => b.Boundary.Perimeter.Center().Y).ToList();

            // Check each configuration separately
            for (int i = 0; i < orderedKeys.Count(); i++)
            {
                // Confirm that the size of the room covers the size of the corresponding configuration
                var boundary = boundaries[i];
                var config = configs.FirstOrDefault(c => c.Key == orderedKeys[i]).Value;
                Assert.True(config.Width < boundary.Bounds.YSize);

                // Look for all the furniture placed within the room
                var offsetedBox = boundary.Bounds.Offset(0.1);
                offsetedBox.Extend(new Vector3(offsetedBox.Min.X, offsetedBox.Min.Y, offsetedBox.Min.Z - 1));
                var boundaryElements = elements.Where(e => offsetedBox.Contains(e.Transform.Origin)).ToList();
                
                // Check that the room has all furniture that are in the appropriate configuration
                foreach (var contentItem in config.ContentItems)
                {
                    var boundaryElement = boundaryElements.FirstOrDefault(be => be.Name == contentItem.Name || be.Name == contentItem.Url);
                    Assert.NotNull(boundaryElement);
                    boundaryElements.Remove(boundaryElement);
                }
            }
        }

        private (MeetingRoomLayoutOutputs output, Model spacePlanningModel) MeetingRoomLayoutTest(string testName)
        {
            var spacePlanningModel = Model.FromJson(System.IO.File.ReadAllText($"{INPUT}/{testName}/Space Planning Zones.json"));
            var levelsModel = Model.FromJson(System.IO.File.ReadAllText($"{INPUT}/{testName}/Levels.json"));
            var circulationModel = Model.FromJson(System.IO.File.ReadAllText($"{INPUT}/{testName}/Circulation.json"));
            var input = GetInput(testName);
            var output = MeetingRoomLayout.Execute(
                new Dictionary<string, Model>
                {
                    {"Space Planning Zones", spacePlanningModel},
                    {"Levels", levelsModel},
                    {"Circulation", circulationModel}
                }, input);

            System.IO.File.WriteAllText($"{OUTPUT}/{testName}/MeetingRoomLayout.json", output.Model.ToJson());
            output.Model.AddElements(spacePlanningModel.Elements.Values);
            output.Model.AddElements(levelsModel.Elements.Values);
            output.Model.AddElements(circulationModel.Elements.Values);
            output.Model.ToGlTF($"{OUTPUT}/{testName}/MeetingRoomLayout.glb");
            output.Model.ToGlTF($"{OUTPUT}/{testName}/MeetingRoomLayout.gltf", false);

            return (output, spacePlanningModel);
        }

        private MeetingRoomLayoutInputs GetInput(string testName)
        {
            var json = File.ReadAllText($"{INPUT}/{testName}/inputs.json");
            return Newtonsoft.Json.JsonConvert.DeserializeObject<MeetingRoomLayoutInputs>(json);
        }
    }
}