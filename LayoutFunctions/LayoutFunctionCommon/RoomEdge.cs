using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Elements;
using Elements.Geometry;

namespace LayoutFunctionCommon;

public class RoomEdge
{
    private Line _line;
    private string _type;
    // Ugly — backwards serialization compatibility on read
    [JsonIgnore]
    public Line Item1
    {
        get
        {
            return _line;
        }
        set
        {
            _line = value;
        }
    }

    [JsonIgnore]
    public string Item2
    {
        get
        {
            return _type;
        }
        set
        {
            _type = value;
        }
    }
    public Line Line
    {
        get
        {
            return _line;
        }
        set
        {
            _line = value;
        }
    }
    public string Type
    {
        get
        {
            return _type;
        }
        set
        {
            _type = value;
        }
    }
    public (double innerWidth, double outerWidth)? Thickness { get; set; }
    public double Length => Line.Length();
    public Vector3 Direction => Line.Direction();

    public bool? PrimaryEntryEdge { get; set; }
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