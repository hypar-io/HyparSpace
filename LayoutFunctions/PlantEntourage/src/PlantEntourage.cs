using Elements;
using Elements.Annotations;
using Elements.Geometry;

namespace PlantEntourage
{
    public static class PlantEntourage
    {
        private const string NotAllPlantsPlacedWarningName = "Some plants cannot be placed";
        private const string NotAllPlantsPlacedWarningText = "Some plants cannot be placed in a room because of obstacles or low density.";
        
        private const double plantWidth = 0.5;
        private const double plantHeight = 1.8;
        private const double plantLength = 0.5;

        private const double gapBetweenPlantAndWall = 0.3;
        private const double minGapBetweenPlants = plantLength;

        /// <summary>
        /// Puts a plant into each meeting room.
        /// </summary>
        /// <param name="model">The input model.</param>
        /// <param name="input">The arguments to the execution.</param>
        /// <returns>A PlantEntourageOutputs instance containing computed results and the model with any new elements.</returns>
        public static PlantEntourageOutputs Execute(Dictionary<string, Model> inputModels, PlantEntourageInputs input)
        {
            var output = new PlantEntourageOutputs();

            if (!inputModels.TryGetValue("Space Planning Zones", out var siteModel))
            {
                output.Errors.Add("The model output named 'Space Planning Zones' could not be found.");
                return output;
            }

            var furnitureModelsKeys = new[] {
                "Meeting Room Layout",
                "Open Office Layout",
                "Restroom Layout"
            };

            var obstacles = furnitureModelsKeys.SelectMany(key => GetObstaclesFromModel(inputModels, key)).ToList();

            var programTypes = input.ProgramTypes.ToHashSet();
            var spaceBoundaries = siteModel.AllElementsOfType<SpaceBoundary>().Where(sp => programTypes.Contains(sp.ProgramType));

            foreach (var spaceBoundary in spaceBoundaries)
            {
                Polygon roomPolygon = spaceBoundary.Boundary.Perimeter;
                BBox3 bounds = roomPolygon.Bounds();

                int estimatedCountOfPlants = (int)Math.Floor(spaceBoundary.Area / input.PlantDensity);
                var roomObstacles = obstacles.Where(o => o.Intersects(bounds)).ToList();
                var placementSites = GetPlantSitesInRoom(spaceBoundary, estimatedCountOfPlants, roomObstacles);

                if (estimatedCountOfPlants > placementSites.Count)
                {
                    var warning = Message.FromPolygon(NotAllPlantsPlacedWarningText,
                                                      roomPolygon,
                                                      severity: MessageSeverity.Warning,
                                                      name: NotAllPlantsPlacedWarningName);
                    output.Model.AddElement(warning);
                }

                foreach (var placement in placementSites)
                {
                    var plantElements = CreatePlantElementsAtPlacementSite(placement);
                    output.Model.AddElements(plantElements);
                }
            }

            return output;
        }

        private static IList<Polygon> GetPlantSitesInRoom(SpaceBoundary room,
                                                                int estimatedCountOfPlants,
                                                                IList<BBox3> obstacles)
        {
            if (estimatedCountOfPlants == 0)
            {
                return new List<Polygon>();
            }

            double offset = gapBetweenPlantAndWall + 0.5 * plantWidth;
            var roomPolygonWithOffset = room.Boundary.Perimeter.Offset(-offset);

            var wallLines = roomPolygonWithOffset.SelectMany(or => or.Segments()).ToList();

            (var plantableLines, var cornerPlantSites) = ExtractPlantableLinesAndCornersSites(wallLines, obstacles);

            int cornerPlantSitesCount = cornerPlantSites.Count;

            if (estimatedCountOfPlants <= cornerPlantSitesCount)
            {
                return cornerPlantSites.GetRange(0, estimatedCountOfPlants);
            }

            var lineToSitesCount = GetLineToPlantSitesCount(plantableLines, estimatedCountOfPlants - cornerPlantSitesCount);
            var plantableSites = plantableLines.SelectMany(wl => GetPlantSitesAlongLine(wl, lineToSitesCount[wl]));
            return cornerPlantSites.Concat(plantableSites).ToList();
        }

        private static (IList<Line> plantableLines, List<Polygon> cornerPlantSites) ExtractPlantableLinesAndCornersSites(IList<Line> lines,
                                                                                                                          IList<BBox3> obstacles)
        {
            var plantableLines = new List<Line>();
            var cornerPlantSites = new List<Polygon>();

            double plantableLength = plantLength + minGapBetweenPlants;
            foreach (var wallLine in lines)
            {
                var cornerPlantSite = GetPlantSiteAtPoint(wallLine.Start, wallLine.Direction());
                var cornerPlantSiteBBox = cornerPlantSite.Bounds();

                if (!obstacles.Any(o => o.Intersects(cornerPlantSiteBBox)))
                {
                    cornerPlantSites.Add(cornerPlantSite);
                    // Add plant sites to obstacles to avoid plants intersections.
                    obstacles.Add(cornerPlantSiteBBox);
                }

                if (wallLine.Length() < plantableLength + Vector3.EPSILON)
                {
                    continue;
                }

                var plantLengthVector = 0.5 * plantableLength * wallLine.Direction();
                var lineWithoutCorners = new Line(wallLine.Start + plantLengthVector, wallLine.End - plantLengthVector);
                var linesWithoutObstacles = DivideLineIntoPlantableLines(lineWithoutCorners, obstacles);

                if (!linesWithoutObstacles.Any())
                {
                    continue;
                }

                // Add line polygon to obstacles to avoid plants intersections.
                obstacles.Add(lineWithoutCorners.Thicken(plantWidth).Bounds());

                plantableLines.AddRange(linesWithoutObstacles);
            }

            return (plantableLines, cornerPlantSites);
        }

        private static Dictionary<Line, int> GetLineToPlantSitesCount(IList<Line> plantableLines, int countOfPlants)
        {
            var lineToSitesCount = new Dictionary<Line, int>();
            var lineToMaxSitesCount = new Dictionary<Line, int>();
            double minStepLength = plantLength + minGapBetweenPlants;
            double plantableLength = plantableLines.Sum(line => line.Length());

            foreach (var line in plantableLines)
            {
                double lineLength = line.Length();
                lineToMaxSitesCount[line] = (int)Math.Floor((lineLength - plantLength) / minStepLength + 1);
                int calculatedCountOfPlants = (int)Math.Floor(countOfPlants * lineLength / plantableLength);
                lineToSitesCount[line] = Math.Min(lineToMaxSitesCount[line], calculatedCountOfPlants);
            }

            var currentPlantsCount = lineToSitesCount.Values.Sum();
            var maxPlantsCount = lineToMaxSitesCount.Values.Sum();
            var maxAchievablePlantsCount = Math.Min(maxPlantsCount, countOfPlants);

            if (maxAchievablePlantsCount > currentPlantsCount)
            {
                AdjustSitesCountToEstimation(lineToSitesCount,
                                             lineToMaxSitesCount,
                                             currentPlantsCount,
                                             maxAchievablePlantsCount);
            }

            return lineToSitesCount;
        }

        private static void AdjustSitesCountToEstimation(Dictionary<Line, int> lineToSitesCount,
                                                         Dictionary<Line, int> lineToMaxSitesCount,
                                                         int currentSitesCount,
                                                         int maxPossibleSitesCount)
        {
            for (var i = 0; i < maxPossibleSitesCount - currentSitesCount; i++)
            {
                var bestLine = lineToSitesCount.Keys.MaxBy(line => lineToMaxSitesCount[line] - lineToSitesCount[line]);

                if (bestLine == null || lineToMaxSitesCount[bestLine] - lineToSitesCount[bestLine] < 1)
                {
                    break;
                }

                lineToSitesCount[bestLine]++;
            }
        }

        private static IList<Polygon> GetPlantSitesAlongLine(Line line, int countOfPlants)
        {
            if (countOfPlants < 1)
            {
                return new List<Polygon>();
            }

            return line.DivideIntoEqualSegments(countOfPlants).Select(segm => GetPlantSiteAtLineCenter(segm)).ToList();
        }

        private static IList<Line> DivideLineIntoPlantableLines(Line line, IList<BBox3> obstacles)
        {
            var lines = new List<Line>();
            var splitPoints = GetLineSplitPoints(line, obstacles);

            if (!splitPoints.Any())
            {
                lines.Add(line);
                return lines;
            }

            var p1 = line.Start;
            foreach (var (minT, maxT) in splitPoints) 
            {
                Vector3 p2 = line.PointAt(minT);

                if (p1 != p2)
                {
                    lines.Add(new Line(p1, p2));
                }

                p1 = line.PointAt(maxT);
            }

            if (p1 != line.End)
            {
                lines.Add(new Line(p1, line.End));
            }

            return lines.Where(l => l.Length() > plantLength + Vector3.EPSILON).ToList();
        }

        private static IEnumerable<(double minT, double maxT)> GetLineSplitPoints(Line line, IList<BBox3> obstacles)
        {
            var linePolygon = new Line(line.Start, line.End).Thicken(plantWidth);
            var obstaclePolygons = obstacles.Select(bbox => BBox3ToXYPolygon(bbox)).ToList();
            var intersections = Polygon.Intersection(new List<Polygon>() { linePolygon }, obstaclePolygons);

            if (intersections == null)
            {
                return Enumerable.Empty<(double minT, double maxT)>();
            }

            var splitPoints = intersections.Select(intersection => ProjectPolygonToLine(intersection, line)).ToList();
            return MergeIntersectingIntervals(splitPoints);
        }

        private static IEnumerable<(double minT, double maxT)> MergeIntersectingIntervals(
            IEnumerable<(double minT, double maxT)> ranges)
        {
            if (!ranges.Any())
            {
                return Enumerable.Empty<(double minT, double maxT)>();
            }

            var orderedIntervals = ranges.OrderBy(r => r.minT).ThenBy(r => r.maxT);
            (double minT, double maxT) prevInterval = orderedIntervals.First();
            var result = new List<(double minT, double maxT)>();

            foreach (var currInterval in orderedIntervals.Skip(1))
            {
                if (currInterval.minT <= prevInterval.maxT)
                {
                    prevInterval.maxT = Math.Max(currInterval.maxT, prevInterval.maxT);
                } 
                else
                {
                    result.Add(prevInterval);
                    prevInterval = currInterval;
                }
            }

            if (!result.Any() || result.Last() != prevInterval)
            {
                result.Add(prevInterval);
            }

            return result;
        }

        private static (double minT, double maxT) ProjectPolygonToLine(Polygon polygon, Line line)
        {
            double minT = double.MaxValue;
            double maxT = double.MinValue;

            foreach (var vertex in polygon.Vertices)
            {
                double t = line.GetParameterAt(vertex.ClosestPointOn(line));

                if (t < minT)
                {
                    minT = t;
                }

                if (t > maxT)
                {
                    maxT = t;
                }
            }

            return (minT, maxT);
        }

        private static Polygon BBox3ToXYPolygon(BBox3 bBox)
        {
            var polygon = new Polygon(new List<Vector3>
                {
                    new Vector3(bBox.Min.X, bBox.Min.Y, bBox.Min.Z),
                    new Vector3(bBox.Min.X, bBox.Max.Y, bBox.Min.Z),
                    new Vector3(bBox.Max.X, bBox.Max.Y, bBox.Min.Z),
                    new Vector3(bBox.Max.X, bBox.Min.Y, bBox.Min.Z)
                });

            return polygon;
        }

        private static Element[] CreatePlantElementsAtPlacementSite(Polygon plantSite)
        {
            ContentElement plantCE = Plants.DFlowersAndVase3DFlowersAndVase;
            BBox3 plantCEBBox = plantCE.BoundingBox;
            Vector3 bboxCenter = plantCEBBox.Center();
            var segments = plantSite.Segments();
            var dummyTransform = new Transform(plantSite.Center(), segments[0].Direction(), segments[1].Direction(), Vector3.ZAxis);
            var plantTransform = new Transform(-bboxCenter.X, -bboxCenter.Y, 0).Concatenated(dummyTransform);
            var plant = plantCE.CreateInstance(plantTransform, "Plant");
            var plantDummy = new Plant(plantLength, plantWidth, plantHeight, dummyTransform);
            return new Element[] { plant, plantDummy };
        }

        // lengthDir should be unitized first
        private static Polygon GetPlantSiteAtPoint(Vector3 point, Vector3 lengthDir)
        {
            var halfLengthDir = 0.5 * plantLength * lengthDir;
            return new Line(point - halfLengthDir, point + halfLengthDir).Thicken(plantWidth);
        }

        private static Polygon GetPlantSiteAtLineCenter(Line line)
        {
            return GetPlantSiteAtPoint(line.Mid(), line.Direction());
        }

        private static IList<BBox3> GetObstaclesFromModel(Dictionary<string, Model> inputModels, string modelName)
        {
            if (!inputModels.TryGetValue(modelName, out var inputModel))
            {
                return new List<BBox3>();
            }

            var obstacles = inputModel.AllElementsOfType<ElementInstance>();
            return obstacles.Select(obstacle => GetElementIstanceBBox(obstacle)).ToList();
        }

        private static BBox3 GetElementIstanceBBox(ElementInstance obstacleInstance)
        {
            var baseDefinition = (ContentElement)obstacleInstance.BaseDefinition;
            var transform = obstacleInstance.Transform;
            return new BBox3(baseDefinition.BoundingBox.Corners().Select(c => transform.OfPoint(c)));
        }
    }
}