using Elements.Geometry;

namespace TravelDistanceAnalyzer
{
    public static class ThickenedPolylineExtensions
    {
        public static double GetWidth(this ThickenedPolyline polyline)
        {
            return polyline.LeftWidth + polyline.RightWidth;
        }

        public static double GetOffset(this ThickenedPolyline polyline) 
        {
            return (polyline.RightWidth - polyline.LeftWidth) / 2;
        }
    }
}
