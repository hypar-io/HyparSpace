using Elements;
using Elements.Geometry;
using System.Collections.Generic;
using System.Linq;

namespace DefineProgramRequirements
{
    public static class DefineProgramRequirements
    {
        /// <summary>
        /// The DefineProgramRequirements function.
        /// </summary>
        /// <param name="model">The input model.</param>
        /// <param name="input">The arguments to the execution.</param>
        /// <returns>A DefineProgramRequirementsOutputs instance containing computed results and the model with any new elements.</returns>
        public static DefineProgramRequirementsOutputs Execute(Dictionary<string, Model> inputModels, DefineProgramRequirementsInputs input)
        {
            var output = new DefineProgramRequirementsOutputs();
            output.Model.AddElements(input.ProgramRequirements);
            if (input.ProgramRequirements.Select(p => p.ProgramName + p.ProgramGroup).Distinct().Count() != input.ProgramRequirements.Count)
            {
                output.Errors.Add("No two programs can have the same Program Name. Please remove one of the duplicates.");
            }
            return output;
        }
    }
}