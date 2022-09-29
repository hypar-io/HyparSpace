using System;
using Elements.Geometry;

namespace Elements
{
    public class CorridorCandidate : Element
    {
        public Guid Level { get; set; }
        public Line Line { get; set; }
    }
}