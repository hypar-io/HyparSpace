using Newtonsoft.Json;
using System;
namespace DefineProgramRequirements;

public partial class ProgramRequirements
{
    public string QualifiedProgramName => String.IsNullOrEmpty(this.ProgramGroup) ? this.ProgramName : $"{this.ProgramGroup} - {this.ProgramName}";

    public Elements.ProgramRequirement ToElement(Elements.CatalogWrapper catalogWrapper, Elements.SpaceConfigurationElement spaceConfigElem)
    {
        return new Elements.ProgramRequirement
        {
            ProgramGroup = this.ProgramGroup,
            ProgramName = this.ProgramName,
            Color = this.Color,
            AreaPerSpace = this.AreaPerSpace,
            SpaceCount = this.SpaceCount,
            Width = this.Width,
            Depth = this.Depth,
            HyparSpaceType = this.QualifiedProgramName,
            CountType = (Elements.ProgramRequirementCountType)this.CountType,
            Catalog = catalogWrapper?.Id,
            Enclosed = this.Enclosed ?? false,
            SpaceConfig = spaceConfigElem?.Id
        };
    }

}
