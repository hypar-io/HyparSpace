using System.Collections.Generic;
using System.Linq;
using Elements;
using Elements.Geometry;

namespace LayoutFunctionCommon
{
    public static class Circulation
    {
        public static List<Line> GetCorridorSegments<TCirculationSegment, TSpaceBoundary>(IEnumerable<Element> elements)
         where TCirculationSegment : Floor where TSpaceBoundary : ISpaceBoundary
        {
            var corridorSegments = new List<Line>();
            var circulationSegments = elements.OfType<TCirculationSegment>();
            corridorSegments.AddRange(circulationSegments.SelectMany(p => p.Profile.Segments()));
            var spacesAsCirculation = elements.OfType<TSpaceBoundary>().Where(z => z.HyparSpaceType == "Circulation");
            corridorSegments.AddRange(spacesAsCirculation.SelectMany(p => p.Boundary.Segments()));
            return corridorSegments;
        }
    }
}