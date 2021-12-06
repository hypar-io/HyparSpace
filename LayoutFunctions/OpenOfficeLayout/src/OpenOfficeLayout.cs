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
        static string[] _columnSources = new []{ "Columns", "Structure" };

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
            List<Vector3> modelColumnLocations = new List<Vector3>();
            foreach(var source in _columnSources){
                if(inputModels.ContainsKey(source)){
                    var sourceData = inputModels[source];
                    modelColumnLocations.AddRange(GetColumnLocations(sourceData));
                }
            }
            SearchablePointCollection columnSearchTree = new SearchablePointCollection(modelColumnLocations);

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

                    var aisleWidth = 1.0;
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
                    int desksSkipped = 0;
                    // output.Model.AddElements(grid.ToModelCurves());
                    foreach (var cell in grid.GetCells())
                    {
                        try
                        {
                            if ((cell.Type?.Contains("Desk") ?? true) && !cell.IsTrimmed())
                            {
                                // Get closest columns from cell location
                                var cellGeo = cell.GetCellGeometry() as Polygon;
                                var cellBounds = cellGeo.Bounds();
                                if(columnSearchTree.HasPointInBounds(cellBounds, 0.3, 2)){
                                    // Column found at locatoin
                                    desksSkipped++;
                                    continue;
                                }
                                // if(columnSearchTree.FindClosest())

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

        public static IEnumerable<Vector3> GetColumnLocations(Model m) {
            if(m == null){ throw new Exception("Model provided was null."); }
            foreach(var ge in m.AllElementsOfType<Column>()){
                if(!ge.IsElementDefinition){
                    yield return ge.Location;
                }
                else{
                    Vector3 geOrigin =  ge.Location;
                    foreach(var e in m.AllElementsOfType<ElementInstance>().Where(e => e.BaseDefinition == ge)){
                        yield return e.Transform.OfPoint(geOrigin);
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

    /// <summary>
    /// Fast way to retreive points within a range
    /// or near a location
    /// </summary>
    public class SearchablePointCollection {
        public int Count => _count;
        private int _count = 0;

        List<SortedDictionary<double, List<Vector3>>> _coords
         = new List<SortedDictionary<double, List<Vector3>>>();

         List<List<double>> _keys => __keys ?? (__keys = _coords.Select(c => c.Keys.ToList()).ToList());
         List<List<double>> __keys;

        public readonly int Dimensions;

        public SearchablePointCollection(IEnumerable<Vector3> points = null, int dimensions = 3){
            Dimensions = dimensions;
            for(int i = 0; i<dimensions; i++){
                _coords.Add(new SortedDictionary<double, List<Vector3>>());
                _keys.Add(new List<double>{});
            }
            if(points != null){
                foreach(var p in points){
                    Add(p);
                }
            }
        }

        public bool IsWithinTolerance(double a, double b, double tolerance){
            return Math.Abs(a-b) <= tolerance;
        }

        public IEnumerable<Vector3> FindWithinRange(List<(double,double)> bounds, double tolerance = 0){
            if(bounds == null || bounds.Count == 0){ yield break; }
            HashSet<Vector3> prev = null;
            for(int i = 0; i<bounds.Count; i++){
                List<Vector3> found = new List<Vector3>();
                int dMin = Math.Max(0,(~_keys[i].BinarySearch(bounds[i].Item1 - tolerance) - 1));
                int dMax = Math.Min(_keys[i].Count -1, ~_keys[i].BinarySearch(bounds[i].Item2 + tolerance));
                int j = dMin;
                while(j <= dMax){
                    double coord = _keys[i][j];
                    if(coord < (bounds[i].Item1 - tolerance)){ j++; continue; }
                    if(coord > (bounds[i].Item2 + tolerance)){ break;}
                    foreach(var potential in _coords[i][coord]){
                        found.Add(potential);
                    }
                    j++;
                }
                if(prev != null){
                    prev = prev.Intersect(found).ToHashSet();
                    // If no matches here, then there are no items within the provided range.
                }
                else{
                    prev = found.ToHashSet();
                }
                if(prev.Count == 0){ yield break;}
            }           
            foreach(var v in prev){
                yield return v;
            }
        }

        public bool HasPointInBounds(BBox3 bbox, double tolerance = 0, int dimensions = 3){
            List<(double,double)> bounds = new List<(double, double)>();
            for(int i =0; i<Math.Min(dimensions, 3); i++)
            {
                bounds.Add(GetDimensionalRange(i,bbox));
            }
            return FindWithinRange(bounds, tolerance).Any();
        }

        public Vector3 FindClosestPoint(Vector3 location, int dimensions = 2){
            List<Vector3> potential = new List<Vector3>();
            for(int d = 0; d<Math.Min(dimensions, Dimensions); d++){
                var dVal = GetDimensionalValue(d, location);
                int min = Math.Max(0,(~_keys[d].BinarySearch(dVal))-1);
                potential.AddRange(_coords[d][_keys[d][min]]);
            }
            return potential.OrderBy(p => p.DistanceTo(location)).First();
        }

        public IEnumerable<Vector3> FindClosestPoints(Vector3 location, double distance, int dimensions = 2) => FindWithinRange(Inflate(location,distance));     

        public List<(double,double)> Inflate(Vector3 center, double radius, int dimensions = 2){
            var output =  new List<(double, double)>();
            for(int d = 0; d<Math.Min(Dimensions,dimensions); d++){
                output.Add(
                    (GetDimensionalValue(d, center) - radius,
                     GetDimensionalValue(d, center) + radius)
                     );
            }
            return output;
        }

        public void Add(Vector3 point){
            for(int d = 0; d<Dimensions; d++){
                var dval = GetDimensionalValue(d,point);
                if(!_coords[d].ContainsKey(dval)){
                    _coords[d].Add(dval, new List<Vector3>(){ point });
                }
                else{ _coords[d][dval].Add(point); }
            }
            __keys = null;
            _count++;
        }

        public void Remove(Vector3 point){
            for(int d = 0; d<Dimensions; d++){
                var dval = GetDimensionalValue(d,point);
                if(!_coords[d].ContainsKey(dval)){
                    // Could not find
                    return;
                }
                else if(_coords[d][dval].Count == 1){
                    // Only point which has this dimensional value
                    _coords[d].Remove(dval);
                }
                else{
                    _coords[d][dval].Remove(point);
                }
            }
            __keys = null;
            _count--;
        }

        double GetDimensionalValue(int dim, Vector3 point){
            switch(dim){
                case 0:
                return point.X;
                case 1:
                return point.Y;
                case 2:
                return point.Z;
                default:
                throw new Exception($"Can't handle dimension {dim}");
            }
        }

        
        (double,double) GetDimensionalRange(int dim, BBox3 bounds){
            switch(dim){
                case 0:
                return (bounds.Min.X,bounds.Max.X);
                case 1:
                return (bounds.Min.Y,bounds.Max.Y);
                case 2:
                return (bounds.Min.Z,bounds.Max.Z);
                default:
                throw new Exception($"Can't handle dimension {dim}");
            }
        }
    }


}