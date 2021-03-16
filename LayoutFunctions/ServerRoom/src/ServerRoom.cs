using Elements;
using Elements.Geometry;
using Elements.Spatial;
using System.Collections.Generic;
using System.Linq;

namespace ServerRoom
{
    public static class ServerRoom
    {
        /// <summary>
        /// The ServerRoom function.
        /// </summary>
        /// <param name="model">The input model.</param>
        /// <param name="input">The arguments to the execution.</param>
        /// <returns>A ServerRoomOutputs instance containing computed results and the model with any new elements.</returns>
        public static ServerRoomOutputs Execute(Dictionary<string, Model> inputModels, ServerRoomInputs input)
        {
            var spacePlanningZones = inputModels["Space Planning Zones"];
            var levelsModel = inputModels["Levels"];
            var levels = spacePlanningZones.AllElementsOfType<LevelElements>();
            var levelVolumes = levelsModel.AllElementsOfType<LevelVolume>();

            var output = new ServerRoomOutputs();
            foreach (var lvl in levels)
            {
                var roomBoundaries = lvl.Elements.OfType<SpaceBoundary>().Where(z => z.Name == "Server Room");

                foreach (var room in roomBoundaries)
                {
                    var profile = room.Boundary;
                    //inset from walls
                    var inset = profile.Perimeter.Offset(-1);
                    var longestEdge = inset.SelectMany(s => s.Segments()).OrderBy(l => l.Length()).Last();
                    var grid = new Grid2d(inset, new Transform(longestEdge.Start, longestEdge.Direction(), Vector3.ZAxis));
                    grid.U.DivideByPattern(new[] { 1.0, 2.0 });
                    grid.V.DivideByFixedLength(1.0);
                    output.Model.AddElements(grid.GetCells().SelectMany(c => c.GetTrimmedCellGeometry()).Select(c => new ModelCurve(c, BuiltInMaterials.XAxis, room.Transform)));
                }
            }
            return output;
        }
    }
}