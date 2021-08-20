using System.Linq;
using Elements;
public static class TestHelper
{
    public static void AddCurvesForSpaceBoundaries(this Model m)
    {
        var spaceBoundaries = m.AllElementsOfType<SpaceBoundary>().ToList();
        foreach (var spaceBoundary in spaceBoundaries)
        {
            m.AddElements(spaceBoundary.Boundary.ToModelCurves());
        }
    }
}