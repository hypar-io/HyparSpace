using Elements;
using Hypar.Model;

namespace OpenOfficeLayout
{
    public partial class SpaceSettingsValue : ISpaceSettingsOverrideDesksValue
    {
        public string GetDeskType => Utilities.GetStringValueFromEnum(DeskType);
    }

    public partial class SpaceSettingsOverride : ISpaceSettingsOverride<SpaceSettingsValue>
    {
    }

}