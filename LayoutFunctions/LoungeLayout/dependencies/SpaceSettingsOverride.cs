using Elements;

namespace LoungeLayout
{
    public partial class SpaceSettingsValue : ISpaceSettingsOverrideFlipValue
    {
    }

    public partial class SpaceSettingsOverride : ISpaceSettingsOverride<SpaceSettingsValue>
    {
        public SpaceSettingsOverride()
        {
        }
    }
}