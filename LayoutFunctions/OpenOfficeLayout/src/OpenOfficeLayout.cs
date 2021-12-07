using Elements;
using Elements.Geometry;
using Elements.Spatial;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Elements.Components;
using Newtonsoft.Json.Linq;
using Elements.Geometry.Solids;
using LayoutFunctionCommon;

namespace OpenOfficeLayout
{
    public static class OpenOfficeLayout
    {
        static string[] _columnSources = new[] { "Columns", "Structure" };

        /// <summary>
        /// The OpenOfficeLayout function.
        /// </summary>
        /// <param name="model">The input model.</param>
        /// <param name="input">The arguments to the execution.</param>
        /// <returns>A OpenOfficeLayoutOutputs instance containing computed results and the model with any new elements.</returns>
        public static OpenOfficeLayoutOutputs Execute(Dictionary<string, Model> inputModels, OpenOfficeLayoutInputs input)
        {
            var catalog = JsonConvert.DeserializeObject<ContentCatalog>(File.ReadAllText("./catalog.json"));

            var spacePlanningZones = inputModels["Space Planning Zones"];
            var levels = spacePlanningZones.AllElementsOfType<LevelElements>();

            var deskCount = 0;

            var configJson = File.ReadAllText("OpenOfficeDeskConfigurations.json");
            var configs = JsonConvert.DeserializeObject<SpaceConfiguration>(configJson);

            if (input.CustomWorkstationProperties == null)
            {
                input.CustomWorkstationProperties = new CustomWorkstationProperties(2, 2);
            }
            configs["Custom"] = new ContentConfiguration()
            {
                CellBoundary = new ContentConfiguration.BoundaryDefinition()
                {
                    Min = (0, 0, 0),
                    Max = (input.CustomWorkstationProperties.Width, input.CustomWorkstationProperties.Length, 0)
                },
                ContentItems = new List<ContentConfiguration.ContentItem>()
            };

            var defaultCustomDesk = new CustomWorkstation(input.CustomWorkstationProperties.Width, input.CustomWorkstationProperties.Length);

            var defaultConfig = configs[Hypar.Model.Utilities.GetStringValueFromEnum(input.DeskType)];

            var desksPerInstance = input.DeskType == OpenOfficeLayoutInputsDeskType.Enclosed_Pair || input.DeskType == OpenOfficeLayoutInputsDeskType.Double_Desk ? 2 : 1;

            var output = new OpenOfficeLayoutOutputs();

            var overridesByCentroid = new Dictionary<Guid, SpaceSettingsOverride>();
            foreach (var spaceOverride in input.Overrides?.SpaceSettings ?? new List<SpaceSettingsOverride>())
            {
                var matchingBoundary =
                levels.SelectMany(l => l.Elements)
                    .OfType<SpaceBoundary>()
                    .OrderBy(ob => ob.ParentCentroid.Value
                    .DistanceTo(spaceOverride.Identity.ParentCentroid))
                    .First();

                if (overridesByCentroid.ContainsKey(matchingBoundary.Id))
                {
                    var mbCentroid = matchingBoundary.ParentCentroid.Value;
                    if (overridesByCentroid[matchingBoundary.Id].Identity.ParentCentroid.DistanceTo(mbCentroid) > spaceOverride.Identity.ParentCentroid.DistanceTo(mbCentroid))
                    {
                        overridesByCentroid[matchingBoundary.Id] = spaceOverride;
                    }
                }
                else
                {
                    overridesByCentroid.Add(matchingBoundary.Id, spaceOverride);
                }
            }

            // Get column locations from model
            List<(Vector3, Profile)> modelColumnLocations = new List<(Vector3, Profile)>();
            foreach (var source in _columnSources)
            {
                if (inputModels.TryGetValue(source, out var sourceData))
                {
                    modelColumnLocations.AddRange(GetColumnLocations(sourceData));
                }
            }
            SearchablePointCollection<Profile> columnSearchTree = new SearchablePointCollection<Profile>(modelColumnLocations);

            int desksSkippedTotal = 0;

            foreach (var lvl in levels)
            {
                var corridors = lvl.Elements.OfType<Floor>();
                var corridorSegments = corridors.SelectMany(p => p.Profile.Segments());
                var officeBoundaries = lvl.Elements.OfType<SpaceBoundary>().Where(z => z.Name == "Open Office");
                foreach (var ob in officeBoundaries)
                {
                    // create a boundary we can use to override individual groups of desks. It's sunk slightly so that, if floors are on, you don't see it. 
                    var overridableBoundary = new SpaceBoundary()
                    {
                        Boundary = ob.Boundary,
                        Cells = ob.Cells,
                        Area = ob.Area,
                        Transform = ob.Transform.Concatenated(new Transform(0, 0, -0.05)),
                        Material = ob.Material,
                        Representation = new Lamina(ob.Boundary.Perimeter, false),
                        Name = "DeskArea",
                    };
                    overridableBoundary.ParentCentroid = ob.ParentCentroid;
                    overridableBoundary.AdditionalProperties.Add("Desk Type", Hypar.Model.Utilities.GetStringValueFromEnum(input.DeskType));
                    overridableBoundary.AdditionalProperties.Add("Integrated Collaboration Space Density", input.IntegratedCollaborationSpaceDensity);
                    overridableBoundary.AdditionalProperties.Add("Grid Rotation", input.GridRotation);
                    output.Model.AddElement(overridableBoundary);

                    var selectedConfig = defaultConfig;
                    var rotation = input.GridRotation;
                    var collabDensity = input.IntegratedCollaborationSpaceDensity;
                    var customDesk = defaultCustomDesk;
                    var isCustom = input.DeskType == OpenOfficeLayoutInputsDeskType.Custom;
                    if (overridesByCentroid.ContainsKey(ob.Id))
                    {
                        var spaceOverride = overridesByCentroid[ob.Id];
                        isCustom = spaceOverride.Value.DeskType == SpaceSettingsValueDeskType.Custom;
                        if (isCustom)
                        {
                            selectedConfig = new ContentConfiguration()
                            {
                                CellBoundary = new ContentConfiguration.BoundaryDefinition()
                                {
                                    Min = (0, 0, 0),
                                    Max = (spaceOverride.Value.CustomWorkstationProperties.Width, spaceOverride.Value.CustomWorkstationProperties.Length, 0)
                                },
                                ContentItems = new List<ContentConfiguration.ContentItem>()
                            };
                            customDesk = new CustomWorkstation(spaceOverride.Value.CustomWorkstationProperties.Width, spaceOverride.Value.CustomWorkstationProperties.Length);
                            Identity.AddOverrideIdentity(ob, spaceOverride);
                        }
                        else
                        {
                            selectedConfig = configs[Hypar.Model.Utilities.GetStringValueFromEnum(spaceOverride.Value.DeskType)];
                        }
                        overridableBoundary.AdditionalProperties["Desk Type"] = Hypar.Model.Utilities.GetStringValueFromEnum(spaceOverride.Value.DeskType);
                        overridableBoundary.AdditionalProperties["Integrated Collaboration Space Density"] = spaceOverride.Value.IntegratedCollaborationSpaceDensity;
                        overridableBoundary.AdditionalProperties["Grid Rotation"] = spaceOverride.Value.GridRotation;
                        rotation = spaceOverride.Value.GridRotation;
                        collabDensity = spaceOverride.Value.IntegratedCollaborationSpaceDensity;
                    }

                    var spaceBoundary = ob.Boundary;
                    Line orientationGuideEdge = FindEdgeAdjacentToCorridor(spaceBoundary.Perimeter, corridorSegments);
                    var dir = orientationGuideEdge.Direction();
                    if (rotation != 0)
                    {
                        var gridRotation = new Transform();
                        gridRotation.Rotate(Vector3.ZAxis, rotation);
                        dir = gridRotation.OfVector(dir);
                    }
                    var orientationTransform = new Transform(Vector3.Origin, dir, Vector3.ZAxis);
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

                    var aisleWidth = 1.0; // LJ
                    grid.V.DivideByPattern(
                        new[] {
                            ("Desk", selectedConfig.Width),
                            ("Desk", selectedConfig.Width),
                            ("Desk", selectedConfig.Width),
                            ("Desk", selectedConfig.Width),
                            ("Aisle", aisleWidth) },
                        PatternMode.Cycle,
                        FixedDivisionMode.RemainderAtBothEnds);

                    var mainVPattern = new[] {
                        ("Aisle", aisleWidth),
                        ("Forward", selectedConfig.Depth),
                        ("Backward", selectedConfig.Depth)
                    };
                    var nonMirroredVPattern = new[] {
                        ("Forward", selectedConfig.Depth),
                        ("Aisle", aisleWidth) };

                    var chosenDeskAislePattern = input.DeskType == OpenOfficeLayoutInputsDeskType.Double_Desk ? nonMirroredVPattern : mainVPattern;

                    grid.U.DivideByPattern(chosenDeskAislePattern, PatternMode.Cycle, FixedDivisionMode.RemainderAtBothEnds);

                    // Insert interstitial collab spaces
                    if (collabDensity > 0.0)
                    {
                        var spaceEveryURows = 4;
                        var numDesksToConsume = 1;

                        if (collabDensity >= 0.3)
                        {
                            spaceEveryURows = 3;
                        }
                        if (collabDensity >= 0.5)
                        {
                            numDesksToConsume = 2;
                        }
                        if (collabDensity >= 0.7)
                        {
                            spaceEveryURows = 2;
                        }
                        if (collabDensity >= 0.8)
                        {
                            numDesksToConsume = 3;
                        }
                        if (collabDensity >= 0.9)
                        {
                            numDesksToConsume = 4;
                        }

                        var colCounter = 0;
                        for (int j = 0; j < grid.V.Cells.Count; j++)
                        {
                            var colType = grid.V[j].Type;
                            var rowCounter = 0;
                            for (int i = 0; i < grid.U.Cells.Count; i++)
                            {
                                var rowType = grid.U[i].Type;
                                var cell = grid[i, j];
                                if (
                                    rowCounter % spaceEveryURows == 0 &&
                                    (rowType == "Forward" || rowType == "Backward") &&
                                    colType == "Desk" &&
                                    colCounter < numDesksToConsume
                                    )
                                {
                                    cell.Type = "Collab Space";
                                }
                                if (rowType == "Aisle")
                                {
                                    rowCounter++;
                                }
                            }
                            if (colType == "Desk")
                            {
                                colCounter++;
                            }
                            else if (colType == "Aisle")
                            {
                                colCounter = 0;
                            }
                        }
                    }
                    // output.Model.AddElements(grid.ToModelCurves());
                    foreach (var cell in grid.GetCells())
                    {
                        try
                        {
                            if ((cell.Type?.Contains("Desk") ?? true) && !cell.IsTrimmed())
                            {
                                var cellGeo = cell.GetCellGeometry() as Polygon;
                                var cellBounds = cellGeo.Bounds();

                                // Get closest columns from cell location
                                var nearbyColumns = columnSearchTree.FindWithinBounds(cellBounds, 0.3, 2).ToList();
                                var columnProfilesCollection =
                                    nearbyColumns.Select(c => columnSearchTree.GetElementsAtPoint(c))
                                                 .Select(e => e.FirstOrDefault());
                                if (
                                    nearbyColumns.Any(
                                        c => cellGeo.Contains(c)) ||
                                        columnProfilesCollection.Any(cp => cp != null && cp.Perimeter.Intersects(cellGeo)))
                                {
                                    desksSkippedTotal++;
                                    continue;
                                }
                                if (cell.Type?.Contains("Backward") ?? false)
                                {
                                    // output.Model.AddElement(cell.GetCellGeometry());
                                    var transform =
                                    orientationTransform
                                        .Concatenated(new Transform(Vector3.Origin, -90))
                                        .Concatenated(new Transform(cellGeo.Vertices[3]))
                                        .Concatenated(ob.Transform);
                                    if (isCustom)
                                    {
                                        output.Model.AddElement(customDesk.CreateInstance(transform, null));
                                    }
                                    else
                                    {
                                        output.Model.AddElements(selectedConfig.Instantiate(transform));
                                    }
                                    // output.Model.AddElement(new ModelCurve(cellGeo, BuiltInMaterials.YAxis));
                                    deskCount += desksPerInstance;
                                }
                                else if (cell.Type?.Contains("Forward") ?? false)
                                {
                                    // output.Model.AddElement(cell.GetCellGeometry());

                                    var transform =
                                    orientationTransform
                                        .Concatenated(new Transform(Vector3.Origin, 90))
                                        .Concatenated(new Transform(cellGeo.Vertices[1]))
                                        .Concatenated(ob.Transform);

                                    // output.Model.AddElement(new ModelCurve(cellGeo, BuiltInMaterials.XAxis));
                                    if (isCustom)
                                    {
                                        output.Model.AddElement(customDesk.CreateInstance(transform, null));
                                    }
                                    else
                                    {
                                        output.Model.AddElements(selectedConfig.Instantiate(transform));
                                    }
                                    deskCount += desksPerInstance;
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e.Message);
                            Console.WriteLine(e.StackTrace);
                        }
                    }

                    var collabSpaceCells = grid.GetCells()
                        .Where(c => !c.IsTrimmed() && c.Type?.Contains("Collab Space") == true)
                        .Select(c => new Profile(c.GetCellGeometry() as Polygon));

                    var union = Profile.UnionAll(collabSpaceCells);
                    foreach (var profile in union)
                    {
                        var sb = SpaceBoundary.Make(profile, "Open Collaboration", ob.Transform.Concatenated(new Transform(0, 0, -0.03)), 3, profile.Perimeter.Centroid(), profile.Perimeter.Centroid());
                        sb.Representation = new Representation(new[] { new Lamina(profile.Perimeter, false) });
                        sb.AdditionalProperties.Add("Parent Level Id", lvl.Id);
                        output.Model.AddElement(sb);
                    }
                }
            }

            OverrideUtilities.InstancePositionOverrides(input.Overrides, output.Model);
            var model = output.Model;

            model.AddElement(new WorkpointCount() { Count = deskCount, Type = "Desk" });
            var outputWithData = new OpenOfficeLayoutOutputs(deskCount);
            outputWithData.Model = model;
            return outputWithData;
        }

        public static IEnumerable<(Vector3, Profile)> GetColumnLocations(Model m)
        {
            if (m == null) { throw new Exception("Model provided was null."); }
            foreach (var ge in m.AllElementsOfType<Column>())
            {
                if (!ge.IsElementDefinition)
                {
                    yield return (ge.Location, ge.ProfileTransformed());
                }
                else
                {
                    Vector3 geOrigin = ge.Location;
                    foreach (var e in m.AllElementsOfType<ElementInstance>().Where(e => e.BaseDefinition == ge))
                    {
                        yield return (e.Transform.OfPoint(geOrigin), ge.Profile.Transformed(e.Transform));
                    }
                }
            }
            yield break;
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