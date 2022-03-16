using System;
using System.Collections.Generic;
using Elements.Geometry;

namespace Elements
{
    public class InteriorPartitionCandidate : Element
    {
        public List<(Line line, string type)> WallCandidateLines { get; set; }

        public double Height { get; set; }

        public Transform LevelTransform { get; set; }

        public InteriorPartitionCandidate(Guid id = default, string name = null)
            : base(id, name)
        {
        }
    }
}