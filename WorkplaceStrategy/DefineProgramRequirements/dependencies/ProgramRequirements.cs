using Elements.Geometry;
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
            Color = this.Color ?? Colors.Magenta,
            SpaceCount = this.SpaceCount,
            AreaPerSpace = this.Dimensions?.Area ?? this.AreaPerSpace,
            Dimensions = this.Dimensions,
            Width = this.Dimensions?.Width ?? this.Width,
            Depth = this.Dimensions?.Depth ?? this.Depth,
            HyparSpaceType = this.HyparSpaceType ?? this.QualifiedProgramName,
            CountType = (Elements.ProgramRequirementCountType)this.CountType,
            Catalog = catalogWrapper?.Id,
            Enclosed = this.Enclosed ?? false,
            SpaceConfig = spaceConfigElem?.Id
        };
    }

}
