namespace Elements
{
    public interface ISpaceSettingsOverride<TValueType> where TValueType : ISpaceSettingsOverrideValue
    {
        TValueType Value { get; set; }
    }

    public interface ISpaceSettingsOverrideValue
    {
        public string GetDeskType { get; }
        public double GridRotation { get; set; }

        public double IntegratedCollaborationSpaceDensity { get; set; }

        public double AisleWidth { get; set; }
    }
}