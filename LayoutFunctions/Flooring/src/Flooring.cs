using Elements;
using Elements.Geometry;
using System.Collections.Generic;

namespace Flooring
{
  public static class Flooring
  {
    /// <summary>
    /// The Flooring function.
    /// </summary>
    /// <param name="model">The input model.</param>
    /// <param name="input">The arguments to the execution.</param>
    /// <returns>A FlooringOutputs instance containing computed results and the model with any new elements.</returns>
    public static FlooringOutputs Execute(Dictionary<string, Model> inputModels, FlooringInputs input)
    {
      var output = new FlooringOutputs();


      var defaultFlooringTypes = new List<FlooringType>
      {
        FlooringType.Terazzo,
        FlooringType.Wood,
        FlooringType.LightWood,
        FlooringType.Laminate,
        FlooringType.Tile,
        FlooringType.WoodParquet,
        FlooringType.Carpet,
        FlooringType.Vinyl,
      };
      foreach (var t in defaultFlooringTypes)
      {
        t.AddId = t.Name + "-default";
      }
      var flooringTypes = FlooringType.CreateElements(input.Overrides, defaultFlooringTypes);
      output.Model.AddElements(flooringTypes);

      var defaultFlooringRegions = new List<FlooringRegion>();
      if (inputModels.TryGetValue("Floors", out var floorsModel))
      {
        foreach (var floor in floorsModel.AllElementsOfType<Floor>())
        {
          defaultFlooringRegions.Add(new FlooringRegion(floor, defaultFlooringTypes[0]));
        }
      }

      var flooringRegionElements = FlooringRegion.CreateElements(input.Overrides, flooringTypes, defaultFlooringRegions);
      output.Model.AddElements(flooringRegionElements);

      return output;
    }
  }
}