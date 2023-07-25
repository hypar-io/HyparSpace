using Elements;
using Elements.Geometry;
using Elements.Spatial;
using System.Collections.Generic;
using System.Linq;

namespace DataHallLayout
{
    public static class DataHallLayout
    {
        /// <summary>
        /// The DataHallLayout function.
        /// </summary>
        /// <param name="model">The input model.</param>
        /// <param name="input">The arguments to the execution.</param>
        /// <returns>A DataHallLayoutOutputs instance containing computed results and the model with any new elements.</returns>
        public static DataHallLayoutOutputs Execute(Dictionary<string, Model> inputModels, DataHallLayoutInputs input)
        {
            Elements.Serialization.glTF.GltfExtensions.UseReferencedContentExtension = true;
            var spacePlanningZones = inputModels["Space Planning Zones"];
            var roomBoundaries = spacePlanningZones.AllElementsOfType<SpaceBoundary>().Where(b => b.Name == "Data Hall");

            var model = new Model();
            var warnings = new List<string>();
            var dataRack = DataRack.CabinetSiemonV800V82ADataCenterV82A48U;
            switch (input.CabinetDepth)
            {
                case DataHallLayoutInputsCabinetDepth._1000mm:
                    switch (input.CabinetHeight)
                    {
                        case DataHallLayoutInputsCabinetHeight._42U__2013mm_:
                            dataRack = DataRack.CabinetSiemonV800V81ADataCenterV81A42U;
                            break;
                        case DataHallLayoutInputsCabinetHeight._45U__2146mm_:
                            dataRack = DataRack.CabinetSiemonV800V81ADataCenterV81A45U;
                            break;
                        case DataHallLayoutInputsCabinetHeight._48U__2280mm_:
                            dataRack = DataRack.CabinetSiemonV800V81ADataCenterV81A48U;
                            break;
                    }
                    break;
                case DataHallLayoutInputsCabinetDepth._1200mm:
                    switch (input.CabinetHeight)
                    {
                        case DataHallLayoutInputsCabinetHeight._42U__2013mm_:
                            dataRack = DataRack.CabinetSiemonV800V82ADataCenterV82A42U;
                            break;
                        case DataHallLayoutInputsCabinetHeight._45U__2146mm_:
                            dataRack = DataRack.CabinetSiemonV800V82ADataCenterV82A45U;
                            break;
                        case DataHallLayoutInputsCabinetHeight._48U__2280mm_:
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
                var inset = profile.Perimeter.Offset(-input.Clearance);
                Line alignmentEdge = null;
                try
                {
                    if (input.FlipDirection)
                    {
                        alignmentEdge = inset.SelectMany(s => s.Segments()).OrderByDescending(l => l.Length()).Last();
                    }
                    else
                    {
                        alignmentEdge = inset.SelectMany(s => s.Segments()).OrderBy(l => l.Length()).Last();
                    }
                }
                catch
                {
                    warnings.Add("One space was too small for a data hall.");
                    continue;
                }
                var alignment = new Transform(Vector3.Origin, alignmentEdge.Direction(), Vector3.ZAxis);
                var grid = new Grid2d(inset, alignment);

                grid.U.DivideByFixedLength(width);

                if (input.SwapColdHotPattern)
                {
                    grid.V.DivideByPattern(new[] { ("Forward Rack", depth), ("Cold Aisle", input.ColdAisleWidth), ("Backward Rack", depth), ("Hot Aisle", input.HotAisleWidth) });
                }
                else
                {
                    grid.V.DivideByPattern(new[] { ("Forward Rack", depth), ("Hot Aisle", input.HotAisleWidth), ("Backward Rack", depth), ("Cold Aisle", input.ColdAisleWidth) });
                }

                var floorGrid = new Grid2d(profile.Perimeter, alignment);
                floorGrid.U.DivideByFixedLength(0.6096);
                floorGrid.V.DivideByFixedLength(0.6096);
                model.AddElement(ToModelLines(floorGrid, room.Transform));

                double rackAngle = 0;
                if (input.SwapColdHotPattern) rackAngle = 180;

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
                        var rackInstance = dataRack.CreateInstance(alignment.Concatenated(new Transform(new Vector3(), rackAngle - 180)).Concatenated(new Transform(centroid)).Concatenated(room.Transform), "Rack");
                        model.AddElement(rackInstance);
                    }
                    else if (cell.Type == "Backward Rack" && cellRect.Area().ApproximatelyEquals(width * depth, 0.01))
                    {
                        var centroid = cellRect.Centroid();
                        var rackInstance = dataRack.CreateInstance(alignment.Concatenated(new Transform(new Vector3(), rackAngle)).Concatenated(new Transform(centroid)).Concatenated(room.Transform), "Rack");
                        model.AddElement(rackInstance);
                    }
                }
            }

            var rackCount = model.AllElementsOfType<ElementInstance>().Count();
            var areaInSf = totalArea * 10.7639;
            var density = input.KWRack * rackCount / areaInSf;
            var output = new DataHallLayoutOutputs(rackCount, $"{density * 1000:0} watts/sf");
            output.Model = model;
            output.Warnings.AddRange(warnings);
            return output;
        }

        private static ModelLines ToModelLines(Grid2d grid, Transform transform)
        {
            var boundary = grid.GetTrimmedCellGeometry();
            var uLines = grid.GetCellSeparators(GridDirection.U, true);
            var vLines = grid.GetCellSeparators(GridDirection.V, true);
            var curves = boundary.Concat(uLines).Concat(vLines).OfType<BoundedCurve>();

            ModelLines ml = new ModelLines(transform: transform);
            foreach (var b in boundary.Concat(curves))
            {
                var parameters = b.GetSubdivisionParameters();
                var points = parameters.Select(p => b.PointAt(p)).ToList();
                for (int i = 0; i < points.Count - 1; i++)
                {
                    ml.Lines.Add(new Line(points[i], points[i + 1]));
                }
            }
            return ml;
        }
    }
}