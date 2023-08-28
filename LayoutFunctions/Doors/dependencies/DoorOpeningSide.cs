using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Doors.Dependencies
{
    public enum DoorOpeningSide
    {
        [System.Runtime.Serialization.EnumMember(Value = @"Left Hand")]
        LeftHand,
        [System.Runtime.Serialization.EnumMember(Value = @"Right Hand")]
        RightHand,
        [System.Runtime.Serialization.EnumMember(Value = @"Double Door")]
        DoubleDoor
    }
}
