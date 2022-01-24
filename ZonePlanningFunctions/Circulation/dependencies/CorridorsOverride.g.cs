using Elements;
using System.Collections.Generic;

namespace Circulation
{
	/// <summary>
	/// Override metadata for CorridorsOverride
	/// </summary>
	public partial class CorridorsOverride : IOverride
	{
        public static string Name = "Corridors";
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