namespace Elements
{
    public interface ISpaceSettingsOverride<TValueType>: IOverride where TValueType : ISpaceSettingsOverrideValue
    {
        TValueType Value { get; set; }
    }

    public interface ISpaceSettingsOverrideValue
    {
    }

    public interface ISpaceSettingsOverrideFlipValue : ISpaceSettingsOverrideValue
    {
        public bool PrimaryAxisFlipLayout { get; set; }
        public bool SecondaryAxisFlipLayout { get; set; }
    }

    public interface ISpaceSettingsOverrideDesksValue : ISpaceSettingsOverrideValue
    {
        public string GetDeskType { get; }
        public double GridRotation { get; set; }

        public double IntegratedCollaborationSpaceDensity { get; set; }

        public double AisleWidth { get; set; }

        public double BackToBackWidth { get; set; }
    }
}