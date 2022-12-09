using Elements;
using Elements.Geometry;
using System.Collections.Generic;
using Elements.Components;
using System.Linq;
using System.IO;
using Newtonsoft.Json;
using System;
using Elements.Spatial;
using LayoutFunctionCommon;

namespace LoungeLayout
{
    public static class LoungeLayout
    {
        /// <summary>
        /// The LoungeLayout function.
        /// </summary>
        /// <param name="model">The input model.</param>
        /// <param name="input">The arguments to the execution.</param>
        /// <returns>A LoungeLayoutOutputs instance containing computed results and the model with any new elements.</returns>
        public static LoungeLayoutOutputs Execute(Dictionary<string, Model> inputModels, LoungeLayoutInputs input)
        {
            var output = new LoungeLayoutOutputs();
            LayoutStrategies.StandardLayoutOnAllLevels<LevelElements, LevelVolume, SpaceBoundary, CirculationSegment>("Lounge", inputModels, input.Overrides, output.Model, false, "./LoungeConfigurations.json");

            return output;
        }
    }

}