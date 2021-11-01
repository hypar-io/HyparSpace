using Elements;
using System.Collections.Generic;

namespace MeetingRoomLayout
{
	/// <summary>
	/// Override metadata for FurnitureLocationsOverride
	/// </summary>
	public partial class FurnitureLocationsOverride : IOverride
	{
        public static string Name = "Furniture Locations";
        public static string Dependency = null;
        public static string Context = "[*discriminator=Elements.ElementInstance]";
		public static string Paradigm = "Edit";

        /// <summary>
        /// Get the override name for this override.
        /// </summary>
        public string GetName() {
			return Name;
		}

		public object GetIdentity() {

			return Identity;
		}

	}
}