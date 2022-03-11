using System;
using System.Collections.Generic;
using Elements.Geometry;

namespace Elements
{
    public class InteriorPartitionCandidate : Element
    {
        public List<(Line line, string type)> WallCandidateLines { get; }

        public double Height { get; }

        public Transform LevelTransform { get; }

        public InteriorPartitionCandidate(List<(Line line, string type)> wallCandidateLines, double height, Transform levelTransform, Guid id = default, string name = null)
            : base(id, name)
        {
            this.WallCandidateLines = wallCandidateLines;
            this.Height = height;
            this.LevelTransform = levelTransform;
        }
    }
}