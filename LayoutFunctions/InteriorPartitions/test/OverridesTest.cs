using Elements;
using Xunit;
using System;
using System.IO;
using System.Collections.Generic;
using Elements.Serialization.glTF;
using Elements.Geometry;
using System.Linq;

namespace InteriorPartitions.Tests
{
    public class OverridesTest
    {
        private InteriorPartitionsInputs GetInput(string filename)
        {
            var json = File.ReadAllText($"../../../OverridesTestData/{filename}.json");
            return Newtonsoft.Json.JsonConvert.DeserializeObject<InteriorPartitionsInputs>(json);
        }

        private static List<InteriorPartitionCandidate> CreateInteriorPartitions()
        {
            var interiorPartitionCandidates = new List<InteriorPartitionCandidate>();
            var wallCandidateLines = new List<(Line line, string type)>();
            wallCandidateLines.Add((new Line(new Vector3(), new Vector3(30, 0)), "Solid"));
            interiorPartitionCandidates.Add(new InteriorPartitionCandidate(Guid.NewGuid())
            {
                WallCandidateLines = wallCandidateLines,
                Height = 3,
                LevelTransform = new Transform()
            });
            return interiorPartitionCandidates;
        }

        [Fact]
        public void SplitInTheMiddle()
        {
            var input = GetInput("inputs1");
            List<InteriorPartitionCandidate> interiorPartitionCandidates = CreateInteriorPartitions();
            var wallCandidates = InteriorPartitions.CreateWallCandidates(input, interiorPartitionCandidates);
            Assert.Equal(3, wallCandidates.Count);
            Assert.Contains(wallCandidates, w => w.Line.IsAlmostEqualTo(new Line(new Vector3(), new Vector3(10, 0)), false));
            Assert.Contains(wallCandidates, w => w.Line.IsAlmostEqualTo(new Line(new Vector3(10, 0), new Vector3(20, 0)), false));
            Assert.Contains(wallCandidates, w => w.Line.IsAlmostEqualTo(new Line(new Vector3(20, 0), new Vector3(30, 0)), false));
        }

        [Fact]
        public void RevertEditedPartAfterSplitInTheMiddle()
        {
            var input = GetInput("inputs2");
            List<InteriorPartitionCandidate> interiorPartitionCandidates = CreateInteriorPartitions();
            var wallCandidates = InteriorPartitions.CreateWallCandidates(input, interiorPartitionCandidates);
            Assert.Equal(3, wallCandidates.Count);
            Assert.Contains(wallCandidates, w => w.Line.IsAlmostEqualTo(new Line(new Vector3(), new Vector3(10, 0)), false));
            Assert.Contains(wallCandidates, w => w.Line.IsAlmostEqualTo(new Line(new Vector3(10, 0), new Vector3(20, 0)), false));
            Assert.Contains(wallCandidates, w => w.Line.IsAlmostEqualTo(new Line(new Vector3(20, 0), new Vector3(30, 0)), false));
        }

        [Fact]
        public void UpdateRevertedPartAfterSplit()
        {
            var input = GetInput("inputs3");
            List<InteriorPartitionCandidate> interiorPartitionCandidates = CreateInteriorPartitions();
            var wallCandidates = InteriorPartitions.CreateWallCandidates(input, interiorPartitionCandidates);
            Assert.Equal(3, wallCandidates.Count);
            Assert.Contains(wallCandidates, w => w.Line.IsAlmostEqualTo(new Line(new Vector3(0, 10), new Vector3(10, 0)), false) && w.Type.Equals("Solid"));
            Assert.Contains(wallCandidates, w => w.Line.IsAlmostEqualTo(new Line(new Vector3(10, 0), new Vector3(20, 0)), false) && w.Type.Equals("Glass"));
            Assert.Contains(wallCandidates, w => w.Line.IsAlmostEqualTo(new Line(new Vector3(20, 0), new Vector3(30, 0)), false) && w.Type.Equals("Glass"));
        }

        [Fact]
        public void AddOverlappingWall()
        {
            var input = GetInput("inputs4");
            List<InteriorPartitionCandidate> interiorPartitionCandidates = CreateInteriorPartitions();
            var wallCandidates = InteriorPartitions.CreateWallCandidates(input, interiorPartitionCandidates);
            Assert.Equal(3, wallCandidates.Count);
            Assert.Contains(wallCandidates, w => w.Line.IsAlmostEqualTo(new Line(new Vector3(-10, 0), new Vector3(0, 0)), false) && w.Type.Equals("Solid"));
            Assert.Contains(wallCandidates, w => w.Line.IsAlmostEqualTo(new Line(new Vector3(0, 0), new Vector3(10, 0)), false) && w.Type.Equals("Glass"));
            Assert.Contains(wallCandidates, w => w.Line.IsAlmostEqualTo(new Line(new Vector3(10, 0), new Vector3(30, 0)), false) && w.Type.Equals("Glass"));
        }

        [Fact]
        public void SplitInTheMiddleAndRemoveMiddlePart()
        {
            var input = GetInput("inputs5");
            List<InteriorPartitionCandidate> interiorPartitionCandidates = CreateInteriorPartitions();
            var wallCandidates = InteriorPartitions.CreateWallCandidates(input, interiorPartitionCandidates);
            Assert.Equal(2, wallCandidates.Count);
            Assert.Contains(wallCandidates, w => w.Line.IsAlmostEqualTo(new Line(new Vector3(0, 0), new Vector3(10, 0)), false) && w.Type.Equals("Glass"));
            Assert.Contains(wallCandidates, w => w.Line.IsAlmostEqualTo(new Line(new Vector3(20, 0), new Vector3(30, 0)), false) && w.Type.Equals("Glass"));
        }

        [Fact]
        public void SplitInTheMiddleRemoveAndAddAgainMiddlePart()
        {
            var input = GetInput("inputs6");
            List<InteriorPartitionCandidate> interiorPartitionCandidates = CreateInteriorPartitions();
            var wallCandidates = InteriorPartitions.CreateWallCandidates(input, interiorPartitionCandidates);
            Assert.Equal(3, wallCandidates.Count);
            Assert.Contains(wallCandidates, w => w.Line.IsAlmostEqualTo(new Line(new Vector3(0, 0), new Vector3(10, 0)), false) && w.Type.Equals("Glass"));
            Assert.Contains(wallCandidates, w => w.Line.IsAlmostEqualTo(new Line(new Vector3(10, 0), new Vector3(20, 0)), false) && w.Type.Equals("Solid"));
            Assert.Contains(wallCandidates, w => w.Line.IsAlmostEqualTo(new Line(new Vector3(20, 0), new Vector3(30, 0)), false) && w.Type.Equals("Glass"));
        }
    }
}