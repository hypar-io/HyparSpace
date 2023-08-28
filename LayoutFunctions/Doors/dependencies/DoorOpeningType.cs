using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Doors.Dependencies
{
    public enum DoorOpeningType
    {
        [System.Runtime.Serialization.EnumMember(Value = @"Single Swing")]
        SingleSwing,
        [System.Runtime.Serialization.EnumMember(Value = @"Double Swing")]
        DoubleSwing
    }
}
