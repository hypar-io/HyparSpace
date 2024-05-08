using Elements.Geometry;
using Newtonsoft.Json;
using System;
using System.Collections;
namespace DefineProgramRequirements;

public partial class ProgramRequirements
{
    public string QualifiedProgramName => String.IsNullOrEmpty(this.ProgramGroup) ? this.ProgramName : $"{this.ProgramGroup} - {this.ProgramName}";

    public Guid? Id { get; set; }

    [JsonProperty("Program Display Name")]
    public string ProgramDisplayName { get; set; }
    public Elements.ProgramRequirement ToElement(Elements.CatalogWrapper catalogWrapper, Elements.SpaceConfigurationElement spaceConfigElem)
    {
        var req = new Elements.ProgramRequirement
        {
            ProgramGroup = this.ProgramGroup,
            ProgramName = this.ProgramName,
            Color = this.Color ?? Colors.Magenta,
            SpaceCount = this.SpaceCount ?? 1,
            AreaPerSpace = this.Dimensions?.Area ?? this.AreaPerSpace ?? 0.0,
            Dimensions = this.Dimensions,
            Width = this.Dimensions?.Width ?? this.Width,
            Depth = this.Dimensions?.Depth ?? this.Depth,
            HyparSpaceType = this.HyparSpaceType ?? this.QualifiedProgramName,
            CountType = (Elements.ProgramRequirementCountType)this.CountType,
            Catalog = catalogWrapper?.Id,
            Enclosed = this.Enclosed ?? false,
            SpaceConfig = spaceConfigElem?.Id,
            DefaultWallType = this.DefaultWallType,
            ProgramDisplayName = this.ProgramDisplayName,
            LayoutType = this.LayoutType?.Id is not null ? new InputFolder(this.LayoutType.Id, Array.Empty<InputFileRef>(), this.LayoutType.Name) : null
        };
        req.Id = this.Id ?? req.Id;
        return req;
    }

}
