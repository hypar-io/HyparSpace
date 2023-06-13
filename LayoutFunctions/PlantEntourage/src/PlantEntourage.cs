using Elements;
using Elements.Geometry;
using System.Collections.Generic;

namespace PlantEntourage
{
      public static class PlantEntourage
    {
        private static readonly double plantWidth = 1.0;
        private static readonly double plantHeight = 1.0;
        private static readonly double plantLength = 1.0;

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

            var roomType = "Meeting Room";
            var spaceBoundaries = siteModel.AllElementsOfType<SpaceBoundary>().Where(sp => roomType.Equals(sp.ProgramType));
            var plantMaterial = new Material("Plant material", Colors.Green);

            foreach (var spaceBoundary in spaceBoundaries)
            {
                var mass = CreatePlantInRoom(spaceBoundary, plantMaterial);
                output.Model.AddElement(mass);
            }

            return output;
        }

        private static Element CreatePlantInRoom(SpaceBoundary room, Material plantMaterial)
        {
            var halfRectangleSize = new Vector3(0.5 * plantWidth, 0.5 * plantLength); 
            var roomCentroid = room.Boundary.Perimeter.Centroid();
            var rectangle = Polygon.Rectangle(roomCentroid - halfRectangleSize, roomCentroid + halfRectangleSize);
            var plantMass = new Mass(rectangle, plantHeight, material: plantMaterial, name: "Plant");
            return plantMass;
        }
      }
}