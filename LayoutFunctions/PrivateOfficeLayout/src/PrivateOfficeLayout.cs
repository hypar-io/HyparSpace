using Elements;
using Elements.Geometry;
using System.Collections.Generic;
using Elements.Components;
using System.Linq;
using System.IO;
using Newtonsoft.Json;
using System;
using Elements.Spatial;
using LayoutFunctionCommon;
using IFC;

namespace PrivateOfficeLayout
{
    public static class PrivateOfficeLayout
    {
        private static readonly List<ElementProxy<SpaceBoundary>> proxies = new List<ElementProxy<SpaceBoundary>>();

        private static readonly string SpaceBoundaryDependencyName = SpaceSettingsOverride.Dependency;

        /// <summary>
        /// Map between the layout and the number of seats it lays out
        /// </summary>
        private static Dictionary<string, int> _configSeats = new Dictionary<string, int>()
        {
            ["Configuration A"] = 9,
            ["Configuration B"] = 4,
            ["Configuration C"] = 3,
            ["Configuration D"] = 2,
            ["Configuration E"] = 2,
            ["Configuration F"] = 2,
        };

        /// <summary>
        /// The PrivateOfficeLayout function.
        /// </summary>
        /// <param name="model">The input model.</param>
        /// <param name="input">The arguments to the execution.</param>
        /// <returns>A PrivateOfficeLayoutOutputs instance containing computed results and the model with any new elements.</returns>
        public static PrivateOfficeLayoutOutputs Execute(Dictionary<string, Model> inputModels, PrivateOfficeLayoutInputs input)
        {
            Elements.Serialization.glTF.GltfExtensions.UseReferencedContentExtension = true;

            proxies.Clear();
            var spacePlanningZones = inputModels["Space Planning Zones"];
            var hasLevels = inputModels.TryGetValue("Levels", out var levelsModel);
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
            var levelVolumes = LayoutStrategies.GetLevelVolumes<LevelVolume>(inputModels);
            var output = new PrivateOfficeLayoutOutputs();
            var assmLoc = System.Reflection.Assembly.GetExecutingAssembly().Location;
            var dir = Path.GetDirectoryName(assmLoc);
            var configJson = File.ReadAllText(Path.Combine(dir, "PrivateOfficeConfigurations.json"));
            var configs = JsonConvert.DeserializeObject<SpaceConfiguration>(configJson);
            Configurations.Init(configs);


            var configsNames = new string[] {
                "ClassroomLayout",
                "LoungeLayout",
                "MeetingRoomLayout",
                "OpenCollabLayout",
                "OpenOfficeLayout",
                "PantryLayout",
                "ReceptionLayout",
                "PrivateOfficeLayout",
                "PhoneBoothLayout",
            };
            var configsNames1 = new string[] {
                "ClassroomConfigurations",
                "LoungeConfigurations",
                "ConferenceRoomConfigurations",
                "OpenCollaborationConfigurations",
                "OpenOfficeDeskConfigurations",
                "PantryConfigurations",
                "ReceptionConfigurations",
                "PrivateOfficeConfigurations",
                "PhoneBoothConfigurations",
            };

            var elements = new Dictionary<string, List<string>>();
            var elements1 = new Dictionary<string, List<(string, string)>>();

            // var configJson0 = File.ReadAllText($"D:/Hypar/Forks/HyparSpace/LayoutFunctions/PrivateOfficeLayout/mirrored-catalog.json");
            // var model = Model.FromJson(configJson0);
            // var catalog = model.AllElementsOfType<ContentCatalog>().First();

            // var elemNames = new Dictionary<string, ContentElement>();
            // foreach (var item in catalog.Content)
            // {
            //     if (item.Name.Contains(" Mirrored"))
            //     {
            //         elemNames.Add(item.Name.Replace(" Mirrored", "").Replace("1 Mirrored", ""), item);
            //     }
            //     else
            //     {
            //         var oldNameArray = item.Name.Split(" ");
            //         for (int j = 0; j < oldNameArray.Count(); j++)
            //         {
            //             oldNameArray[j] =
            //                 oldNameArray[j] == "Left" ? "Right" :
            //                 oldNameArray[j] == "Right" ? "Left" :
            //                 oldNameArray[j];
            //         }
            //         elemNames.Add(string.Join(" ", oldNameArray), item);
            //     }
            // }

            // // var allCatalogs = new Dictionary<string, ContentCatalog>();
            // // for (int i = 0; i < configsNames.Count(); i++)
            // // {
            // //     // var configJson1 = File.ReadAllText($"D:/Hypar/Forks/HyparSpace/LayoutFunctions/{configsNames[i]}/catalog-mirrored.json");

            // //     var configJson1 = File.ReadAllText($"D:/Hypar/Forks/HyparSpace/LayoutFunctions/{configsNames[i]}/catalog.json");
            // //     var model1 = Model.FromJson(configJson1);
            // //     var catalog1 = model1.AllElementsOfType<ContentCatalog>().First();
            // //     allCatalogs.Add(configsNames[i], catalog1);
            // // }

            // for (int i = 0; i < configsNames.Count(); i++)
            // {
            //     // var configJson1 = File.ReadAllText($"D:/Hypar/Forks/HyparSpace/LayoutFunctions/{configsNames[i]}/catalog-mirrored.json");

            //     var configJson1 = File.ReadAllText($"D:/Hypar/Forks/HyparSpace/LayoutFunctions/{configsNames[i]}/catalog.json");
            //     var model1 = Model.FromJson(configJson1);
            //     var catalog1 = model1.AllElementsOfType<ContentCatalog>().First();

            //     var configJson2 = File.ReadAllText($"D:/Hypar/Forks/HyparSpace/LayoutFunctions/{configsNames[i]}/{configsNames1[i]}.json");
            //     var configs1 = JsonConvert.DeserializeObject<SpaceConfiguration>(configJson2);

            //     var allElem0 = configs1.SelectMany(c => c.Value.ContentItems).ToList();
            //     // var allElem0 = configs1.SelectMany(c => c.Value.ContentItems).Where(e => !catalog1.Content.Any(cit => cit.Name == e.Name || cit.GltfLocation == e.Url)).ToList();
            //     // var allElem00 = configs1.SelectMany(c => c.Value.ContentItems).Where(e => !catalog1.Content.Any(cit => cit.Name == e.Name && cit.GltfLocation == e.Url)).ToList();
            //     // var allElem0 = catalog1.Content.Where(e => !configs1.SelectMany(c => c.Value.ContentItems).Any(cit => cit.Name == e.Name || e.GltfLocation == cit.Url)).ToList();
            //     // var allElem00 = catalog1.Content.Where(e => !configs1.SelectMany(c => c.Value.ContentItems).Any(cit => cit.Name == e.Name && e.GltfLocation == cit.Url)).ToList();

            //     var j = 0;
            //     // add names
            //     while (j < allElem0.Count())
            //     {
            //         var item = allElem0[j];
            //         var elem = catalog1.Content.FirstOrDefault(c => c.GltfLocation == item.Url);
            //         if (elem != null)
            //         {
            //             item.Name = elem.Name;
            //         }
            //         j++;
            //     }

            //     using (FileStream s = File.Create($"D:/Hypar/Forks/HyparSpace/LayoutFunctions/{configsNames[i]}/{configsNames1[i]}-out.json"))
            //     using (StreamWriter writer = new StreamWriter(s))
            //     using (JsonTextWriter jsonWriter = new JsonTextWriter(writer))
            //     {
            //         var serializer = new JsonSerializer();
            //         serializer.Serialize(jsonWriter, configs1);
            //         jsonWriter.Flush();
            //     }

            //     j = 0;
            //     // remove not used
            //     while (j < catalog1.Content.Count())
            //     {
            //         var item = catalog1.Content[j];
            //         if (!allElem0.Any(e => e.Name == item.Name || e.Url == item.GltfLocation))
            //         {
            //             var refes = catalog1.ReferenceConfiguration.Where(e => (e as ElementInstance).BaseDefinition?.Name == item.Name).ToList();
            //             for (int k = refes.Count() - 1; k >= 0; k--)
            //             {
            //                 catalog1.ReferenceConfiguration.Remove(refes[k]);
            //             }
            //             catalog1.Content.Remove(item);
            //             continue;
            //         }
            //         j++;
            //     }

            //     // var allElem = configs1.SelectMany(c => c.Value.ContentItems.Select(it => (it.Name, it.Url)));
            //     var allElem = configs1.SelectMany(c => c.Value.ContentItems.Select(it => !string.IsNullOrEmpty(it.Name) ? it.Name : catalog1.Content.FirstOrDefault(cit => cit.Name == it.Name || cit.GltfLocation == it.Url)?.Name)).Distinct().OrderBy(n => n).ToList();
            //     var allElem1 = configs1.SelectMany(c => c.Value.ContentItems.Select(it => (!string.IsNullOrEmpty(it.Name) ? it.Name : catalog1.Content.FirstOrDefault(cit => cit.Name == it.Name || cit.GltfLocation == it.Url)?.Name, it.Url))).Distinct().OrderBy(n => n).ToList();

            //     // elements.Add(configsNames[i], catalog.Content.Where(c => allElem.Contains(c.Name)).Select(c => c.Name).ToList());
            //     // elements.Add(configsNames[i], catalog.Content.Where(c => allElem.Any(a => c.Name.Replace(" Mirrored", "") == a)).Select(c => c.Name).ToList());
            //     // elements.Add(configsNames[i], allElem.Where(e => e != null && (e.Contains("Left") || e.Contains("Right"))).ToList());
            //     elements.Add(configsNames[i], allElem);
            //     // elements1.Add(configsNames[i], allElem0.Select(n => (n.Name, n.GltfLocation)).ToList());
            //     // elements1.Add(configsNames[i] + "1", allElem00.Select(n => (n.Name, n.GltfLocation)).ToList());
            //     // elements1.Add(configsNames[i], allElem1.Where(n => string.IsNullOrEmpty(n.Item1)).ToList());
            //     // elements1.Add(configsNames[i], catalog1.Content.Where(n => string.IsNullOrEmpty(n.Name)).Select(n => (n.Name, n.GltfLocation)).ToList());

            //     var elements4 = allElem.Distinct().OrderBy(n => n).Where(n => catalog.Content.Any(u => u.Name.Replace(" Mirrored", "").Replace("1 Mirrored", "") == n));
            //     // var elements4 = allElem.Distinct().OrderBy(n => n).Where(n => elemNames.Any(u => u.Key == n));
            //     foreach (var item in elements4)
            //     {
            //         var contentName = elemNames[item].Name;
            //         var content = catalog.Content.FirstOrDefault(u => u.Name == contentName);
            //         var Reference = catalog.ReferenceConfiguration.FirstOrDefault(u => u.Name == contentName);
            //         if (content != null) catalog1.Content.Add(content);
            //         if (Reference != null) catalog1.ReferenceConfiguration.Add(Reference);
            //     }

            //     model1.ToJson($"D:/Hypar/Forks/HyparSpace/LayoutFunctions/{configsNames[i]}/catalog-out.json", true);

            //     // elements.Add(configsNames[i], catalog.Content.Where(c => allElem.Any(a => a.Name == c.Name) || allElem.Any(a => a.Url == c.GltfLocation)).Select(c => c.Name).ToList());
            //     // elements1.Add(configsNames[i], configs1.Content.Where(c => t.Contains(c.Name) && (configJson2.Contains(c.Name) || configJson2.Contains(c.GltfLocation))).Select(c => (c.Name, c.GltfLocation)).DistinctBy(n => n.Name).OrderBy(n => n.Name).ToList());
            //     // var ti = string.Join("\n", elements1.Last().Value.Select(e => e));
            // }

            // var elements5 = string.Join("\n", elements.SelectMany(e => e.Value).Distinct().OrderBy(n => n));
            // var elements2 = string.Join("\n", elements.SelectMany(e => e.Value).Distinct().OrderBy(n => n).Where(n => !catalog.Content.Any(u => u.Name.Replace(" Mirrored", "") == n)));
            // var elements3 = string.Join("\n\n\n", elements.Select(es => es.Key + "\n" + string.Join("\n", es.Value.Distinct().OrderBy(n => n).Where(n => catalog.Content.Any(u => u.Name.Replace(" Mirrored", "") == n)))));

            var wallMat = new Material("Drywall", new Color(0.9, 0.9, 0.9, 1.0), 0.01, 0.01);
            var glassMat = new Material("Glass", new Color(0.7, 0.7, 0.7, 0.3), 0.3, 0.6);
            var mullionMat = new Material("Storefront Mullions", new Color(0.5, 0.5, 0.5, 1.0));

            var overridesById = GetOverridesByBoundaryId(input, levels);
            var totalPrivateOfficeCount = 0;
            foreach (var lvl in levels)
            {
                var corridors = lvl.Elements.OfType<CirculationSegment>();
                var corridorSegments = corridors.SelectMany(p => p.Profile.Segments()).ToList();
                var privateOfficeBoundaries = lvl.Elements.OfType<SpaceBoundary>().Where(z => z.Name == "Private Office");
                var levelVolume = levelVolumes.FirstOrDefault(l =>
                    (lvl.AdditionalProperties.TryGetValue("LevelVolumeId", out var levelVolumeId) &&
                        levelVolumeId as string == l.Id.ToString())) ??
                        levelVolumes.FirstOrDefault(l => l.Name == lvl.Name);

                var wallCandidateLines = new List<(Line line, string type)>();
                foreach (var room in privateOfficeBoundaries)
                {
                    var seatsCount = 0;
                    var privateOfficeCount = 0;
                    var config = MatchApplicableOverride(overridesById, GetElementProxy(room, privateOfficeBoundaries.Proxies(SpaceBoundaryDependencyName)), input);
                    var privateOfficeRoomBoundaries = DivideBoundaryAlongVAxis(room, levelVolume, corridorSegments, wallCandidateLines, config);

                    foreach (var roomBoundary in privateOfficeRoomBoundaries)
                    {
                        WallGeneration.FindWallCandidates(roomBoundary, levelVolume?.Profile, corridorSegments.Union(wallCandidateLines.Where(w => w.type == "Glass-Edge").Select(w => w.line)), out Line orientationGuideEdge);

                        var relativeRoomTransform = room.Transform.Concatenated(levelVolume?.Transform.Inverted() ?? new Transform());
                        var orientationTransform = new Transform(Vector3.Origin, orientationGuideEdge.Direction(), Vector3.ZAxis);
                        orientationTransform.Concatenate(relativeRoomTransform);
                        var boundaryCurves = new List<Polygon>();
                        boundaryCurves.Add(roomBoundary.Boundary.Perimeter.TransformedPolygon(relativeRoomTransform));
                        boundaryCurves.AddRange(roomBoundary.Boundary.Voids?.Select(v => v.TransformedPolygon(relativeRoomTransform)) ?? new List<Polygon>());

                        var grid = new Grid2d(boundaryCurves, orientationTransform);
                        if (config.Value.OfficeSizing.AutomateOfficeSubdivisions)
                        {
                            try
                            {
                                grid.U.DivideByApproximateLength(config.Value.OfficeSizing.OfficeSize, EvenDivisionMode.RoundDown);
                            }
                            catch
                            {
                                // continue
                            }
                        }
                        foreach (var cell in grid.GetCells())
                        {
                            var rect = cell.GetCellGeometry() as Polygon;
                            var (selectedConfigs, configsTransform) = Configurations.GetConfigs(rect.Centroid(), config.Value.PrimaryAxisFlipLayout, config.Value.SecondaryAxisFlipLayout);
                            var segs = rect.Segments();
                            var width = segs[0].Length();
                            var depth = segs[1].Length();
                            var trimmedGeo = cell.GetTrimmedCellGeometry();
                            if (!cell.IsTrimmed() && trimmedGeo.Count() > 0)
                            {
                                var layout = InstantiateLayout(selectedConfigs, width, depth, rect, (levelVolume?.Transform ?? new Transform()).Concatenated(configsTransform), out var seats);
                                LayoutStrategies.SetLevelVolume(layout, levelVolume?.Id);
                                output.Model.AddElement(layout);
                                privateOfficeCount++;
                                seatsCount += seats;
                            }
                            else if (trimmedGeo.Count() > 0)
                            {
                                var largestTrimmedShape = trimmedGeo.OfType<Polygon>().OrderBy(s => s.Area()).Last();
                                var cinchedVertices = rect.Vertices.Select(v => largestTrimmedShape.Vertices.OrderBy(v2 => v2.DistanceTo(v)).First()).ToList();
                                var cinchedPoly = new Polygon(cinchedVertices);
                                var areaRatio = cinchedPoly.Area() / rect.Area();
                                if (areaRatio > 0.7)
                                {
                                    var layout = InstantiateLayout(selectedConfigs, width, depth, cinchedPoly, (levelVolume?.Transform ?? new Transform()).Concatenated(configsTransform), out var seats);
                                    LayoutStrategies.SetLevelVolume(layout, levelVolume?.Id);
                                    output.Model.AddElement(layout);
                                    privateOfficeCount++;
                                    seatsCount += seats;
                                }
                            }
                        }

                        if (config.Value.CreateWalls)
                        {
                            wallCandidateLines.AddRange(WallGeneration.PartitionsAndGlazingCandidatesFromGrid(wallCandidateLines, grid, levelVolume?.Profile));
                        }
                    }

                    totalPrivateOfficeCount += privateOfficeCount;
                    output.Model.AddElement(new SpaceMetric(room.Id, seatsCount, privateOfficeCount, 0, 0));
                }

                var height = privateOfficeBoundaries.FirstOrDefault()?.Height ?? 3;
                output.Model.AddElement(new InteriorPartitionCandidate(Guid.NewGuid())
                {
                    WallCandidateLines = wallCandidateLines,
                    Height = height,
                    LevelTransform = levelVolume?.Transform ?? new Transform()
                });
            }
            output.PrivateOfficeCount = totalPrivateOfficeCount;
            OverrideUtilities.InstancePositionOverrides(input.Overrides, output.Model);
            output.Model.AddElements(proxies);
            return output;
        }

        private static IEnumerable<SpaceBoundary> DivideBoundaryAlongVAxis(SpaceBoundary room, LevelVolume levelVolume, List<Line> corridorSegments, List<(Line line, string type)> wallCandidateLines, SpaceSettingsOverride config)
        {
            var levelInvertedTransform = levelVolume?.Transform.Inverted() ?? new Transform();
            if (config.Value.OfficeSizing.AutomateOfficeSubdivisions)
            {
                var initialWallCandidates = WallGeneration.FindWallCandidates(room, levelVolume?.Profile, corridorSegments, out var orientationGuideEdge)
                          .Select(w =>
                          {
                              if (w.type == "Glass")
                              {
                                  w.type = "Glass-Edge";
                              }
                              return w;
                          });
                if (config.Value.CreateWalls)
                {
                    wallCandidateLines.AddRange(initialWallCandidates.Select(c => (c.line.TransformedLine(levelInvertedTransform), c.type)));
                }
                var relativeRoomTransform = room.Transform.Concatenated(levelInvertedTransform);
                var relativeRoomTransformProjected = new Transform(0, 0, -relativeRoomTransform.Origin.Z);
                var orientationTransform = new Transform(Vector3.Origin, orientationGuideEdge.Direction(), Vector3.ZAxis);
                orientationTransform.Concatenate(relativeRoomTransform);
                var boundaryCurves = new List<Polygon>();
                boundaryCurves.Add(room.Boundary.Perimeter.TransformedPolygon(relativeRoomTransform));
                boundaryCurves.AddRange(room.Boundary.Voids?.Select(v => v.TransformedPolygon(relativeRoomTransform)) ?? new List<Polygon>());
                var tempGrid = new Grid2d(boundaryCurves, orientationTransform);
                try
                {
                    var vLength = tempGrid.V.Domain.Length;
                    if (vLength > 3 * config.Value.OfficeSizing.OfficeSize)
                    {
                        var officeCount = Math.Floor(vLength / config.Value.OfficeSizing.OfficeSize);
                        var corridorCount = Math.Floor(officeCount / 2 - 0.1); // 1 corridor for 3, 4 offices, 2 corridors for 5, 6 offices, etc
                        var idealOfficeSize = ((vLength - (corridorCount * 1.5)) / officeCount) * .99;
                        Console.WriteLine($"🤪 officeCount: {officeCount}, corridorCount: {corridorCount}, idealOfficeSize: {idealOfficeSize}");
                        tempGrid.V.DivideByPattern(new[] { ("Office", idealOfficeSize), ("Office", idealOfficeSize), ("Corridor", 1.5) });

                        List<SpaceBoundary> returnCells = new List<SpaceBoundary>();
                        foreach (var cell in tempGrid.GetCells())
                        {
                            var cellBoundaries = cell.GetTrimmedCellGeometry()?.OfType<Polygon>().ToList() ?? new List<Polygon>();
                            if (cellBoundaries.Count < 0)
                            {
                                continue;
                            }
                            if (cell.Type != null && cell.Type.Contains("Office"))
                            {
                                var profile = new Profile(cellBoundaries).Transformed(relativeRoomTransformProjected);
                                var spaceBoundary = new SpaceBoundary()
                                {
                                    Boundary = profile,
                                    Area = profile.Area(),
                                    Transform = room.Transform,
                                    Material = room.Material,
                                    Name = room.Name
                                };
                                spaceBoundary.ParentCentroid = room.IndividualCentroid;
                                returnCells.Add(spaceBoundary);
                            }
                            else
                            {
                                corridorSegments.AddRange(cellBoundaries.SelectMany(c => c.Segments()));
                            }
                        }
                        if (config.Value.CreateWalls)
                        {
                            wallCandidateLines.AddRange(WallGeneration.PartitionsAndGlazingCandidatesFromGrid(wallCandidateLines, tempGrid, levelVolume?.Profile));
                        }
                        return returnCells;
                    }
                    else if (vLength > 2 * config.Value.OfficeSizing.OfficeSize)
                    {
                        tempGrid.V.DivideByApproximateLength(config.Value.OfficeSizing.OfficeSize, EvenDivisionMode.RoundDown);
                        if (config.Value.CreateWalls)
                        {
                            wallCandidateLines.AddRange(WallGeneration.PartitionsAndGlazingCandidatesFromGrid(wallCandidateLines, tempGrid, levelVolume?.Profile));
                        }
                        return tempGrid.GetCells().Select(c =>
                        {
                            var profile = new Profile(c.GetTrimmedCellGeometry().OfType<Polygon>().ToList()).Transformed(relativeRoomTransformProjected);
                            var spaceBoundary = new SpaceBoundary()
                            {
                                Boundary = profile,
                                Area = profile.Area(),
                                Transform = room.Transform,
                                Material = room.Material,
                                Name = room.Name
                            };
                            spaceBoundary.ParentCentroid = room.IndividualCentroid;
                            return spaceBoundary;
                        }
                        );
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("⚠️ exception while trying to subdivide office depth direction");
                    Console.WriteLine(e.Message);
                    Console.WriteLine(e.StackTrace);
                }

                return new[] { room };
            }
            else
            {
                if (config.Value.CreateWalls)
                {
                    var initialWallCandidates = WallGeneration.FindWallCandidates(room, levelVolume?.Profile, corridorSegments, out var orientationGuideEdge)
                                                          .Select(w =>
                                                          {
                                                              if (w.type == "Glass")
                                                              {
                                                                  w.type = "Glass-Edge";
                                                              }
                                                              return w;
                                                          });
                    wallCandidateLines.AddRange(initialWallCandidates.Select(c => (c.line.TransformedLine(levelInvertedTransform), c.type)));
                }
                return new[] { room };
            }
        }

        private static ComponentInstance InstantiateLayout(SpaceConfiguration configs, double width, double length, Polygon rectangle, Transform xform, out int seatsCount)
        {
            seatsCount = 0;
            ContentConfiguration selectedConfig = null;
            var orderedKeys = configs.OrderByDescending(kvp => kvp.Value.CellBoundary.Depth * kvp.Value.CellBoundary.Width).Select(kvp => kvp.Key);
            foreach (var key in orderedKeys)
            {
                var config = configs[key];
                if (config.CellBoundary.Width < width && config.CellBoundary.Depth < length)
                {
                    selectedConfig = config;
                    seatsCount += _configSeats[key];
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
            return instance;
        }

        private static Dictionary<Guid, SpaceSettingsOverride> GetOverridesByBoundaryId(PrivateOfficeLayoutInputs input, IEnumerable<LevelElements> levels)
        {
            var overridesById = new Dictionary<Guid, SpaceSettingsOverride>();
            foreach (var spaceOverride in input.Overrides?.SpaceSettings ?? new List<SpaceSettingsOverride>())
            {
                var matchingBoundary =
                levels.SelectMany(l => l.Elements)
                    .OfType<SpaceBoundary>()
                    .OrderBy(ob => ob.ParentCentroid.Value
                    .DistanceTo(spaceOverride.Identity.ParentCentroid))
                    .First();

                if (overridesById.ContainsKey(matchingBoundary.Id))
                {
                    var mbCentroid = matchingBoundary.ParentCentroid.Value;
                    if (overridesById[matchingBoundary.Id].Identity.ParentCentroid.DistanceTo(mbCentroid) > spaceOverride.Identity.ParentCentroid.DistanceTo(mbCentroid))
                    {
                        overridesById[matchingBoundary.Id] = spaceOverride;
                    }
                }
                else
                {
                    overridesById.Add(matchingBoundary.Id, spaceOverride);
                }
            }

            return overridesById;
        }

        private static SpaceSettingsOverride MatchApplicableOverride(
            Dictionary<Guid, SpaceSettingsOverride> overridesById,
            ElementProxy<SpaceBoundary> boundaryProxy,
            PrivateOfficeLayoutInputs input)
        {
            var overrideName = SpaceSettingsOverride.Name;
            SpaceSettingsOverride config;

            // See if we already have matching override attached
            var existingOverrideId = boundaryProxy.OverrideIds<SpaceSettingsOverride>(overrideName).FirstOrDefault();
            if (existingOverrideId != null)
            {
                if (overridesById.TryGetValue(Guid.Parse(existingOverrideId), out config))
                {
                    return config;
                }
            }

            // Try to match from identity in configs dictionary. Use a default in case none found
            if (!overridesById.TryGetValue(boundaryProxy.ElementId, out config))
            {
                config = new SpaceSettingsOverride(
                            Guid.NewGuid().ToString(),
                            null,
                            new SpaceSettingsValue(
                                new SpaceSettingsValueOfficeSizing(
                                    input.OfficeSizing.AutomateOfficeSubdivisions,
                                    input.OfficeSizing.OfficeSize),
                                    input.CreateWalls,
                                    false,
                                    false
                            )
                    );
                overridesById.Add(boundaryProxy.ElementId, config);
            }

            // Attach the identity and values data to the proxy
            boundaryProxy.AddOverrideIdentity(overrideName, config.Id, config.Identity);
            boundaryProxy.AddOverrideValue(overrideName, config.Value);

            // Make sure proxies list has the proxy so that it will serialize in the model.
            if (!proxies.Contains(boundaryProxy))
            {
                proxies.Add(boundaryProxy);
            }

            return config;
        }

        private static ElementProxy<SpaceBoundary> GetElementProxy(SpaceBoundary spaceBoundary, IEnumerable<ElementProxy<SpaceBoundary>> allSpaceBoundaries)
        {
            return allSpaceBoundaries.Proxy(spaceBoundary) ?? spaceBoundary.Proxy(SpaceBoundaryDependencyName);
        }
    }
}