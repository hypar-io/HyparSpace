
// This code was generated by Hypar.
// Edits to this code will be overwritten the next time you run 'hypar test generate'.
// DO NOT EDIT THIS FILE.

using Elements;
using Xunit;
using System.IO;
using System.Collections.Generic;
using Elements.Serialization.glTF;

namespace SpacePlanningZones
{
    public class TestCase_4823e9
    {
        [Fact]
        public void TestExecute()
        {
            var input = GetInput();

            var modelDependencies = new Dictionary<string, Model> {
                {"Levels", Model.FromJson(File.ReadAllText(@"/Users/andrewheumann/Dev/HyparSpace/ZonePlanningFunctions/SpacePlanningZones/test/Generated/TestCase_4823e9/model_dependencies/Levels/model.json")) },
                {"Floors", Model.FromJson(File.ReadAllText(@"/Users/andrewheumann/Dev/HyparSpace/ZonePlanningFunctions/SpacePlanningZones/test/Generated/TestCase_4823e9/model_dependencies/Floors/model.json")) },
                {"Core", Model.FromJson(File.ReadAllText(@"/Users/andrewheumann/Dev/HyparSpace/ZonePlanningFunctions/SpacePlanningZones/test/Generated/TestCase_4823e9/model_dependencies/Core/model.json")) },
            };

            var result = SpacePlanningZones.Execute(modelDependencies, input);
            result.Model.AddCurvesForSpaceBoundaries();
            result.Model.ToGlTF("../../../Generated/results/TestCase_4823e9.glb");
            File.WriteAllText("../../../Generated/results/TestCase_4823e9.json", result.Model.ToJson());
        }

        public SpacePlanningZonesInputs GetInput()
        {
            var inputText = @"
            {
  ""Default Program Assignment"": ""unspecified"",
  ""Circulation Mode"": ""Automatic"",
  ""Add Corridors"": {
    ""SplitLocations"": []
  },
  ""Depth at Ends"": 1,
  ""Split Zones"": {
    ""SplitLocations"": [
      {
        ""position"": {
          ""X"": 6.771535439012386,
          ""Y"": 29.881993182866502,
          ""Z"": 0
        },
        ""direction"": {
          ""X"": -1,
          ""Y"": 0,
          ""Z"": 0
        }
      },
      {
        ""position"": {
          ""X"": 15.002471454464526,
          ""Y"": 25.65650824129023,
          ""Z"": 0
        },
        ""direction"": {
          ""X"": 0,
          ""Y"": 1,
          ""Z"": 0
        }
      },
      {
        ""position"": {
          ""X"": 8.959467276130958,
          ""Y"": 11.020051317302368,
          ""Z"": 0
        },
        ""direction"": {
          ""X"": -0.5534839130427918,
          ""Y"": 0.8328598669661297,
          ""Z"": 0
        }
      },
      {
        ""position"": {
          ""X"": 20.834165822451084,
          ""Y"": -5.486007711070048,
          ""Z"": 0
        },
        ""direction"": {
          ""X"": -0.5534853643970721,
          ""Y"": 0.8328589024548158,
          ""Z"": 0
        }
      },
      {
        ""position"": {
          ""X"": 35.27190200728005,
          ""Y"": -1.728370268016203,
          ""Z"": 0
        },
        ""direction"": {
          ""X"": 0.9058085258377097,
          ""Y"": -0.42368728387776167,
          ""Z"": 0
        }
      },
      {
        ""position"": {
          ""X"": 23.263007988929257,
          ""Y"": -13.643081760044051,
          ""Z"": 0
        },
        ""direction"": {
          ""X"": -0.5534850544118961,
          ""Y"": 0.8328591084587239,
          ""Z"": 0
        }
      },
      {
        ""position"": {
          ""X"": 2.771467306710907,
          ""Y"": -14.859814317033678,
          ""Z"": 0
        },
        ""direction"": {
          ""X"": 0,
          ""Y"": -1,
          ""Z"": 0
        }
      },
      {
        ""position"": {
          ""X"": -4.2913744238788425,
          ""Y"": -2.4373286098116544,
          ""Z"": 0
        },
        ""direction"": {
          ""X"": 1,
          ""Y"": 1.2622690719252439E-15,
          ""Z"": 0
        }
      },
      {
        ""position"": {
          ""X"": 5.882457538829939,
          ""Y"": 45.16097545233031,
          ""Z"": 0
        },
        ""direction"": {
          ""X"": -1,
          ""Y"": 0,
          ""Z"": 0
        }
      },
      {
        ""position"": {
          ""X"": 16.036806753105036,
          ""Y"": 45.90323750001731,
          ""Z"": 0
        },
        ""direction"": {
          ""X"": -0.9999999999999999,
          ""Y"": 0,
          ""Z"": 0
        }
      },
      {
        ""position"": {
          ""X"": 14.514012287652353,
          ""Y"": 37.252627821789154,
          ""Z"": 0
        },
        ""direction"": {
          ""X"": 1,
          ""Y"": 0,
          ""Z"": 0
        }
      },
      {
        ""position"": {
          ""X"": 5.784722195967696,
          ""Y"": 62.067561663719445,
          ""Z"": 0
        },
        ""direction"": {
          ""X"": 0,
          ""Y"": -0.9999999999999999,
          ""Z"": 0
        }
      },
      {
        ""position"": {
          ""X"": -3.779350268508111,
          ""Y"": 40.55700162489988,
          ""Z"": 0
        },
        ""direction"": {
          ""X"": 1,
          ""Y"": 0,
          ""Z"": 0
        }
      },
      {
        ""position"": {
          ""X"": -4.051289939420506,
          ""Y"": 24.54565838334165,
          ""Z"": 0
        },
        ""direction"": {
          ""X"": 1,
          ""Y"": 1.3770208057366246E-15,
          ""Z"": 0
        }
      },
      {
        ""position"": {
          ""X"": 11.243571875761798,
          ""Y"": 15.901864532763511,
          ""Z"": 0
        },
        ""direction"": {
          ""X"": 0.83285986696613,
          ""Y"": 0.5534839130427914,
          ""Z"": 0
        }
      },
      {
        ""position"": {
          ""X"": 21.621776224020493,
          ""Y"": 20.783591409172722,
          ""Z"": 0
        },
        ""direction"": {
          ""X"": 0,
          ""Y"": -1,
          ""Z"": 0
        }
      }
    ]
  },
  ""Corridors"": [],
  ""Corridor Width"": 1.5,
  ""Outer Band Depth"": 15.249999999999995,
  ""Manual Split Locations"": [],
  ""model_input_keys"": {
    ""Levels"": ""c8fa343f-fdf5-4433-917a-392737d8aebb_61dbb9f8-aaae-4295-9112-c8ae81655361_elements.zip"",
    ""Floors"": ""3f62e268-c43e-4a4f-a53b-bd47c0879c26_31ec3b95-5062-47b9-a1e0-e3550bf7e2d1_elements.zip"",
    ""Core"": ""fbdc0794-12fc-4cfd-95c5-f638a39fb004_a9cac5a1-f68d-4d2e-bfdd-0d204359bbe4_elements.zip""
  },
  ""Additional Corridor Locations"": []
}
            ";
            return Newtonsoft.Json.JsonConvert.DeserializeObject<SpacePlanningZonesInputs>(inputText);
        }
    }
}