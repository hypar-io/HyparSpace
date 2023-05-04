using System;
using System.Collections.Generic;
using System.Linq;
using Elements.Geometry;
using Elements.Search;

namespace Elements
{
    public interface IThickenedPolyline
    {
        public Polyline Polyline { get; }
        public double LeftWidth { get; }
        public double RightWidth { get; }

        public static List<(Polygon offsetPolygon, Line offsetLine)> GetPolygons(IEnumerable<IThickenedPolyline> polylines, Vector3? normal = null)
        {
            if (!polylines.Any())
            {
                return new();
            }
            var resultList = new Dictionary<int, (Polygon offsetPolygon, Line offsetLine)>();
            var graph = new
            {
                nodes = new List<(
                    Vector3 position,
                    List<(
                        int otherPointIndex,
                        Vector3 otherPoint,
                        double leftWidth,
                        double rightWidth,
                        bool pointingAway
                        )> edges,
                        Dictionary<int, Vector3[]> offsetVertexMap
                    )>(),
                edges = new List<(int a, int b, int origPlIndex)>()
            };
            var pointSet = new PointOctree<int?>(10, polylines.First().Polyline.Vertices.First(), Vector3.EPSILON);
            var segments = polylines.SelectMany((polyline) => polyline.Polyline.Segments().Select(s => (Line: s, polyline.LeftWidth, polyline.RightWidth))).ToArray();
            for (int i = 0; i < segments.Length; i++)
            {
                (Line Line, double LeftWidth, double RightWidth) segment = segments[i];
                var ptA = segment.Line.Start;
                var ptB = segment.Line.End;

                var indexA = pointSet.GetNearby(ptA, Vector3.EPSILON).FirstOrDefault();
                var indexB = pointSet.GetNearby(ptB, Vector3.EPSILON).FirstOrDefault();
                if (indexA == null)
                {
                    indexA = graph.nodes.Count;
                    pointSet.Add(indexA, ptA);
                    graph.nodes.Add((
                        ptA,
                        new(),
                        new()
                        ));
                }
                if (indexB == null)
                {
                    indexB = graph.nodes.Count;
                    pointSet.Add(indexB, ptB);
                    graph.nodes.Add((
                        ptB,
                        new(),
                        new()
                        ));
                }

                graph.edges.Add((indexA.Value, indexB.Value, i));
                graph.nodes[indexA.Value].edges.Add((
                    indexB.Value,
                    ptB,
                    segment.LeftWidth,
                    segment.RightWidth,
                    true));
                graph.nodes[indexB.Value].edges.Add((
                    indexA.Value,
                    ptA,
                    segment.LeftWidth,
                    segment.RightWidth,
                    false
                    ));
            }
            foreach (var (position, edges, offsetVertexMap) in graph.nodes)
            {
                var projectionPlane = new Plane(position, normal ?? Vector3.ZAxis);
                var edgesSorted = edges.Select((edge) =>
                {
                    var otherPoint = edge.otherPoint;
                    var edgeVector = otherPoint - position;
                    var edgeAngle = Vector3.XAxis.PlaneAngleTo(edgeVector, normal ?? Vector3.ZAxis);
                    var edgeLength = edgeVector.Length();
                    offsetVertexMap[edge.otherPointIndex] = new Vector3[3];
                    offsetVertexMap[edge.otherPointIndex][1] = position;
                    return (edge, edgeVector, edgeAngle, edgeLength);
                }).OrderBy((edge) => edge.edgeAngle).ToArray();
                for (int i = 0; i < edgesSorted.Length; i++)
                {
                    var edge = edgesSorted[i];
                    var nextOffsetDist = edge.edge.pointingAway ? edge.edge.leftWidth : edge.edge.rightWidth;
                    var consistentCenterLine = new[] { position, edge.edge.otherPoint };
                    var awayDir = edge.edgeVector;
                    var perpendicular = awayDir.Cross(normal ?? Vector3.ZAxis).Unitized();
                    var leftOffsetLine = new Line(
                        consistentCenterLine[0] + perpendicular * nextOffsetDist * -1,
                        consistentCenterLine[1] + perpendicular * nextOffsetDist * -1
                    ).Projected(projectionPlane);

                    var nextEdge = edgesSorted[(i + 1) % edgesSorted.Length];
                    var nextCenterLine = new[] { position, nextEdge.edge.otherPoint };
                    var nextOffsetDist2 = nextEdge.edge.pointingAway ? nextEdge.edge.rightWidth : nextEdge.edge.leftWidth;
                    var nextAwayDir = nextEdge.edgeVector;
                    var nextPerpendicular = nextAwayDir.Cross(normal ?? Vector3.ZAxis).Unitized();
                    var rightOffsetLine = new Line(
                        nextCenterLine[0] + nextPerpendicular * nextOffsetDist2,
                        nextCenterLine[1] + nextPerpendicular * nextOffsetDist2
                    ).Projected(projectionPlane);
                    var intersects = leftOffsetLine.Intersects(rightOffsetLine, out var intersection, true);
                    // var minDistance = edge.edge.leftWidth * 5;
                    var angleThreshold = 90;
                    var angleDiff = (nextEdge.edgeAngle - edge.edgeAngle + 360) % 360;
                    if (intersects)
                    {
                        var maxLength = Math.Min(edge.edgeLength, nextEdge.edgeLength);
                        if (angleDiff < angleThreshold)
                        {
                            // acute angle
                            var toIntersectionVec = intersection - position;
                            if (toIntersectionVec.Length() > maxLength)
                            {
                                intersection = position + toIntersectionVec.Unitized() * maxLength;
                            }
                            offsetVertexMap[edge.edge.otherPointIndex][0] = intersection;
                            offsetVertexMap[nextEdge.edge.otherPointIndex][2] = intersection;
                        }
                        else if (angleDiff > 360 - angleThreshold)
                        {
                            // reflex angle
                            var squareEndLeft = leftOffsetLine.Start + awayDir.Unitized() * -1 * nextOffsetDist;
                            var squareEndRight = rightOffsetLine.Start + nextAwayDir.Unitized() * -1 * nextOffsetDist;
                            var newInt = (squareEndLeft + squareEndRight) * 0.5;
                            offsetVertexMap[edge.edge.otherPointIndex][0] = squareEndLeft;
                            offsetVertexMap[edge.edge.otherPointIndex][1] = newInt;
                            offsetVertexMap[nextEdge.edge.otherPointIndex][1] = newInt;
                            offsetVertexMap[nextEdge.edge.otherPointIndex][2] = squareEndRight;
                        }
                        else if (Math.Abs(360 - angleDiff) < angleThreshold / 2 && intersection.DistanceTo(position) > maxLength)
                        {
                            offsetVertexMap[edge.edge.otherPointIndex][0] = leftOffsetLine.Start;
                            offsetVertexMap[nextEdge.edge.otherPointIndex][2] = rightOffsetLine.Start;
                        }
                        else
                        {
                            offsetVertexMap[edge.edge.otherPointIndex][0] = intersection;
                            offsetVertexMap[nextEdge.edge.otherPointIndex][2] = intersection;
                        }
                    }
                    else
                    {
                        offsetVertexMap[edge.edge.otherPointIndex][0] = leftOffsetLine.Start;
                        offsetVertexMap[nextEdge.edge.otherPointIndex][2] = rightOffsetLine.Start;
                    }
                }
            }
            var polygons = new List<Polygon>();
            var lines = new List<Line>();
            foreach (var (a, b, origPlIndex) in graph.edges)
            {

                var abc = graph.nodes[a].offsetVertexMap[b];
                var def = graph.nodes[b].offsetVertexMap[a];
                try
                {
                    var pgonOutput = new Polygon(abc.Concat(def).ToArray());
                    resultList[origPlIndex] = (offsetPolygon: pgonOutput, offsetLine: null);
                }
                catch
                {
                    try
                    {
                        // we may have a degenerate polygon.
                        Elements.Validators.Validator.DisableValidationOnConstruction = true;
                        var pgonOutput = new Polygon(abc.Concat(def).ToArray());
                        var offsets = Profile.Offset(Profile.Offset(new Profile[] { pgonOutput }, -0.01), 0.01);
                        // get largest offset
                        var largestOffset = offsets.OrderByDescending((pgon) => Math.Abs(pgon.Area())).First();
                        resultList[origPlIndex] = (offsetPolygon: largestOffset.Perimeter, offsetLine: null);
                        Elements.Validators.Validator.DisableValidationOnConstruction = false;
                    }
                    catch
                    {
                        resultList[origPlIndex] = (offsetPolygon: null, offsetLine: new Line(abc[1], def[1]));
                    }
                }

            }
            return resultList.Values.ToList();
        }
    }
}