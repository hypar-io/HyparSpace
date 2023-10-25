using Elements;

namespace Doors
{
    internal static class DoorOpeningEnumsHelper
    {
        internal static DoorOpeningSide ConvertOpeningSideEnum(DoorPositionsOverrideAdditionValueDoorOpeningSide openingSide) => openingSide switch
        {
            DoorPositionsOverrideAdditionValueDoorOpeningSide.Left_Hand => DoorOpeningSide.LeftHand,
            DoorPositionsOverrideAdditionValueDoorOpeningSide.Right_Hand => DoorOpeningSide.RightHand,
            DoorPositionsOverrideAdditionValueDoorOpeningSide.Double_Door => DoorOpeningSide.DoubleDoor,
            _ => DoorOpeningSide.Undefined,
        };

        internal static DoorOpeningType ConvertOpeningTypeEnum(DoorPositionsOverrideAdditionValueDoorOpeningType openingType) => openingType switch
        {
            DoorPositionsOverrideAdditionValueDoorOpeningType.Single_Swing => DoorOpeningType.SingleSwing,
            DoorPositionsOverrideAdditionValueDoorOpeningType.Double_Swing => DoorOpeningType.DoubleSwing,
            _ => DoorOpeningType.Undefined,
        };

        internal static DoorOpeningSide ConvertOpeningSideEnum(DoorPositionsValueDefaultDoorOpeningSide openingSide) => openingSide switch
        {
            DoorPositionsValueDefaultDoorOpeningSide.Left_Hand => DoorOpeningSide.LeftHand,
            DoorPositionsValueDefaultDoorOpeningSide.Right_Hand => DoorOpeningSide.RightHand,
            DoorPositionsValueDefaultDoorOpeningSide.Double_Door => DoorOpeningSide.DoubleDoor,
            _ => DoorOpeningSide.Undefined,
        };

        internal static DoorOpeningType ConvertOpeningTypeEnum(DoorPositionsValueDefaultDoorOpeningType openingType) => openingType switch
        {
            DoorPositionsValueDefaultDoorOpeningType.Single_Swing => DoorOpeningType.SingleSwing,
            DoorPositionsValueDefaultDoorOpeningType.Double_Swing => DoorOpeningType.DoubleSwing,
            _ => DoorOpeningType.Undefined,
        };

        internal static DoorOpeningSide ConvertOpeningSideEnum(DoorsInputsDefaultDoorOpeningSide openingSide) => openingSide switch
        {
            DoorsInputsDefaultDoorOpeningSide.Left_Hand => DoorOpeningSide.LeftHand,
            DoorsInputsDefaultDoorOpeningSide.Right_Hand => DoorOpeningSide.RightHand,
            DoorsInputsDefaultDoorOpeningSide.Double_Door => DoorOpeningSide.DoubleDoor,
            _ => DoorOpeningSide.Undefined,
        };

        internal static DoorOpeningType ConvertOpeningTypeEnum(DoorsInputsDefaultDoorOpeningType openingType) => openingType switch
        {
            DoorsInputsDefaultDoorOpeningType.Single_Swing => DoorOpeningType.SingleSwing,
            DoorsInputsDefaultDoorOpeningType.Double_Swing => DoorOpeningType.DoubleSwing,
            _ => DoorOpeningType.Undefined,
        };
    }
}
