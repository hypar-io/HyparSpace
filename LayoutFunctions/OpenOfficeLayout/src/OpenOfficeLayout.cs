using Elements;
using Elements.Geometry;
using Elements.Spatial;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Elements.Components;

namespace OpenOfficeLayout
{
    public static class OpenOfficeLayout
    {
        /// <summary>
        /// The OpenOfficeLayout function.
        /// </summary>
        /// <param name="model">The input model.</param>
        /// <param name="input">The arguments to the execution.</param>
        /// <returns>A OpenOfficeLayoutOutputs instance containing computed results and the model with any new elements.</returns>
        public static OpenOfficeLayoutOutputs Execute(Dictionary<string, Model> inputModels, OpenOfficeLayoutInputs input)
        {
            var spacePlanningZones = inputModels["Space Planning Zones"];
            var levels = spacePlanningZones.AllElementsOfType<LevelElements>();
            var deskCount = 0;

            var configJson = File.ReadAllText("OpenOfficeConfigurations.json");
            var configs = JsonConvert.DeserializeObject<SpaceConfiguration>(configJson);
            var firstConfig = configs[Hypar.Model.Utilities.GetStringValueFromEnum(input.DeskType)];

            var desksPerInstance = input.DeskType == OpenOfficeLayoutInputsDeskType.Enclosed_Pair || input.DeskType == OpenOfficeLayoutInputsDeskType.Double_Desk ? 2 : 1;

            var output = new OpenOfficeLayoutOutputs();
            foreach (var lvl in levels)
            {
                var corridors = lvl.Elements.OfType<Floor>();
                var corridorSegments = corridors.SelectMany(p => p.Profile.Segments());
                var officeBoundaries = lvl.Elements.OfType<SpaceBoundary>().Where(z => z.Name == "Open Office");
                foreach (var ob in officeBoundaries)
                {
                    var spaceBoundary = ob.Boundary;
                    Line orientationGuideEdge = FindEdgeAdjacentToCorridor(spaceBoundary.Perimeter, corridorSegments);
                    var orientationTransform = new Transform(Vector3.Origin, orientationGuideEdge.Direction(), Vector3.ZAxis);
                    var boundaryCurves = new List<Polygon>();
                    boundaryCurves.Add(spaceBoundary.Perimeter);
                    boundaryCurves.AddRange(spaceBoundary.Voids ?? new List<Polygon>());
                    Grid2d grid;
                    try
                    {
                        grid = new Grid2d(boundaryCurves, orientationTransform);
                    }
                    catch
                    {
                        Console.WriteLine("Something went wrong creating a grid.");
                        continue;
                    }
                    var aisleWidth = 1.0;
                    grid.V.DivideByPattern(new[] { ("Desk", firstConfig.Width), ("Desk", firstConfig.Width), ("Desk", firstConfig.Width), ("Desk", firstConfig.Width), ("Aisle", aisleWidth) }, PatternMode.Cycle, FixedDivisionMode.RemainderAtBothEnds);
                    var mainVPattern = new[] { ("Aisle", aisleWidth), ("Forward", firstConfig.Depth), ("Backward", firstConfig.Depth) };
                    var nonMirroredVPattern = new[] { ("Forward", firstConfig.Depth), ("Aisle", aisleWidth) };
                    var pattern = input.DeskType == OpenOfficeLayoutInputsDeskType.Double_Desk ? nonMirroredVPattern : mainVPattern;
                    grid.U.DivideByPattern(pattern, PatternMode.Cycle, FixedDivisionMode.RemainderAtBothEnds);
                    var random = new Random();
                    foreach (var cell in grid.GetCells())
                    {
                        try
                        {
                            if ((cell.Type?.Contains("Desk") ?? true) && !cell.IsTrimmed() && cell.GetTrimmedCellGeometry().Count() > 0)
                            {
                                if (cell.Type?.Contains("Backward") ?? false)
                                {
                                    // output.Model.AddElement(cell.GetCellGeometry());
                                    var cellGeo = cell.GetCellGeometry() as Polygon;
                                    var transform = orientationTransform.Concatenated(new Transform(Vector3.Origin, -90)).Concatenated(new Transform(cellGeo.Vertices[3])).Concatenated(ob.Transform);
                                    output.Model.AddElements(firstConfig.Instantiate(transform));
                                    deskCount += desksPerInstance;
                                }
                                else if (cell.Type?.Contains("Forward") ?? false)
                                {
                                    // output.Model.AddElement(cell.GetCellGeometry());
                                    var cellGeo = cell.GetCellGeometry() as Polygon;
                                    var transform = orientationTransform.Concatenated(new Transform(Vector3.Origin, 90)).Concatenated(new Transform(cellGeo.Vertices[1])).Concatenated(ob.Transform);
                                    // output.Model.AddElement(new ModelCurve(rect, BuiltInMaterials.XAxis, transform));
                                    output.Model.AddElements(firstConfig.Instantiate(transform));
                                    deskCount += desksPerInstance;
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e.Message);
                        }
                    }
                }
            }

            InstancePositionOverrides(input.Overrides, output.Model);
            var model = output.Model;
            var outputWithData = new OpenOfficeLayoutOutputs(deskCount);
            outputWithData.Model = model;

            return outputWithData;
        }

        private static void InstancePositionOverrides(Overrides overrides, Model model)
        {
            var allElementInstances = model.AllElementsOfType<ElementInstance>();
            var pointTranslations = allElementInstances.Select(ei => ei.Transform.Origin).Distinct().Select(t => new PointTranslation(t, t, new Transform(), null, null, false, Guid.NewGuid(), null)).ToList();
            if (overrides != null)
            {
                foreach (var positionOverride in overrides.FurnitureLocations)
                {
                    var thisOriginalLocation = positionOverride.Identity.OriginalLocation;
                    var thisPt = positionOverride.Value.Location;
                    thisPt.Z = thisOriginalLocation.Z;
                    var nearInstances = allElementInstances.Where(ei => ei.Transform.Origin.DistanceTo(thisOriginalLocation) < 0.01);
                    nearInstances.ToList().ForEach(ni => ni.Transform.Concatenate(new Transform(thisPt.X - ni.Transform.Origin.X, thisPt.Y - ni.Transform.Origin.Y, 0)));
                    // should only be one
                    var nearTranslations = pointTranslations.Where(pt => pt.OriginalLocation.DistanceTo(thisOriginalLocation) < 0.01);
                    nearTranslations.ToList().ForEach(nt => nt.Location = thisPt);
                }

            }
            model.AddElements(pointTranslations);
        }

        private static Line FindEdgeAdjacentToCorridor(Polygon perimeter, IEnumerable<Line> corridorSegments)
        {
            var minDist = double.MaxValue;
            var minSeg = perimeter.Segments()[0];
            foreach (var edge in perimeter.Segments())
            {
                var midpt = edge.PointAt(0.5);
                foreach (var seg in corridorSegments)
                {
                    var dist = midpt.DistanceTo(seg);
                    // if two segments are basically the same distance to the corridor segment,
                    // prefer the longer one. 
                    if (Math.Abs(dist - minDist) < 0.1)
                    {
                        minDist = dist;
                        if (minSeg.Length() < edge.Length())
                        {
                            minSeg = edge;
                        }
                    }
                    else if (dist < minDist)
                    {
                        minDist = dist;
                        minSeg = edge;
                    }
                }
            }
            return minSeg;
        }
    }
}