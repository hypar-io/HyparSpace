using Elements;
using Elements.Geometry;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using LayoutFunctionCommon;

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
            var wallCandidates = GetWallCandidates(inputModels);
            var doors = new List<Door>();

            foreach (var roomsOfLevel in rooms.GroupBy(r => r.Level))
            {
                var levelCorridors = corridors.Where(c => c.Level == roomsOfLevel.Key);
                var levelCorridorsSegments = levelCorridors.SelectMany(
                    c => c.Profile.Transformed(c.Transform).Segments()).ToList();
                foreach (var room in rooms)
                {
                    var pair = RoomDefaultDoorWall(room, levelCorridorsSegments, wallCandidates);
                    if (pair == null)
                    {
                        continue;
                    }

                    var wall = pair.Value.Wall;
                    var type = (DoorType)input.Type;
                    var doorPosition = pair.Value.Segment.Mid();
                    var doorOverride = input.Overrides.DoorPositions.FirstOrDefault(
                        o => doorPosition.IsAlmostEqualTo(o.Identity.OriginalPosition));

                    if (doorOverride != null && doorOverride.Value.Position != null)
                    {
                        doorPosition = doorOverride.Value.Position.Origin;
                        type = (DoorType)doorOverride.Value.Type;
                        wall = GetClosestWall(doorPosition, wallCandidates, out _);
                    }

                    double width = doorOverride?.Value.ClearWidth ?? input.ClearWidth;
                    double height = doorOverride?.Value.ClearHeight ?? input.ClearHeight;

                    var door = CreateDoor(wall, doorPosition, type, width, height, doorOverride);
                    if (door != null)
                    {
                        doors.Add(door);
                    }
                }
            }

            AddDoors(doors, input.Overrides.Additions.DoorPositions, wallCandidates, input.Overrides);
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

        private static List<WallCandidate> GetWallCandidates(Dictionary<string, Model> inputModels)
        {
            if (!inputModels.TryGetValue("Interior Partitions", out Model? interiorPartitions))
            {
                throw new ArgumentException("Interior Partitions is missing");
            }
            var wallCandidates = interiorPartitions.AllElementsOfType<WallCandidate>().ToList();
            return wallCandidates;
        }

        private static (Line Segment, WallCandidate Wall)? RoomDefaultDoorWall(
            SpaceBoundary room,
            IEnumerable<Line> corridorsSegments,
            IEnumerable<WallCandidate> wallCandidates)
        {
            List<Line>? corridorEdges = RoomCorridorEdges(room, corridorsSegments);
            if (corridorEdges == null || !corridorEdges.Any())
            {
                return null;
            }

            var roomWallCandidates = RoomWallCandidates(corridorEdges, wallCandidates);
            // Where are no walls or not all corridor edges are covered by walls.
            // There is open passage so no default door required.
            if (!roomWallCandidates.Any() || roomWallCandidates.Count < corridorEdges.Count)
            {
                return null;
            }
            
            var wall = RoomLongestWall(room, roomWallCandidates);
            return wall;
        }

        private static void AddDoors(List<Door> doors, 
                                     IEnumerable<DoorPositionsOverrideAddition> additions,
                                     List<WallCandidate> wallCandidates,
                                     Overrides overrides)
        {
            foreach (var addition in additions)
            {
                var position = addition.Value.Position.Origin;
                var type = (DoorType)addition.Value.Type;
                var wall = GetClosestWall(position, wallCandidates, out var closest);
                if (wall == null)
                {
                    continue;
                }

                position = closest;
                var doorOverride = overrides.DoorPositions.FirstOrDefault(
                    o => closest.IsAlmostEqualTo(o.Identity.OriginalPosition));
                double width = addition.Value.ClearWidth;
                double height = addition.Value.ClearHeight;
                if (doorOverride != null && doorOverride.Value.Position != null)
                {
                    position = doorOverride.Value.Position.Origin;
                    width = doorOverride.Value.ClearWidth;
                    height = doorOverride.Value.ClearHeight;
                    type = (DoorType)doorOverride.Value.Type;
                }

                var door = CreateDoor(wall, position, type, width, height, doorOverride);
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
                s => s.TransformedLine(room.Transform));
            var corridorEdges = WallGeneration.FindAllEdgesAdjacentToSegments(roomSegments, corridorSegments, out _);
            return corridorEdges;
        }

        private static List<(Line, WallCandidate)> RoomWallCandidates(List<Line> corridorEdges,
                                                                      IEnumerable<WallCandidate> wallCandidates)
        {
            List<(Line, WallCandidate)> roomWalls = new List<(Line, WallCandidate)>();
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

        private static (Line, WallCandidate)? RoomLongestWall(
            SpaceBoundary room,
            IEnumerable<(Line CorridorEdge, WallCandidate Wall)> roomWallCandidates)
        {
            double maxLength = 0;
            (Line, WallCandidate)? longestWall = null;

            foreach (var (edge, wall) in roomWallCandidates)
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

        private static bool IsWallCoverRoomSegment(Line segment, WallCandidate wall)
        {
            const double wallToRoomMatchTolerance = 1e-3;
            return wall.Line.PointOnLine(segment.Start, true, wallToRoomMatchTolerance) &&
                   wall.Line.PointOnLine(segment.End, true, wallToRoomMatchTolerance);
        }

        private static Door? CreateDoor(WallCandidate? wall,
                                        Vector3 position,
                                        DoorType type,
                                        double width,
                                        double height,
                                        DoorPositionsOverride? doorOverride = null)
        {
            if (wall == null || !Door.CanFit(wall.Line, type, width))
            {
                return null;
            }

            var door = new Door(wall, position, type, width, height);

            if (doorOverride != null)
            {
                door.AddOverrideIdentity(doorOverride);
            }
                    
            return door;
        }

        private static WallCandidate? GetClosestWall(Vector3 pos,
                                                     IEnumerable<WallCandidate> wallCandidates,
                                                     out Vector3 closestPoint)
        {
            closestPoint = Vector3.Origin;
            if (wallCandidates == null || !wallCandidates.Any())
            {
                return null;
            }

            double minDist = Double.MaxValue;
            WallCandidate? closestWall = null;

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