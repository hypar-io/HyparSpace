using Elements;
using System.Collections.Generic;

namespace SpacePlanningZonesFromProgramRequirements
{
	/// <summary>
	/// Override metadata for ArrangeSpacesOverride
	/// </summary>
	public partial class ArrangeSpacesOverride : IOverride
	{
        public static string Name = "Arrange Spaces";
        public static string Dependency = null;
        public static string Context = "[*discriminator=Elements.SpaceBoundary&DefaultType!=true]";
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