using Elements.Geometry;

namespace Elements;

public class RoomSeparatorLine : Element
{
    public Line Line { get; set; }

    public Guid? Level { get; set; }

    public Transform Transform { get; set; } = new Transform();

}