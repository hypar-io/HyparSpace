using System;
using System.Collections.Generic;
using Elements;
using Elements.Geometry;

namespace Elements
{
    public interface ILevelVolume
    {
        Profile Profile { get; set; }

        double Height { get; set; }
    }

}