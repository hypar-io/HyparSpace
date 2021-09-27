using System;
using Elements.Geometry;

namespace Elements
{
    public partial class AreaTally : Element
    {
        public AreaTally(string @programType, Color @programColor, double @areaTarget, double @achievedArea, double @distinctAreaCount)
            : this(@programType, @programColor, @areaTarget, @achievedArea, @distinctAreaCount, null, null, null, Guid.NewGuid(), null)
        {

        }

    }
}