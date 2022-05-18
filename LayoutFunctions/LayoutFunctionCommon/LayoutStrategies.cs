using Elements;
using Elements.Components;
using Elements.Geometry;
using Elements.Spatial;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LayoutFunctionCommon
{
    public static class LayoutStrategies
    {
        /// <summary>
        /// Instantiate a space by finding the largest space that will fit from a SpaceConfiguration.
        /// </summary>
        /// <param name="configs">The configuration containing all possible space arrangements</param>
        /// <param name="width">The width of the space to fill</param>
        /// <param name="length">The length of the space to fill</param>
        /// <param name="rectangle">The more-or-less rectangular polygon to fill</param>
        /// <param name="xform">A transform to apply to the rectangle.</param>
        /// <returns></returns>
        public static ComponentInstance InstantiateLayoutByFit(SpaceConfiguration configs, double width, double length, Polygon rectangle, Transform xform)
        {
            ContentConfiguration selectedConfig = null;
            var orderedKeys = configs.OrderByDescending(kvp => kvp.Value.CellBoundary.Depth * kvp.Value.CellBoundary.Width).Select(kvp => kvp.Key);
            foreach (var key in orderedKeys)
            {
                var config = configs[key];
                if (config.CellBoundary.Width < width && config.CellBoundary.Depth < length)
                {
                    selectedConfig = config;
                    break;
                }
            }
            if (selectedConfig == null)
            {
                return null;
            }
            var baseRectangle = Polygon.Rectangle(selectedConfig.CellBoundary.Min, selectedConfig.CellBoundary.Max);
            var rules = selectedConfig.Rules();

            var componentDefinition = new ComponentDefinition(rules, selectedConfig.Anchors());
            var instance = componentDefinition.Instantiate(ContentConfiguration.AnchorsFromRect(rectangle.TransformedPolygon(xform)));
            var allPlacedInstances = instance.Instances;
            return instance;
        }

        /// <summary>
        /// Instantiate a space by finding the largest space that will fit a grid cell from a SpaceConfiguration.
        /// </summary>
        /// <param name="configs">The configuration containing all possible space arrangements.</param>
        /// <param name="width">The 2d grid cell to fill.</param>
        /// <param name="xform">A transform to apply to the rectangle.</param>
        /// <returns></returns>
        public static ComponentInstance InstantiateLayoutByFit(SpaceConfiguration configs, Grid2d cell, Transform xform)
        {
            var rect = cell.GetCellGeometry() as Polygon;
            var segs = rect.Segments();
            var width = segs[0].Length();
            var depth = segs[1].Length();
            var trimmedGeo = cell.GetTrimmedCellGeometry();
            if (!cell.IsTrimmed() && trimmedGeo.Count() > 0)
            {
                return InstantiateLayoutByFit(configs, width, depth, rect, xform);
            }
            else if (trimmedGeo.Count() > 0)
            {
                var largestTrimmedShape = trimmedGeo.OfType<Polygon>().OrderBy(s => s.Area()).Last();
                var cinchedVertices = rect.Vertices.Select(v => largestTrimmedShape.Vertices.OrderBy(v2 => v2.DistanceTo(v)).First()).ToList();
                var cinchedPoly = new Polygon(cinchedVertices);
                return InstantiateLayoutByFit(configs, width, depth, cinchedPoly, xform);
            }
            return null;
        }

        public static void StandardLayoutOnAllLevels<TLevelElements, TSpaceBoundary>(string programTypeName, Dictionary<string, Model> inputModels, dynamic overrides, Model outputModel, string configurationsPath, string catalogPath = "catalog.json") where TLevelElements : Element, ILevelElements where TSpaceBoundary : ISpaceBoundary
        {
            ContentCatalogRetrieval.SetCatalogFilePath(catalogPath);
            var spacePlanningZones = inputModels["Space Planning Zones"];
            var levels = spacePlanningZones.AllElementsAssignableFromType<TLevelElements>();
            var configJson = File.ReadAllText(configurationsPath);
            var configs = JsonConvert.DeserializeObject<SpaceConfiguration>(configJson);
            foreach (var lvl in levels)
            {
                var corridors = lvl.Elements.OfType<Floor>();
                var corridorSegments = corridors.SelectMany(p => p.Profile.Segments());
                var roomBoundaries = lvl.Elements.OfType<TSpaceBoundary>().Where(z => z.Name == programTypeName);
                foreach (var room in roomBoundaries)
                {
                    var spaceBoundary = room.Boundary;
                    Line orientationGuideEdge = WallGeneration.FindEdgeAdjacentToSegments(spaceBoundary.Perimeter.Segments(), corridorSegments, out var wallCandidates);

                    var orientationTransform = new Transform(Vector3.Origin, orientationGuideEdge.Direction(), Vector3.ZAxis);
                    var boundaryCurves = new List<Polygon>();
                    boundaryCurves.Add(spaceBoundary.Perimeter);
                    boundaryCurves.AddRange(spaceBoundary.Voids ?? new List<Polygon>());

                    var grid = new Grid2d(boundaryCurves, orientationTransform);
                    foreach (var cell in grid.GetCells())
                    {
                        var layout = InstantiateLayoutByFit(configs, cell, room.Transform);
                        if (layout != null)
                        {
                            outputModel.AddElement(layout);
                        }
                    }
                }
            }
            OverrideUtilities.InstancePositionOverrides(overrides, outputModel);
        }

    }
}