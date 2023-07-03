using Elements;
using Xunit;
using System.IO;
using System.Collections.Generic;
using Elements.Serialization.glTF;
using System.Linq;
using LayoutFunctionCommon;
using System;
using Elements.Geometry;

namespace OpenOfficeLayout.Tests
{
    public class OpenOfficeLayoutTests
    {
        private const string INPUT = "../../../_input/";
        private const string OUTPUT = "../../../_output/";

        [Fact]
        public void OpenOfficeConfigurations()
        {
            var testName = "Configurations";
            var configs = LayoutStrategies.GetConfigurations("OpenOfficeDeskConfigurations.json");
            var spacePlanningModel = Model.FromJson(System.IO.File.ReadAllText($"{INPUT}/{testName}/Space Planning Zones.json"));
            var levelsModel = Model.FromJson(System.IO.File.ReadAllText($"{INPUT}/{testName}/Levels.json"));
            var circulationModel = Model.FromJson(System.IO.File.ReadAllText($"{INPUT}/{testName}/Circulation.json"));
            var columnsModel = Model.FromJson(System.IO.File.ReadAllText($"{INPUT}/{testName}/Columns.json"));
            var input = GetInput(testName);

            foreach (var columnAvoidanceStrategy in Enum.GetNames(typeof(OpenOfficeLayoutInputsColumnAvoidanceStrategy)))
            {
                var output = OpenOfficeLayoutTest(testName, columnAvoidanceStrategy, spacePlanningModel, levelsModel, circulationModel, columnsModel, input);
                var elements = output.Model.AllElementsAssignableFromType<ElementInstance>().Where(e => e.BaseDefinition is not Column).ToList();
                var openCollabBoundaries = output.Model.AllElementsOfType<SpaceBoundary>().Where(b => b.Name == "Open Collaboration");
                var boundaries = spacePlanningModel.AllElementsOfType<SpaceBoundary>().Where(b => b.Name == "Open Office").OrderBy(b => b.Boundary.Perimeter.Center().Y).ThenBy((b => b.Boundary.Perimeter.Center().X)).ToList();
                var expectedResults = GetTestResults(testName, columnAvoidanceStrategy.Replace("_", ""));

                for (int i = 0; i < boundaries.Count(); i++)
                {
                    var boundary = boundaries[i];
                    var expectedResult = expectedResults[i];
                    var config = configs.FirstOrDefault(c => c.Key == expectedResult.DeskType).Value;
                    Assert.NotNull(config);

                    var offsetedBox = boundary.Bounds.Offset(0.1);
                    offsetedBox.Min -= new Vector3(0, 0, 0.1);
                    var boundaryElements = elements.Where(e => offsetedBox.Contains(e.Transform.Origin)).ToList();

                    // Check items
                    foreach (var contentItem in config.ContentItems)
                    {
                        var suitableElements = boundaryElements.Where(be => be.AdditionalProperties.TryGetValue("gltfLocation", out var gltfLocation) && gltfLocation.ToString() == contentItem.Url).ToList();
                        Assert.True(suitableElements.Count() >= expectedResult.Count);

                        var expectedElements = suitableElements.SkipLast(suitableElements.Count() - expectedResult.Count);
                        boundaryElements.RemoveAll(b => expectedElements.Contains(b));
                    }

                    // Check open collab areas
                    var suitableCollabBoundaries = openCollabBoundaries.Where(b => boundary.Boundary.Perimeter.Contains(b.Boundary.Perimeter.Centroid()));
                    Assert.Equal(suitableCollabBoundaries.Count(), expectedResult.CollabCount);
                    Assert.True(suitableCollabBoundaries.Sum(b => b.Area).ApproximatelyEquals(expectedResult.CollabArea, 0.2));
                }
            }
        }

        private OpenOfficeLayoutOutputs OpenOfficeLayoutTest(
            string testName, 
            string columnAvoidanceStrategyName,
            Model spacePlanningModel,
            Model levelsModel,
            Model circulationModel,
            Model columnsModel,
            OpenOfficeLayoutInputs input)
        {
            Enum.TryParse(columnAvoidanceStrategyName, out OpenOfficeLayoutInputsColumnAvoidanceStrategy columnAvoidanceStrategy);
            input.ColumnAvoidanceStrategy = columnAvoidanceStrategy;

            ElementProxy.ClearCache();
            var output = OpenOfficeLayout.Execute(
                new Dictionary<string, Model>
                {
                    {"Space Planning Zones", spacePlanningModel},
                    {"Levels", levelsModel},
                    {"Circulation", circulationModel},
                    {"Columns", columnsModel},
                }, input);

            System.IO.File.WriteAllText($"{OUTPUT}/{testName}/OpenOfficeLayout_" + columnAvoidanceStrategyName + ".json", output.Model.ToJson());
            output.Model.AddElements(spacePlanningModel.Elements.Values);
            output.Model.AddElements(levelsModel.Elements.Values);
            output.Model.AddElements(circulationModel.Elements.Values);
            output.Model.AddElements(columnsModel.Elements.Values);
            output.Model.ToGlTF($"{OUTPUT}/{testName}/OpenOfficeLayout_" + columnAvoidanceStrategyName + ".glb");
            output.Model.ToGlTF($"{OUTPUT}/{testName}/OpenOfficeLayout_" + columnAvoidanceStrategyName + ".gltf", false);

            return output;
        }

        private OpenOfficeLayoutInputs GetInput(string testName)
        {
            var json = File.ReadAllText($"{INPUT}/{testName}/inputs.json");
            return Newtonsoft.Json.JsonConvert.DeserializeObject<OpenOfficeLayoutInputs>(json);
        }

        private List<TestResult> GetTestResults(string testName, string resultName)
        {
            var json = File.ReadAllText($"{INPUT}/{testName}/{resultName}Results.json");
            return Newtonsoft.Json.JsonConvert.DeserializeObject<TestResults>(json).Results;
        }
    }

    public class TestResult
    {
        public string DeskType { get; set; }
        public int Count { get; set; }
        public double CollabArea { get; set; }
        public int CollabCount { get; set; }
    }

    public class TestResults
    {
        public List<TestResult> Results { get; set; }
    }
}