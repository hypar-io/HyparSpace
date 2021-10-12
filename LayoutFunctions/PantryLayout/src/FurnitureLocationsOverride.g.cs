using Elements;
using System.Collections.Generic;

namespace PantryLayout
{
	/// <summary>
	/// Override metadata for FurnitureLocationsOverride
	/// </summary>
	public partial class FurnitureLocationsOverride
	{
        public static string Name = "Furniture Locations";
        public static string Dependency = null;
        public static string Context = "[*discriminator=Elements.ElementInstance]";

	}
}