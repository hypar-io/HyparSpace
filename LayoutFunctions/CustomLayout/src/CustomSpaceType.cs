using Elements;
using Elements.Geometry;
using Elements.Components;
using System.Collections.Generic;
using System.Linq;
using System;
using Elements.Spatial;
using System.Net;
using Newtonsoft.Json;
using Microsoft.VisualBasic.CompilerServices;
using LayoutFunctionCommon;

namespace CustomSpaceType
{
    public static class CustomSpaceType
    {
        /// <summary>
        /// The CustomSpaceType function.
        /// </summary>
        /// <param name="model">The input model.</param>
        /// <param name="input">The arguments to the execution.</param>
        /// <returns>A CustomSpaceTypeOutputs instance containing computed results and the model with any new elements.</returns>
        public static CustomSpaceTypeOutputs Execute(Dictionary<string, Model> inputModels, CustomSpaceTypeInputs input)
        {
            var output = new CustomSpaceTypeOutputs();

            // set up a container for all the layout component definitions:
            Dictionary<string, ComponentDefinition> spaceTypeDefinitions = new Dictionary<string, ComponentDefinition>();

            var instanceNames = new Dictionary<string, List<string>>();

            // set up all component definitions
            foreach (var layout in input.Layouts)
            {
                var rules = new List<IComponentPlacementRule>();
                var anchors = layout.Boundary?.Vertices;
                // establish boundary
                if (layout.Boundary != null)
                {
                    output.Model.AddElement(layout.Boundary);
                }

                // draw furniture
                var catalogInstances = LoadAndDisplayCatalog(layout.Catalog, layout.SpaceType, instanceNames, false);
                if (input.Overrides?.Transform != null)
                {
                    foreach (var xformOverride in input.Overrides.Transform)
                    {
                        var matchingIndex = catalogInstances.FindIndex(i => i.Name == xformOverride.Identity.Name);
                        if (matchingIndex == -1)
                        {
                            output.Warnings.Add("That thing you thought would happen happened");
                            continue;
                        }
                        var matchingInstance = catalogInstances[matchingIndex];
                        catalogInstances.RemoveAt(matchingIndex);
                        var placedInstance = matchingInstance.BaseDefinition.CreateInstance(xformOverride.Value.Transform, matchingInstance.Name);
                        catalogInstances.Add(placedInstance);
                        // add another fresh copy with a new name since we moved the last one.
                        CreateAndSafeNameInstance(layout.SpaceType, instanceNames, catalogInstances, matchingInstance);
                        var anchor = placedInstance.Transform.Origin;
                        var anchorIndex = Enumerable.Range(0, anchors.Count).OrderBy(a => anchors[a].DistanceTo(anchor)).First();
                        var closestAnchor = anchors[anchorIndex];
                        var offsetXform = placedInstance.Transform.Concatenated(new Transform(anchor.Negate())).Concatenated(new Transform(anchor - closestAnchor));
                        // create rule 
                        var placementRule = new PositionPlacementRule(placedInstance.Name, anchorIndex, placedInstance.BaseDefinition, offsetXform);
                        rules.Add(placementRule);

                    }
                }

                output.Model.AddElements(catalogInstances);

                // draw walls
                foreach (var wall in layout.Walls.Where(w => w?.Polyline != null))
                {
                    Func<Polyline, IEnumerable<Element>> postProcess = (Polyline p) =>
                         {
                             var offsets = OffsetThickenedPolyline(wall, p);
                             return offsets.Select((w) =>
                             {
                                 return new Wall(w, 3, BuiltInMaterials.Default);
                             });
                         };
                    var createdWalls = postProcess(wall.Polyline);
                    output.Model.AddElements(createdWalls);
                    if (anchors != null)
                    {
                        var rule = new PolylineBasedElementPlacementRule(PolylinePlacementRule.FromClosestPoints(wall.Polyline, anchors, "Walls"));
                        rule.SetPostProcessOperation(postProcess);
                        rules.Add(rule);
                    }
                }
                if (rules.Count > 0)
                {
                    spaceTypeDefinitions.Add(layout.SpaceType, new ComponentDefinition(rules, anchors));
                }
            }

            // instantiate all component definitions

            var spacePlanningZones = inputModels["Space Planning Zones"];
            inputModels.TryGetValue("Levels", out var levelsModel);
            var levels = spacePlanningZones.AllElementsOfType<LevelElements>();
            if (inputModels.TryGetValue("Circulation", out var circModel))
            {
                var circSegments = circModel.AllElementsOfType<CirculationSegment>();
                foreach (var cs in circSegments)
                {
                    var matchingLevel = levels.FirstOrDefault(l => l.Level == cs.Level);
                    if (matchingLevel != null)
                    {
                        matchingLevel.Elements.Add(cs);
                    }
                }
            }
            var levelVolumes = levelsModel?.AllElementsOfType<LevelVolume>() ?? new List<LevelVolume>();

            foreach (var lvl in levels)
            {
                var corridors = lvl.Elements.OfType<CirculationSegment>();
                var corridorSegments = corridors.SelectMany(p => p.Profile.Segments());
                foreach (var spaceTypeName in spaceTypeDefinitions.Keys)
                {
                    LayOutAllRoomsOfSpaceType(output.Model, levelVolumes, lvl, corridorSegments, spaceTypeName, spaceTypeDefinitions[spaceTypeName]);
                }
            }

            return output;
        }

        public static Polygon[] OffsetThickenedPolyline(ThickenedPolyline wall, Polyline p)
        {
            Polygon[] offsets = null;
            if (wall.Width.HasValue && wall.Flip.HasValue)
            {
                offsets = p.OffsetOnSide(wall.Width.Value, wall.Flip.Value);
            }
            else
            {
                if (wall.LeftWidth > Vector3.EPSILON)
                {
                    p = p.OffsetOpen(-wall.LeftWidth);
                }

                offsets = p.OffsetOnSide(wall.LeftWidth + wall.RightWidth, false);
            }
            return offsets;
        }

        private static void LayOutAllRoomsOfSpaceType(Model model, IEnumerable<LevelVolume> levelVolumes, LevelElements lvl, IEnumerable<Line> corridorSegments, string spaceTypeName, ComponentDefinition component)
        {
            Console.WriteLine($"Laying out {spaceTypeName}");

            var meetingRmBoundaries = lvl.Elements.OfType<SpaceBoundary>().Where(z => (z.HyparSpaceType ?? z.Name) == spaceTypeName || z.AdditionalProperties["ProgramName"] as string == spaceTypeName);
            var levelVolume = levelVolumes.FirstOrDefault(l =>
                    (lvl.AdditionalProperties.TryGetValue("LevelVolumeId", out var levelVolumeId) &&
                        levelVolumeId as string == l.Id.ToString())) ??
                        levelVolumes.FirstOrDefault(l => l.Name == lvl.Name);

            foreach (var room in meetingRmBoundaries)
            {
                var spaceBoundary = room.Boundary;
                Line orientationGuideEdge = WallGeneration.FindPrimaryAccessEdge(spaceBoundary.Perimeter.Segments(), corridorSegments, levelVolume?.Profile, out var otherSegments);

                var orientationTransform = new Transform(Vector3.Origin, orientationGuideEdge.Direction(), Vector3.ZAxis);
                var boundaryCurves = new List<Polygon>();
                boundaryCurves.Add(spaceBoundary.Perimeter);
                boundaryCurves.AddRange(spaceBoundary.Voids ?? new List<Polygon>());

                var grid = new Grid2d(boundaryCurves, orientationTransform);
                foreach (var cell in grid.GetCells())
                {
                    Console.WriteLine("In Da Grid");

                    var rect = cell.GetCellGeometry() as Polygon;
                    var segs = rect.Segments();
                    var width = segs[0].Length();
                    var depth = segs[1].Length();
                    var trimmedGeo = cell.GetTrimmedCellGeometry();
                    if (!cell.IsTrimmed() && trimmedGeo.Count() > 0)
                    {
                        Console.WriteLine("INSTANTIATING STUFF");
                        // output.Model.AddElement(InstantiateLayout(configs, width, depth, rect, room.Transform));
                        var instance = component.Instantiate(ContentConfiguration.AnchorsFromRect(rect.TransformedPolygon(room.Transform)));
                        model.AddElement(instance);
                    }
                    else if (trimmedGeo.Count() > 0)
                    {
                        var largestTrimmedShape = trimmedGeo.OfType<Polygon>().OrderBy(s => s.Area()).Last();
                        var cinchedVertices = rect.Vertices.Select(v => largestTrimmedShape.Vertices.OrderBy(v2 => v2.DistanceTo(v)).First()).ToList();
                        var cinchedPoly = new Polygon(cinchedVertices);
                        // output.Model.AddElement(new ModelCurve(cinchedPoly, BuiltInMaterials.ZAxis, levelVolume.Transform));
                        // output.Model.AddElement(InstantiateLayout(configs, width, depth, cinchedPoly, room.Transform));
                        var instance = component.Instantiate(ContentConfiguration.AnchorsFromRect(cinchedPoly.TransformedPolygon(room.Transform)));
                        model.AddElement(instance);
                        Console.WriteLine("🤷‍♂️ funny shape!!!");
                    }
                }

            }
        }

        public static List<ElementInstance> LoadAndDisplayCatalog(string url, string spaceTypeName, Dictionary<string, List<string>> instanceNames, bool useReferenceArrangement = false, string nameFilter = null)
        {
            var catalogInstances = new List<ElementInstance>();
            var json = "";
            using (var client = new WebClient())
            {
                json = client.DownloadString(url);
            }
            if (string.IsNullOrEmpty(json))
            {
                throw new System.Exception("Unable to fetch catalog at specified url.");
            }
            ContentCatalog content;
            try
            {
                content = JsonConvert.DeserializeObject<ContentCatalog>(json);
            }
            catch
            {
                var model = Model.FromJson(json);
                content = model.AllElementsOfType<ContentCatalog>().First();
            }
            var instances = content.ReferenceConfiguration?.OfType<ElementInstance>() ?? new List<ElementInstance>();
            if (!string.IsNullOrEmpty(nameFilter))
            {
                instances = instances.Where(i => LikeOperator.LikeString(i.BaseDefinition.Name, nameFilter, Microsoft.VisualBasic.CompareMethod.Text) || i.BaseDefinition.Name.Contains(nameFilter));
            }
            // outputModel.AddElement(new Material("White", Colors.White));

            Console.WriteLine($"there are {content.Content.Where(c => c != null).Count()} non-null items in content");
            if (!useReferenceArrangement || instances.Count() == 0)
            {
                PlaceContentInLinearRow(content, spaceTypeName, instanceNames, nameFilter, catalogInstances);
            }
            else
            {
                foreach (var instance in instances)
                {
                    CreateAndSafeNameInstance(spaceTypeName, instanceNames, catalogInstances, instance);
                }
                // foreach (var instance in instances)
                // {
                //     var ce = instance.BaseDefinition as ContentElement;
                //     var bbox = ce.BoundingBox;
                //     var labelMass = new Mass(Polygon.Rectangle(0.1, 0.1), 0.1, BuiltInMaterials.Trans, instance.Transform.Concatenated(new Transform((bbox.Max.X - bbox.Min.X) / 2, (bbox.Max.Y - bbox.Min.Y) / 2, bbox.Max.Z)), name: ce.Name);
                //     outputModel.AddElement(labelMass);
                // }
            }
            return catalogInstances;

        }

        private static void CreateAndSafeNameInstance(string spaceTypeName, Dictionary<string, List<string>> instanceNames, List<ElementInstance> catalogInstances, ElementInstance instance)
        {
            var name = $"{spaceTypeName}: {instance.BaseDefinition.Name}";

            if (!instanceNames.ContainsKey(name))
            {
                instanceNames[name] = new List<string>();
            }

            var newInstanceName = $"{name} {instanceNames[name].Count}";
            instanceNames[name].Add(newInstanceName);
            catalogInstances.Add(instance.BaseDefinition.CreateInstance(instance.Transform, newInstanceName));
        }

        private static void PlaceContentInLinearRow(ContentCatalog content, string spaceTypeName, Dictionary<string, List<string>> instanceNames, string nameFilter, List<ElementInstance> catalogInstances)
        {
            var spacing = 0.8;

            var currentPos = 0.0;
            var contentList = content.Content.Select(c => c.GltfLocation)
                .Distinct()
                .Select(
                    loc => content.Content.First(c => c.GltfLocation == loc)
                    );
            foreach (var contentElement in contentList)
            {
                if (!string.IsNullOrEmpty(nameFilter) && !(LikeOperator.LikeString(contentElement.Name, nameFilter, Microsoft.VisualBasic.CompareMethod.Text) || contentElement.Name.Contains(nameFilter)))
                {
                    continue;
                }
                var bbox = contentElement.BoundingBox;
                var bboxDiagonal = bbox.Max - bbox.Min;
                var targetLocation = new Vector3(currentPos, 0, 0);
                var transformForElement = targetLocation - bbox.Min;
                var boundingBoxCrv = new Polygon(new[] {
                  bbox.Min,
                  new Vector3(bbox.Max.X,bbox.Min.Y,bbox.Min.Z),
                  new Vector3(bbox.Max.X,bbox.Max.Y,bbox.Min.Z),
                  new Vector3(bbox.Min.X,bbox.Max.Y,bbox.Min.Z),
                });
                var displacement = new Transform(transformForElement);
                // outputModel.AddElement(new ModelCurve(boundingBoxCrv.TransformedPolygon(displacement)));
                contentElement.Transform = new Transform();
                var instance = contentElement.CreateInstance(displacement, contentElement.Name);
                // var labelMass = new Mass(Polygon.Rectangle(0.1, 0.1), 0.1, BuiltInMaterials.Trans, displacement.Concatenated(new Transform((bbox.Max.X - bbox.Min.X) / 2, (bbox.Max.Y - bbox.Min.Y) / 2, bbox.Max.Z)), name: contentElement.Name);
                // outputModel.AddElement(labelMass);
                CreateAndSafeNameInstance(spaceTypeName, instanceNames, catalogInstances, instance);
                // catalogInstances.Add(instance);
                Console.WriteLine($"{contentElement.Name}: {bbox.Max.X - bbox.Min.X}");
                currentPos += (bbox.Max.X - bbox.Min.X) + spacing;
            }
        }

    }
}