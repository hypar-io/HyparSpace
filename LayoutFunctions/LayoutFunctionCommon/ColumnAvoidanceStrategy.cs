namespace LayoutFunctionCommon
{
    public enum ColumnAvoidanceStrategy
    {
        [System.Runtime.Serialization.EnumMember(Value = @"Adaptive Grid")]
        Adaptive_Grid = 0,

        [System.Runtime.Serialization.EnumMember(Value = @"Cull")]
        Cull = 1,

        [System.Runtime.Serialization.EnumMember(Value = @"None")]
        None = 2,
    }
}