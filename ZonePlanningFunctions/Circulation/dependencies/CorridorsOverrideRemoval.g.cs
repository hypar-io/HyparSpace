using Elements;
using System.Collections.Generic;

namespace Circulation
{
	/// <summary>
	/// Override metadata for CorridorsOverrideRemoval
	/// </summary>
	public partial class CorridorsOverrideRemoval : IOverride
	{
        public static string Name = "Corridors Removal";
        public static string Dependency = null;
        public static string Context = "[*discriminator=Elements.CirculationSegment]";
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