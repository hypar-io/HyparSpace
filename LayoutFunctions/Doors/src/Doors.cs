using Elements;
using Elements.Geometry;
using LayoutFunctionCommon;
using static Doors.DoorOpeningEnumsHelper;

namespace Doors
{
    public static class Doors
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="model">The input model.</param>
        /// <param name="input">The arguments to the execution.</param>
        /// <returns>A DoorsOutputs instance containing computed results and the model with any new elements.</returns>
        public static DoorsOutputs Execute(Dictionary<string, Model> inputModels, DoorsInputs input)
        {
            var rooms = GetSpaceBoundaries(inputModels);
            var corridors = GetCirculationSegments(inputModels);
            var walls = GetWalls(inputModels);
            var doors = new List<Door>();

            foreach (var roomsOfLevel in rooms.GroupBy(r => r.Level))
            {
                var levelCorridors = corridors.Where(c => c.Level == roomsOfLevel.Key);
                var levelCorridorsSegments = levelCorridors.SelectMany(
                    c => c.Profile.Transformed(c.Transform).Segments()).ToList();
                foreach (var room in rooms)
                {
                    var pair = RoomDefaultDoorWall(room, levelCorridorsSegments, walls);
                    if (pair == null)
                    {
                        continue;
                    }

                    var wall = pair.Value.Wall;
                    var openingSide = ConvertOpeningSideEnum(input.DefaultDoorOpeningSide);
                    var openingType = ConvertOpeningTypeEnum(input.DefaultDoorOpeningType);
                    var doorOriginalPosition = pair.Value.Segment.Mid();
                    var doorCurrentPosition = doorOriginalPosition;
                    var doorOverride = input.Overrides.DoorPositions.FirstOrDefault(
                        o => doorOriginalPosition.IsAlmostEqualTo(o.Identity.OriginalPosition));

                    if (doorOverride != null && doorOverride.Value.Transform != null)
                    {
                        doorCurrentPosition = doorOverride.Value.Transform.Origin;
                        openingSide = ConvertOpeningSideEnum(doorOverride.Value.DefaultDoorOpeningSide);
                        openingType = ConvertOpeningTypeEnum(doorOverride.Value.DefaultDoorOpeningType);
                        wall = GetClosestWall(doorCurrentPosition, walls, out _);
                    }

                    double width = doorOverride?.Value.DoorWidth ?? input.DefaultDoorWidth;
                    double height = doorOverride?.Value.DoorHeight ?? input.DefaultDoorHeight;

                    var door = CreateDoor(wall, doorOriginalPosition, doorCurrentPosition, width, height, openingSide, openingType, doorOverride);
                    if (door != null)
                    {
                        doors.Add(door);
                    }
                }
            }

            AddDoors(doors, input.Overrides.Additions.DoorPositions, walls, input.Overrides);
            RemoveDoors(doors, input.Overrides.Removals.DoorPositions);

            var output = new DoorsOutputs();
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

        private static List<StandardWall> GetWalls(Dictionary<string, Model> inputModels)
        {
            if (!inputModels.TryGetValue("Interior Partitions", out Model? interiorPartitions))
            {
                throw new ArgumentException("Interior Partitions is missing");
            }
            var wallCandidates = interiorPartitions.AllElementsOfType<WallCandidate>().ToList();

            var wallLinesToWalls = new Dictionary<Line, StandardWall>();
            var allWalls = interiorPartitions.AllElementsOfType<StandardWall>().Distinct().ToList();

            foreach (var wall in allWalls)
            {
                // It is not the main wall. Most likely it is a Header, which cannot contain a door.
                if (wall.AdditionalProperties.ContainsKey("Wall"))
                {
                    continue;
                }

                if (wallLinesToWalls.TryGetValue(wall.CenterLine, out var secondWall) && wall.Height.Equals(secondWall.Height))
                {
                    // In this case two different walls have the same centerline. Pick the first one and proceed.
                    continue;
                }

                wallLinesToWalls.Add(wall.CenterLine, wall);
            }

            var walls = wallCandidates.Where(wc => wallLinesToWalls.ContainsKey(wc.Line))
                .Select(wc => wallLinesToWalls[wc.Line]).ToList();

            return walls;
        }

        private static (Line Segment, StandardWall Wall)? RoomDefaultDoorWall(
            SpaceBoundary room,
            IEnumerable<Line> corridorsSegments,
            IEnumerable<StandardWall> walls)
        {
            List<Line>? corridorEdges = RoomCorridorEdges(room, corridorsSegments);
            if (corridorEdges == null || !corridorEdges.Any())
            {
                return null;
            }

            var roomWalls = GetRoomWalls(corridorEdges, walls);
            // Where are no walls or not all corridor edges are covered by walls.
            // There is open passage so no default door required.
            if (!roomWalls.Any() || roomWalls.Count < corridorEdges.Count)
            {
                return null;
            }
            
            var wall = RoomLongestWall(room, roomWalls);
            return wall;
        }

        private static void AddDoors(List<Door> doors, 
                                     IEnumerable<DoorPositionsOverrideAddition> additions,
                                     List<StandardWall> walls,
                                     Overrides overrides)
        {
            foreach (var addition in additions)
            {
                var originalPosition = addition.Value.Transform.Origin;
                var openingSide = ConvertOpeningSideEnum(addition.Value.DoorOpeningSide);
                var openingType = ConvertOpeningTypeEnum(addition.Value.DoorOpeningType);
                var wall = GetClosestWall(originalPosition, walls, out originalPosition);
                if (wall == null)
                {
                    continue;
                }

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
                    openingSide = ConvertOpeningSideEnum(doorOverride.Value.DefaultDoorOpeningSide);
                    openingType = ConvertOpeningTypeEnum(doorOverride.Value.DefaultDoorOpeningType);
                }

                var door = CreateDoor(wall, originalPosition, currentPosition, width, height, openingSide, openingType, doorOverride);
                if (door != null)
                {
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
            var corridorEdges = WallGeneration.FindAllEdgesAdjacentToSegments(roomSegments, corridorSegments, out _)
                .Select(edge => edge.Line).ToList();
            return corridorEdges;
        }

        private static List<(Line, StandardWall)> GetRoomWalls(List<Line> corridorEdges,
                                                               IEnumerable<StandardWall> wallCandidates)
        {
            var roomWalls = new List<(Line, StandardWall)>();
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

        private static (Line, StandardWall)? RoomLongestWall(
            SpaceBoundary room,
            IEnumerable<(Line CorridorEdge, StandardWall Wall)> roomWalls)
        {
            double maxLength = 0;
            (Line, StandardWall)? longestWall = null;

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

        private static bool IsWallCoverRoomSegment(Line segment, StandardWall wall)
        {
            const double wallToRoomMatchTolerance = 1e-3;
            return wall.CenterLine.PointOnLine(segment.Start, true, wallToRoomMatchTolerance) &&
                   wall.CenterLine.PointOnLine(segment.End, true, wallToRoomMatchTolerance);
        }

        private static Door? CreateDoor(StandardWall? wall,
                                        Vector3 originalPosition,
                                        Vector3 currentPosition,
                                        double width,
                                        double height,
                                        DoorOpeningSide openingSide,
                                        DoorOpeningType openingType,
                                        DoorPositionsOverride? doorOverride = null)
        {
            if (wall == null || !Door.CanFit(wall.CenterLine, openingSide, width))
            {
                return null;
            }

            var door = new Door(wall, wall.CenterLine, originalPosition, currentPosition, width, height, openingSide, openingType);

            if (doorOverride != null)
            {
                door.AddOverrideIdentity(doorOverride);
            }
                    
            return door;
        }

        private static StandardWall? GetClosestWall(Vector3 pos,
                                                     IEnumerable<StandardWall> walls,
                                                     out Vector3 closestPoint)
        {
            closestPoint = Vector3.Origin;
            if (walls == null || !walls.Any())
            {
                return null;
            }

            double minDist = Double.MaxValue;
            StandardWall? closestWall = null;

            foreach (var wall in walls)
            {
                double dist = pos.DistanceTo(wall.CenterLine, out var p);

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