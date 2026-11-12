using Elements;
using Elements.Geometry;
using Newtonsoft.Json.Linq;

namespace WallsLOD200.Tests;

public class ParentTransformTests
{
    private static readonly Guid LevelId = Guid.NewGuid();

    [Fact]
    public void WallOnMassInMovedRotatedBuilding_LandsAtWorldPosition()
    {
        var buildingId = Guid.NewGuid();
        var massId = Guid.NewGuid();
        var building = new Transform().Rotated(Vector3.ZAxis, 90).Moved(100, 50, 0);
        var mass = new Transform(10, 0, 0);
        var masses = ModelFromJson(
            ElementJson("Elements.Building", buildingId, building, parent: null),
            ElementJson("Elements.ConceptualMass", massId, mass, parent: buildingId));

        var line = RunOne(Wall("4", massId), new() { ["Conceptual Mass"] = masses });

        // Wall (0,0)-(5,0), +10 in X on the mass, then rotated 90° and moved by the building.
        AssertLine(line, new Vector3(100, 60, 0), new Vector3(100, 65, 0));
    }

    [Fact]
    public void WallOnSpace_StillResolves()
    {
        var spaceId = Guid.NewGuid();
        var spaces = ModelFromJson(ElementJson("Elements.SpaceBoundary", spaceId, new Transform(20, 0, 0), parent: null));

        var line = RunOne(Wall("4", spaceId), new() { ["Space Planning Zones"] = spaces });

        AssertLine(line, new Vector3(20, 0, 0), new Vector3(25, 0, 0));
    }

    [Fact]
    public void NoConceptualMassModel_StillRuns()
    {
        var line = RunOne(Wall("4", Guid.NewGuid()), new());

        AssertLine(line, new Vector3(0, 0, 0), new Vector3(5, 0, 0));
    }

    [Fact]
    public void WallBeforeVersion4_IsLeftInWorldSpace()
    {
        var spaceId = Guid.NewGuid();
        var spaces = ModelFromJson(ElementJson("Elements.SpaceBoundary", spaceId, new Transform(20, 0, 0), parent: null));

        var line = RunOne(Wall("3", spaceId), new() { ["Space Planning Zones"] = spaces });

        AssertLine(line, new Vector3(0, 0, 0), new Vector3(5, 0, 0));
    }

    private static StandardWall Wall(string wallsVersion, Guid parent)
    {
        var wall = new StandardWall(new Line((0, 0, 0), (5, 0, 0)), 0.2, 3, wallsVersion: wallsVersion);
        wall.AdditionalProperties["Parent"] = parent.ToString();
        wall.AdditionalProperties["Level"] = LevelId.ToString();
        return wall;
    }

    private static Line RunOne(StandardWall wall, Dictionary<string, Model> models)
    {
        var walls = new Model();
        walls.AddElement(wall);
        var levels = new Model();
        levels.AddElement(new Level(0, 3, null, id: LevelId));

        // Round-trip through JSON so every model reads the way a function receives it.
        models["Walls"] = Model.FromJson(walls.ToJson());
        models["Levels"] = Model.FromJson(levels.ToJson());
        foreach (var name in models.Keys.ToList())
        {
            models[name] = Model.FromJson(models[name].ToJson());
        }

        var output = WallsLOD200.Execute(models, new WallsLOD200Inputs(new Dictionary<string, string>(), "", "", ""));

        var result = Assert.Single(output.Model.AllElementsOfType<StandardWall>());
        return result.CenterLine.TransformedLine(result.Transform);
    }

    private static Model ModelFromJson(params JObject[] elements)
    {
        var json = new JObject
        {
            ["Transform"] = JObject.FromObject(new Transform()),
            ["Elements"] = new JObject(elements.Select(e => new JProperty((string)e["Id"]!, e))),
        };
        return Model.FromJson(json.ToString());
    }

    private static JObject ElementJson(string discriminator, Guid id, Transform transform, Guid? parent)
    {
        var element = new JObject
        {
            ["discriminator"] = discriminator,
            ["Id"] = id.ToString(),
            ["Name"] = discriminator,
            ["Transform"] = JObject.FromObject(transform),
        };
        if (parent.HasValue)
        {
            element["Parent"] = parent.Value.ToString();
        }
        return element;
    }

    private static void AssertLine(Line actual, Vector3 start, Vector3 end)
    {
        var matches = (actual.Start.IsAlmostEqualTo(start) && actual.End.IsAlmostEqualTo(end))
            || (actual.Start.IsAlmostEqualTo(end) && actual.End.IsAlmostEqualTo(start));
        Assert.True(matches, $"Expected {start} -> {end}, got {actual.Start} -> {actual.End}");
    }
}
