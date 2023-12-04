using Elements;
using System;

namespace Doors
{
    internal static class DoorOpeningEnumsHelper
    {
        internal static DoorOpeningSide ConvertOpeningSideEnum<T>(T openingSide)
        {
            if (!Enum.IsDefined(typeof(T), openingSide))
            {
                return DoorOpeningSide.LeftHand;
            }

            // We shouldn't have an "Undefined" enum value. TODO: Handle Undefined enum in Door for representation
            return (DoorOpeningSide)(Convert.ToInt32(openingSide) + 1);
        }

        internal static DoorOpeningType ConvertOpeningTypeEnum<T>(T openingType)
        {
            if (!Enum.IsDefined(typeof(T), openingType))
            {
                return DoorOpeningType.SingleSwing;
            }

            // We shouldn't have an "Undefined" enum value. TODO: Handle Undefined enum in Door for representation
            return (DoorOpeningType)(Convert.ToInt32(openingType) + 1);
        }
    }
}
