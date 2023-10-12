using System.Collections.Generic;
using System.Linq;
using Elements.Geometry;

namespace LayoutFunctionCommon;

public class RoomEdge
{
    public Line Line { get; set; }
    public string Type { get; set; }
    public (double innerWidth, double outerWidth)? Thickness { get; set; }

    public double Length => Line.Length();
    public Vector3 Direction => Line.Direction();
}

public static class RoomEdgeExtensions
{
    public static List<RoomEdge> RoomEdges(this Profile p)
    {
        var segments = p.Perimeter.Segments();
        var thickness = p.GetEdgeThickness();
        return segments.Select((seg, i) =>
        {
            return new RoomEdge
            {
                Line = seg,
                Thickness = thickness?.ElementAtOrDefault(i)
            };
        }).ToList();
    }
}