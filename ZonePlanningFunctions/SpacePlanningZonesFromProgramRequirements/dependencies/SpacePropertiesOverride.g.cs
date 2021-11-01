using Elements;
using System.Collections.Generic;

namespace SpacePlanningZonesFromProgramRequirements
{
	/// <summary>
	/// Override metadata for SpacePropertiesOverride
	/// </summary>
	public partial class SpacePropertiesOverride : IOverride
	{
        public static string Name = "Space Properties";
        public static string Dependency = null;
        public static string Context = "[*discriminator=Elements.SpaceBoundary]";
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