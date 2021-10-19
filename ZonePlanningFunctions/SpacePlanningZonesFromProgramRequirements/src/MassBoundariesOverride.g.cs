using Elements;
using System.Collections.Generic;

namespace SpacePlanningZonesFromProgramRequirements
{
	/// <summary>
	/// Override metadata for MassBoundariesOverride
	/// </summary>
	public partial class MassBoundariesOverride : IOverride
	{
        public static string Name = "Mass Boundaries";
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