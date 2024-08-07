using System.Runtime.Serialization.Formatters;
using Elements;
using Elements.Geometry;
using IFC;
using LayoutFunctionCommon;
using static Doors.DoorOpeningEnumsHelper;

namespace Doors
{
    public static class Doors
    {
        // Door offset from end of wall. Determines initial position.
        private const double doorOffset = 9 * 0.0254;
        private static Material glassMat = new Material("Glass", new Color(0.7, 0.7, 0.7, 0.3), 0.3, 0.6);
        /// <summary>
        ///
        /// </summary>
        /// <param name="model">The input model.</param>
        /// <param name="input">The arguments to the execution.</param>
        /// <returns>A DoorsOutputs instance containing computed results and the model with any new elements.</returns>
        public static DoorsOutputs Execute(Dictionary<string, Model> inputModels, DoorsInputs input)
        {
            var output = new DoorsOutputs();

            DoorRepresentationStorage.Doors.Clear();

            var allSpaces = GetSpaceBoundaries(inputModels);

            var rooms = allSpaces.Where(x => x.ProgramType != "Circulation").ToList();
            var circulationSpaces = allSpaces.Where(x => x.ProgramType == "Circulation").ToList();

            var corridors = GetCirculationSegments(inputModels);
            var walls = GetWallCandidates(inputModels);
            var doors = new List<Door>();

            var roomGroups = rooms.GroupBy(r => r.Level);

            foreach (var roomsOfLevel in roomGroups)
            {
                var levelCorridors = corridors.Where(c => c.Level == roomsOfLevel.Key);
                var levelCorridorsSegments = levelCorridors.SelectMany(
                    c => c.Profile.Transformed(c.Transform).Segments()).ToList();

                // Get circulation space boundary segments
                var levelCorridorSpaces = circulationSpaces.Where(x => x.Level == roomsOfLevel.Key);
                var levelCorridorSpaceSegments = levelCorridorSpaces.SelectMany(x => x.Boundary.Perimeter.Segments()).ToList();
                levelCorridorsSegments.AddRange(levelCorridorSpaceSegments);

                foreach (var room in roomsOfLevel)
                {
                    var pair = RoomDefaultDoorWall(room, levelCorridorsSegments, walls);
                    if (pair == null || pair.Value.Wall == null || pair.Value.Segment == null)
                    {
                        continue;
                    }

                    var wall = pair.Value.Wall;
                    var openingSide = ConvertOpeningSideEnum(input.DefaultDoorOpeningSide);
                    var openingType = ConvertOpeningTypeEnum(input.DefaultDoorOpeningType);

                    var doorType = "Solid";
                    if (room.DefaultWallType == "Glass") doorType = "Glass";

                    if (!wall.Thickness.HasValue) continue;
                    var wallThickness = wall.Thickness.Value.innerWidth + wall.Thickness.Value.outerWidth;

                    // Don't add door if the wall is zero thickness.
                    if (wallThickness == 0.0) continue;

                    var wallLineForAdjacentEdges = new List<RoomEdge> { wall };
                    var corridorLines = new List<Line>();
                    WallGeneration.FindAllEdgesAdjacentToSegments(wallLineForAdjacentEdges, levelCorridorsSegments, out _, out corridorLines).Select(edge => edge.Line);

                    var corridorEdge = corridorLines.OrderByDescending(x => x.Length()).FirstOrDefault();

                    if (corridorEdge == null) continue;

                    var closestStart = corridorEdge.Start.ClosestPointOn(wall.Line);
                    var closestEnd = corridorEdge.End.ClosestPointOn(wall.Line);

                    if (closestStart == closestEnd) continue;

                    var doorPlacementLine = new Line(closestStart, closestEnd);

                    if (doorPlacementLine.Direction().Dot(wall.Line.Direction()) < 0) doorPlacementLine = doorPlacementLine.Reversed();

                    // Don't add door if the wall length is too short.
                    if (doorPlacementLine.Length() < doorOffset + input.DefaultDoorWidth) continue;

                    var doorPlacementLineMidPoint = doorPlacementLine.Mid();
                    var doorWallSideParam = wall.Line.GetParameterAt(doorPlacementLineMidPoint);

                    var doorOriginalPosition = doorPlacementLine.PointAt(doorOffset + input.DefaultDoorWidth / 2);

                    if (doorWallSideParam / wall.Line.Length() > 0.55)
                    {
                        doorOriginalPosition = doorPlacementLine.PointAt(doorPlacementLine.Length() - (doorOffset + input.DefaultDoorWidth / 2));
                        openingSide = DoorOpeningSide.RightHand;
                    }

                    var doorCurrentPosition = doorOriginalPosition;
                    var doorOverride = input.Overrides.DoorPositions.FirstOrDefault(
                        o => doorOriginalPosition.IsAlmostEqualTo(o.Identity.OriginalPosition));

                    if (doorOverride != null && doorOverride.Value.Transform != null)
                    {
                        doorCurrentPosition = doorOverride.Value.Transform.Origin;
                        openingSide = ConvertOpeningSideEnum(doorOverride.Value.DoorOpeningSide);
                        openingType = ConvertOpeningTypeEnum(doorOverride.Value.DoorOpeningType);
                        doorType = doorOverride.Value.DoorType.ToString();
                        wall = GetClosestWallCandidate(doorCurrentPosition, walls, out doorCurrentPosition);
                    }

                    double height = doorOverride?.Value.DoorHeight ?? input.DefaultDoorHeight;
                    double width = doorOverride?.Value.DoorWidth ?? input.DefaultDoorWidth;

                    var doorThickness = doorType == "Glass" ? Units.InchesToMeters(1) : Door.DEFAULT_DOOR_THICKNESS;

                    var door = CreateDoor(wall, doorOriginalPosition, doorCurrentPosition, width, height, doorThickness, openingSide, openingType, doorOverride);

                    if (door != null)
                    {
                        if (doorType == "Solid")
                        {
                            door.Material = BuiltInMaterials.Default;
                            door.FrameDepth = wall == null ? Units.InchesToMeters(7) : wall.Thickness.Value.outerWidth + wall.Thickness.Value.innerWidth + Units.InchesToMeters(1);
                            door.DoorType = "Solid";
                        }
                        else if (doorType == "Glass")
                        {
                            door.Material = glassMat;
                            door.FrameDepth = 0;
                            door.FrameWidth = 0;
                            door.DoorType = "Glass";
                        }

                        doors.Add(door);
                    }
                }
            }

            AddDoors(doors, input.Overrides.Additions.DoorPositions, walls, input.Overrides);
            RemoveDoors(doors, input.Overrides.Removals.DoorPositions);

            output.Model.AddElements(doors);
            return output;
        }

        private static List<SpaceBoundary> GetSpaceBoundaries(Dictionary<string, Model> inputModels)
        {
            if (!inputModels.TryGetValue("Space Planning Zones", out Model? spacePlanningZones))
            {
                throw new ArgumentException("Space Planning Zones is missing");
            }
            var rooms = spacePlanningZones.AllElementsOfType<SpaceBoundary>();
            return rooms.ToList();
        }

        private static List<CirculationSegment> GetCirculationSegments(Dictionary<string, Model> inputModels)
        {
            if (!inputModels.TryGetValue("Circulation", out Model? circulation))
            {
                throw new ArgumentException("Circulation is missing");
            }
            var corridors = circulation.AllElementsOfType<CirculationSegment>();
            return corridors.ToList();
        }

        private static List<RoomEdge> GetWallCandidates(Dictionary<string, Model> inputModels)
        {
            var interiorPartitionCandidates = new List<InteriorPartitionCandidate>();
            var modelDependencies = new[] {
                "Private Office Layout",
                "Phone Booth Layout",
                "Classroom Layout",
                "Meeting Room Layout",
                "Space Planning Zones",
                "Bedroom Layout",
                "Living Room Layout",
                "Kitchen Layout",
                "Workshop Layout",
                "Home Office Layout",
                "Bathroom Layout",
                "Restroom Layout",
                "Laundry Room Layout",
                "Entertainment Room Layout",
                "Room Layout",
                "Furniture and Equipment"
                 };
            foreach (var md in modelDependencies)
            {
                if (inputModels.TryGetValue(md, out var mdModel))
                {
                    var elements = mdModel?.AllElementsOfType<InteriorPartitionCandidate>();
                    if (elements != null)
                    {
                        interiorPartitionCandidates.AddRange(elements);
                    }
                }
            }

            var roomEdges = interiorPartitionCandidates.SelectMany(wc => wc.WallCandidateLines).ToList();

            return roomEdges;
        }

        private static (Line Segment, RoomEdge Wall)? RoomDefaultDoorWall(
            SpaceBoundary room,
            IEnumerable<Line> corridorsSegments,
            IEnumerable<RoomEdge> wallCandidates)
        {
            List<Line>? corridorEdges = RoomCorridorEdges(room, corridorsSegments);
            if (corridorEdges == null || !corridorEdges.Any())
            {
                return null;
            }

            var roomWalls = GetRoomWallCandidates(corridorEdges, wallCandidates);
            // Where are no walls or not all corridor edges are covered by walls.
            // There is open passage so no default door required.
            if (!roomWalls.Any() || roomWalls.Count < corridorEdges.Count)
            {
                return null;
            }

            var wallCorridors = new List<(Line, RoomEdge)>();

            // Creating a list of room walls that can be sorted by the length of corridor next to it (since it isn't always the full length of wall)
            foreach (var wallCheck in roomWalls)
            {
                var wallsNearCorridor = new List<RoomEdge> { wallCheck.Item2 };

                var corridorLines = new List<Line>();
                var roomLines = WallGeneration.FindAllEdgesAdjacentToSegments(wallsNearCorridor, corridorsSegments, out _, out corridorLines).ToList();

                var corridorEdge = corridorLines.OrderByDescending(x => x.Length()).FirstOrDefault();

                if (corridorEdge == null) continue;

                var closestStart = corridorEdge.Start.ClosestPointOn(wallCheck.Item2.Line);
                var closestEnd = corridorEdge.End.ClosestPointOn(wallCheck.Item2.Line);

                if (closestStart == closestEnd) continue;

                var doorPlacementLine = new Line(closestStart, closestEnd);

                var wallCorridor = (doorPlacementLine, wallCheck.Item2);
                wallCorridors.Add(wallCorridor);
            }

            var wall = wallCorridors.OrderByDescending(x => x.Item1.Length()).FirstOrDefault();


            var primaryEdges = wallCorridors.Where(x => x.Item2.PrimaryEntryEdge == true).ToList();
            if (primaryEdges.Count > 0)
            {
                wall = primaryEdges.OrderByDescending(x => x.Item1.Length()).FirstOrDefault();
            }

            return wall;
        }

        private static void AddDoors(List<Door> doors,
                                     IEnumerable<DoorPositionsOverrideAddition> additions,
                                     List<RoomEdge> walls,
                                     Overrides overrides)
        {
            foreach (var addition in additions)
            {
                var originalPosition = addition.Value.Transform.Origin;
                var openingSide = ConvertOpeningSideEnum(addition.Value.DoorOpeningSide);
                var openingType = ConvertOpeningTypeEnum(addition.Value.DoorOpeningType);
                var wall = GetClosestWallCandidate(originalPosition, walls, out originalPosition);
                // if (wall == null)
                // {
                //     continue;
                // }

                string? doorType = wall?.Type;

                var currentPosition = originalPosition;
                var doorOverride = overrides.DoorPositions.FirstOrDefault(
                    o => originalPosition.IsAlmostEqualTo(o.Identity.OriginalPosition));
                double width = addition.Value.DoorWidth;
                double height = addition.Value.DoorHeight;
                if (doorOverride != null && doorOverride.Value.Transform != null)
                {
                    currentPosition = doorOverride.Value.Transform.Origin;
                    width = doorOverride.Value.DoorWidth;
                    height = doorOverride.Value.DoorHeight;
                    wall = GetClosestWallCandidate(currentPosition, walls, out currentPosition);
                    openingSide = ConvertOpeningSideEnum(doorOverride.Value.DoorOpeningSide);
                    openingType = ConvertOpeningTypeEnum(doorOverride.Value.DoorOpeningType);
                    doorType = doorOverride.Value.DoorType.ToString();
                }

                var doorThickness = doorType == "Glass" ? 1 * 0.0254 : Door.DEFAULT_DOOR_THICKNESS;

                var door = CreateDoor(wall, originalPosition, currentPosition, width, height, doorThickness, openingSide, openingType, doorOverride);
                if (door != null)
                {
                    if (doorType == "Solid") door.Material = BuiltInMaterials.Default;
                    else if (doorType == "Glass")
                    {
                        door.Material = glassMat;
                        door.FrameDepth = 0;
                        door.FrameWidth = 0;
                        door.DoorType = "Glass";
                    }
                    doors.Add(door);
                }
            }
        }

        private static void RemoveDoors(List<Door> doors,
                                        IEnumerable<DoorPositionsOverrideRemoval> removals)
        {
            foreach (var removal in removals)
            {
                var item = doors.FirstOrDefault(
                    d => d.OriginalPosition.Equals(removal.Identity.OriginalPosition));
                if (item != null)
                {
                    doors.Remove(item);
                }
            }
        }

        private static List<Line>? RoomCorridorEdges(SpaceBoundary room, IEnumerable<Line> corridorSegments)
        {
            var roomSegments = room.Boundary.Perimeter.CollinearPointsRemoved().Segments().Select(
                s => s.TransformedLine(room.Transform)).Select(l => new RoomEdge() { Line = l });
            var corridorEdges = WallGeneration.FindAllEdgesAdjacentToSegments(roomSegments, corridorSegments, out _, out _)
                .Select(edge => edge.Line).ToList();
            return corridorEdges;
        }

        private static List<(Line, RoomEdge)> GetRoomWallCandidates(List<Line> corridorEdges,
                                                               IEnumerable<RoomEdge> wallCandidates)
        {
            var roomWalls = new List<(Line, RoomEdge)>();
            foreach (var rs in corridorEdges)
            {
                var wall = wallCandidates.FirstOrDefault(wc => IsWallCoverRoomSegment(rs, wc));
                if (wall != null)
                {
                    roomWalls.Add((rs, wall));
                }
            }
            return roomWalls;
        }

        private static (Line, RoomEdge)? RoomLongestWallCandidate(
            SpaceBoundary room,
            IEnumerable<(Line CorridorEdge, RoomEdge Wall)> roomWalls)
        {
            double maxLength = 0;
            (Line, RoomEdge)? longestWall = null;

            foreach (var (edge, wall) in roomWalls)
            {
                var wallLength = edge.Length();
                if (wallLength > maxLength)
                {
                    maxLength = wallLength;
                    longestWall = (edge, wall);
                }
            }
            return longestWall;
        }

        private static bool IsWallCoverRoomSegment(Line segment, RoomEdge wallCandidate)
        {
            const double wallToRoomMatchTolerance = 1e-3;
            return wallCandidate.Line.PointOnLine(segment.Start, true, wallToRoomMatchTolerance) &&
                   wallCandidate.Line.PointOnLine(segment.End, true, wallToRoomMatchTolerance);
        }

        private static Door? CreateDoor(RoomEdge? wallCandidate,
                                        Vector3 originalPosition,
                                        Vector3 currentPosition,
                                        double width,
                                        double height,
                                        double thickness,
                                        DoorOpeningSide openingSide,
                                        DoorOpeningType openingType,
                                        DoorPositionsOverride? doorOverride = null)
        {
            double rotation = 0;
            if (wallCandidate != null) rotation = Vector3.XAxis.PlaneAngleTo(wallCandidate.Direction.Negate());

            var door = new Door(width, height, thickness, openingSide, openingType)
            {
                OriginalPosition = originalPosition,
                Transform = new Transform(currentPosition).RotatedAboutPoint(currentPosition, Vector3.ZAxis, rotation)
            };

            // if (wallCandidate == null || !CanFit(wallCandidate.Line, door))
            // {
            //     return null;
            // }

            if (doorOverride != null)
            {
                door.AddOverrideIdentity(doorOverride);
            }

            return door;
        }

        /// <summary>
        /// Checks if the door can fit into the wall with the center line @<paramref name="wallLine"/>.
        /// </summary>
        private static bool CanFit(Line wallLine, Door door)
        {
            var doorWidth = GetDoorFullWidthWithoutFrame(door) + door.FrameWidth * 2;
            return wallLine.Length() - doorWidth > door.FrameWidth * 2;
        }

        private static double GetDoorFullWidthWithoutFrame(Door door)
        {
            switch (door.OpeningSide)
            {
                case DoorOpeningSide.LeftHand:
                case DoorOpeningSide.RightHand:
                    return door.DoorWidth;
                case DoorOpeningSide.DoubleDoor:
                    return door.DoorWidth * 2;
            }
            return 0;
        }

        private static RoomEdge? GetClosestWallCandidate(Vector3 pos,
                                                     IEnumerable<RoomEdge> wallCandidates,
                                                     out Vector3 closestPoint)
        {
            closestPoint = pos;
            if (wallCandidates == null || !wallCandidates.Any())
            {
                return null;
            }

            double minDist = 1;
            RoomEdge? closestWall = null;

            foreach (var wall in wallCandidates)
            {
                double dist = pos.DistanceTo(wall.Line, out var p);

                if (dist < minDist)
                {
                    closestWall = wall;
                    minDist = dist;
                    closestPoint = p;
                }
            }

            return closestWall;
        }
    }
}