using Elements;
using Elements.Geometry;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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
                    var wall = RoomDefaultDoorWall(room, levelCorridorsSegments, wallCandidates);
                    if (wall == null)
                    {
                        continue;
                    }

                    var doorPosition = wall.Line.PointAt(0.5);
                    var doorOverride = input.Overrides.DoorPositions.FirstOrDefault(
                        o => doorPosition.IsAlmostEqualTo(o.Identity.OriginalPosition));

                    if (doorOverride != null && doorOverride.Value.Position != null)
                    {
                        doorPosition = doorOverride.Value.Position.Origin;
                        wall = GetClosestWall(doorPosition, wallCandidates, out _);
                    }

                    double width = doorOverride?.Value.ClearWidth ?? input.DoorWidth;
                    var door = CreateDoor(wall, doorPosition, width, doorOverride);
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

            if (!inputModels.TryGetValue("Meeting Room Layout", out Model? meetingRooms))
            {
                throw new ArgumentException("Interior Partitions is missing");
            }

            if (!inputModels.TryGetValue("Private Office Layout", out Model? privateOffices))
            {
                throw new ArgumentException("Interior Partitions is missing");
            }

            if (!inputModels.TryGetValue("Classroom Layout", out Model? classrooms))
            {
                throw new ArgumentException("Interior Partitions is missing");
            }

            if (!inputModels.TryGetValue("Phone Booth Layout", out Model? phoneBooths))
            {
                throw new ArgumentException("Interior Partitions is missing");
            }

            var wallCandidates = interiorPartitions.AllElementsOfType<WallCandidate>().ToList();
            var c = meetingRooms.AllElementsOfType<InteriorPartitionCandidate>();
            wallCandidates.AddRange(c.SelectMany(wc => wc.WallCandidateLines.Select(wcl => new WallCandidate(wcl.line.TransformedLine(wc.LevelTransform), wcl.type, new List<SpaceBoundary>()))));
            c = privateOffices.AllElementsOfType<InteriorPartitionCandidate>();
            wallCandidates.AddRange(c.SelectMany(wc => wc.WallCandidateLines.Select(wcl => new WallCandidate(wcl.line.TransformedLine(wc.LevelTransform), wcl.type, new List<SpaceBoundary>()))));
            c = classrooms.AllElementsOfType<InteriorPartitionCandidate>();
            wallCandidates.AddRange(c.SelectMany(wc => wc.WallCandidateLines.Select(wcl => new WallCandidate(wcl.line.TransformedLine(wc.LevelTransform), wcl.type, new List<SpaceBoundary>()))));
            c = phoneBooths.AllElementsOfType<InteriorPartitionCandidate>();
            wallCandidates.AddRange(c.SelectMany(wc => wc.WallCandidateLines.Select(wcl => new WallCandidate(wcl.line.TransformedLine(wc.LevelTransform), wcl.type, new List<SpaceBoundary>()))));
            return wallCandidates;
        }

        private static WallCandidate? RoomDefaultDoorWall(SpaceBoundary room,
                                                          IEnumerable<Line> corridorsSegments,
                                                          IEnumerable<WallCandidate> wallCandidates)
        {
            List<Line>? corridorEdges = RoomCorridorEdges(room, corridorsSegments);
            if (corridorEdges == null || !corridorEdges.Any())
            {
                return null;
            }

            List<WallCandidate> roomWallCandidates = RoomWallCandidates(room, wallCandidates);
            if (roomWallCandidates == null || !roomWallCandidates.Any())
            {
                return null;
            }
            
            var wall = RoomLongestWall(room, corridorEdges, roomWallCandidates);
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
                var wall = GetClosestWall(position, wallCandidates, out var closest);

                var doorOverride = overrides.DoorPositions.FirstOrDefault(
                    o => closest.IsAlmostEqualTo(o.Identity.OriginalPosition));
                double width = doorOverride?.Value.ClearWidth ?? addition.Value.ClearWidth;
                if (doorOverride != null && doorOverride.Value.Position != null)
                {
                    position = doorOverride.Value.Position.Origin;
                }

                var door = CreateDoor(wall, position, width, doorOverride);
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
            List<Line>? corridorEdges = null;
            if (room.AdditionalProperties.TryGetValue("AdjacentCorridorEdges", out var jCorridorEdges) && jCorridorEdges != null)
            {
                corridorEdges = ((JArray)jCorridorEdges).ToObject<List<Line>>();
            }
            else
            {
                var roomSegments = room.Boundary.Segments();
                corridorEdges = FindAllEdgesAdjacentToSegments(roomSegments, corridorSegments, out _);
            }
            return corridorEdges;
        }

        private static List<WallCandidate> RoomWallCandidates(SpaceBoundary room,
                                                              IEnumerable<WallCandidate> wallCandidates)
        {
            List<WallCandidate> roomWalls = new List<WallCandidate>();
            var roomSegments = room.Boundary.Segments().Select(s => s.TransformedLine(room.Transform));
            foreach (var rs in roomSegments)
            {
                var wall = wallCandidates.FirstOrDefault(wc => wc.Line.Start.IsAlmostEqualTo(rs.Start) && wc.Line.End.IsAlmostEqualTo(rs.End) ||
                                                               wc.Line.Start.IsAlmostEqualTo(rs.End) && wc.Line.End.IsAlmostEqualTo(rs.Start));
                if (wall != null)
                {
                    roomWalls.Add(wall);
                }
            }
            return roomWalls;
        }

        private static WallCandidate? RoomLongestWall(SpaceBoundary room,
                                                      IEnumerable<Line> corridorEdges,
                                                      IEnumerable<WallCandidate> roomWallCandidates)
        {
            double maxLength = 0;
            WallCandidate? longestWall = null;

            foreach (var edge in corridorEdges)
            {
                var matchingWall = roomWallCandidates.FirstOrDefault(wc => wc.Line.Start.IsAlmostEqualTo(edge.Start) && wc.Line.End.IsAlmostEqualTo(edge.End) ||
                                                                           wc.Line.Start.IsAlmostEqualTo(edge.End) && wc.Line.End.IsAlmostEqualTo(edge.Start));
                if (matchingWall != null)
                {
                    var wallLength = matchingWall.Line.Length();
                    if (wallLength > maxLength)
                    {
                        maxLength = wallLength;
                        longestWall = matchingWall;
                    }
                }
                else
                {
                    //There is open corridor - no default door required.
                    return null;
                }
            }
            return longestWall;
        }

        private static Door? CreateDoor(WallCandidate? wall,
                                        Vector3 position,
                                        double width,
                                        DoorPositionsOverride? doorOverride = null)
        {
            if (wall == null || !Door.CanFit(wall.Line, width))
            {
                return null;
            }

            var door = new Door(wall.Line, position, width);

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

        public static List<Line> FindAllEdgesAdjacentToSegments(IEnumerable<Line> edgesToClassify, IEnumerable<Line> comparisonSegments, out List<Line> otherSegments)
        {
            otherSegments = new List<Line>();
            var adjacentSegments = new List<Line>();

            foreach (var edge in edgesToClassify)
            {
                var midPt = edge.PointAt(0.5);
                midPt.Z = 0;
                var adjacentToAny = false;
                foreach (var comparisonSegment in comparisonSegments)
                {
                    var start = comparisonSegment.Start;
                    var end = comparisonSegment.End;
                    start.Z = 0;
                    end.Z = 0;
                    var comparisonSegmentProjected = new Line(start, end);
                    var dist = midPt.DistanceTo(comparisonSegmentProjected);
                    if (dist < 0.35)
                    {
                        adjacentToAny = true;
                        adjacentSegments.Add(edge);
                        break;
                    }
                }
                if (!adjacentToAny)
                {
                    otherSegments.Add(edge);
                }
            }
            return adjacentSegments;
        }
    }
}