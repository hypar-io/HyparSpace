using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Elements;
using Elements.Geometry;
using Elements.Components;
using Newtonsoft.Json;
using Xunit;

namespace CustomSpaceType.Tests
{
    public class WallOffsetTests
    {
        [Fact]
        public void WallOffsetTest()
        {
            var polyline = new Polyline(new List<Vector3>()
            {
                new Vector3(0,0,0),
                new Vector3(10,0,0),
                new Vector3(10,10,0)
            });

            var tp = new ThickenedPolyline(polyline, 0, 0);
            var walls = CustomSpaceType.OffsetThickenedPolyline(tp, polyline);
            Assert.Equal(2, walls.Count());
            Assert.Equal(4, walls.First().Vertices.Count);
            Assert.Equal(4, walls.Last().Vertices.Count);
            Assert.Contains(new Vector3(0, -1, 0), walls.First().Vertices);
            Assert.Contains(new Vector3(11, -1, 0), walls.First().Vertices);
            Assert.Contains(new Vector3(10, 0, 0), walls.First().Vertices);
            Assert.Contains(new Vector3(0, 0, 0), walls.First().Vertices);
            Assert.Contains(new Vector3(10, 0, 0), walls.Last().Vertices);
            Assert.Contains(new Vector3(11, -1, 0), walls.Last().Vertices);
            Assert.Contains(new Vector3(11, 10, 0), walls.Last().Vertices);
            Assert.Contains(new Vector3(10, 10, 0), walls.Last().Vertices);

            tp = new ThickenedPolyline(polyline, 0, 0);
            walls = CustomSpaceType.OffsetThickenedPolyline(tp, polyline);
            Assert.Contains(new Vector3(0, 1, 0), walls.First().Vertices);
            Assert.Contains(new Vector3(9, 1, 0), walls.First().Vertices);
            Assert.Contains(new Vector3(10, 0, 0), walls.First().Vertices);
            Assert.Contains(new Vector3(0, 0, 0), walls.First().Vertices);
            Assert.Contains(new Vector3(10, 0, 0), walls.Last().Vertices);
            Assert.Contains(new Vector3(9, 1, 0), walls.Last().Vertices);
            Assert.Contains(new Vector3(9, 10, 0), walls.Last().Vertices);
            Assert.Contains(new Vector3(10, 10, 0), walls.Last().Vertices);

            tp = new ThickenedPolyline(polyline, 0, 1);
            walls = CustomSpaceType.OffsetThickenedPolyline(tp, polyline);
            Assert.Equal(2, walls.Count());
            Assert.Equal(4, walls.First().Vertices.Count);
            Assert.Equal(4, walls.Last().Vertices.Count);
            Assert.Contains(new Vector3(0, -1, 0), walls.First().Vertices);
            Assert.Contains(new Vector3(11, -1, 0), walls.First().Vertices);
            Assert.Contains(new Vector3(10, 0, 0), walls.First().Vertices);
            Assert.Contains(new Vector3(0, 0, 0), walls.First().Vertices);
            Assert.Contains(new Vector3(10, 0, 0), walls.Last().Vertices);
            Assert.Contains(new Vector3(11, -1, 0), walls.Last().Vertices);
            Assert.Contains(new Vector3(11, 10, 0), walls.Last().Vertices);
            Assert.Contains(new Vector3(10, 10, 0), walls.Last().Vertices);

            tp = new ThickenedPolyline(polyline, 1, 0);
            walls = CustomSpaceType.OffsetThickenedPolyline(tp, polyline);
            Assert.Contains(new Vector3(0, 1, 0), walls.First().Vertices);
            Assert.Contains(new Vector3(9, 1, 0), walls.First().Vertices);
            Assert.Contains(new Vector3(10, 0, 0), walls.First().Vertices);
            Assert.Contains(new Vector3(0, 0, 0), walls.First().Vertices);
            Assert.Contains(new Vector3(10, 0, 0), walls.Last().Vertices);
            Assert.Contains(new Vector3(9, 1, 0), walls.Last().Vertices);
            Assert.Contains(new Vector3(9, 10, 0), walls.Last().Vertices);
            Assert.Contains(new Vector3(10, 10, 0), walls.Last().Vertices);

            tp = new ThickenedPolyline(polyline, 0.4, 0.6);
            walls = CustomSpaceType.OffsetThickenedPolyline(tp, polyline);
            Assert.Contains(new Vector3(0, -0.6, 0), walls.First().Vertices);
            Assert.Contains(new Vector3(10.6, -0.6, 0), walls.First().Vertices);
            Assert.Contains(new Vector3(9.6, 0.4, 0), walls.First().Vertices);
            Assert.Contains(new Vector3(0, 0.4, 0), walls.First().Vertices);
            Assert.Contains(new Vector3(10.6, -0.6, 0), walls.Last().Vertices);
            Assert.Contains(new Vector3(9.6, 0.4, 0), walls.Last().Vertices);
            Assert.Contains(new Vector3(10.6, 10, 0), walls.Last().Vertices);
            Assert.Contains(new Vector3(9.6, 10, 0), walls.Last().Vertices);
        }
    }
}