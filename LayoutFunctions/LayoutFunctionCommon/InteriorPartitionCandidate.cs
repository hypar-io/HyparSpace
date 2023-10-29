using System;
using System.Collections.Generic;
using Elements.Geometry;
using LayoutFunctionCommon;

namespace Elements
{
    public class InteriorPartitionCandidate : Element
    {
        public List<RoomEdge> WallCandidateLines { get; set; }

        public double Height { get; set; }

        public Transform LevelTransform { get; set; }

        public InteriorPartitionCandidate(Guid id = default, string name = null)
            : base(id, name)
        {
        }
    }
}