using Elements;
using Elements.Geometry;
using Elements.Spatial;
using System.Collections.Generic;
using System.Linq;

namespace DataHall
{
    public static class DataHall
    {
        /// <summary>
        /// The DataHall function.
        /// </summary>
        /// <param name="model">The input model.</param>
        /// <param name="input">The arguments to the execution.</param>
        /// <returns>A DataHallOutputs instance containing computed results and the model with any new elements.</returns>
        public static DataHallOutputs Execute(Dictionary<string, Model> inputModels, DataHallInputs input)
        {
            var spacePlanningZones = inputModels["Space Planning Zones"];
            var roomBoundaries = spacePlanningZones.AllElementsOfType<SpaceBoundary>();

            var model = new Model();
            var dataRack = DataRack.CabinetSiemonV800V82ADataCenterV82A48U;
            switch (input.CabinetDepth)
            {
                case DataHallInputsCabinetDepth._1000mm:
                    switch (input.CabinetHeight)
                    {
                        case DataHallInputsCabinetHeight._42U__2013mm_:
                            dataRack = DataRack.CabinetSiemonV800V81ADataCenterV81A42U;
                            break;
                        case DataHallInputsCabinetHeight._45U__2146mm_:
                            dataRack = DataRack.CabinetSiemonV800V81ADataCenterV81A45U;
                            break;
                        case DataHallInputsCabinetHeight._48U__2280mm_:
                            dataRack = DataRack.CabinetSiemonV800V81ADataCenterV81A48U;
                            break;
                    }
                    break;
                case DataHallInputsCabinetDepth._1200mm:
                    switch (input.CabinetHeight)
                    {
                        case DataHallInputsCabinetHeight._42U__2013mm_:
                            dataRack = DataRack.CabinetSiemonV800V82ADataCenterV82A42U;
                            break;
                        case DataHallInputsCabinetHeight._45U__2146mm_:
                            dataRack = DataRack.CabinetSiemonV800V82ADataCenterV82A45U;
                            break;
                        case DataHallInputsCabinetHeight._48U__2280mm_:
                            dataRack = DataRack.CabinetSiemonV800V82ADataCenterV82A48U;
                            break;
                    }
                    break;
            }
            var width = dataRack.BoundingBox.Max.X - dataRack.BoundingBox.Min.X;
            var depth = dataRack.BoundingBox.Max.Y - dataRack.BoundingBox.Min.Y;
            var totalArea = 0.0;
            foreach (var room in roomBoundaries)
            {
                var profile = room.Boundary;
                totalArea += profile.Area();
                //inset from walls
                var inset = profile.Perimeter.Offset(-1.2);
                var longestEdge = inset.SelectMany(s => s.Segments()).OrderBy(l => l.Length()).Last();
                var alignment = new Transform(Vector3.Origin, longestEdge.Direction(), Vector3.ZAxis);
                var grid = new Grid2d(inset, alignment);
                grid.U.DivideByPattern(new[] { ("Forward Rack", depth), ("Hot Aisle", input.HotAisleWidth), ("Backward Rack", depth), ("Cold Aisle", input.ColdAisleWidth) });
                grid.V.DivideByFixedLength(width);
                var floorGrid = new Grid2d(profile.Perimeter, alignment);
                floorGrid.U.DivideByFixedLength(0.6096);
                floorGrid.V.DivideByFixedLength(0.6096);
                model.AddElements(floorGrid.ToModelCurves(room.Transform));

                foreach (var cell in grid.GetCells())
                {
                    var cellRect = cell.GetCellGeometry() as Polygon;
                    if (cell.IsTrimmed() || cell.Type == null || cell.GetTrimmedCellGeometry().Count() == 0)
                    {
                        continue;
                    }
                    if (cell.Type == "Hot Aisle")
                    {
                        model.AddElement(new Panel(cellRect, BuiltInMaterials.XAxis, room.Transform));
                    }
                    else if (cell.Type == "Cold Aisle")
                    {
                        model.AddElement(new Panel(cellRect, BuiltInMaterials.ZAxis, room.Transform));
                    }
                    else if (cell.Type == "Forward Rack" && cellRect.Area().ApproximatelyEquals(width * depth, 0.01))
                    {
                        var centroid = cellRect.Centroid();
                        var rackInstance = dataRack.CreateInstance(alignment.Concatenated(new Transform(new Vector3(), -90)).Concatenated(new Transform(centroid)).Concatenated(room.Transform), "Rack");
                        model.AddElement(rackInstance);
                    }
                    else if (cell.Type == "Backward Rack" && cellRect.Area().ApproximatelyEquals(width * depth, 0.01))
                    {
                        var centroid = cellRect.Centroid();
                        var rackInstance = dataRack.CreateInstance(alignment.Concatenated(new Transform(new Vector3(), 90)).Concatenated(new Transform(centroid)).Concatenated(room.Transform), "Rack");
                        model.AddElement(rackInstance);
                    }
                }
            }

            var rackCount = model.AllElementsOfType<ElementInstance>().Count();
            var areaInSf = totalArea * 10.7639;
            var density = input.KWRack * rackCount / areaInSf;
            var output = new DataHallOutputs(rackCount, $"{density * 1000:0} watts/sf");
            output.Model = model;
            return output;
        }
    }
}