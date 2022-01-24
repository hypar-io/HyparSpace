using Elements;
using System.Collections.Generic;

namespace Circulation
{
	/// <summary>
	/// Override metadata for CorridorsOverrideAddition
	/// </summary>
	public partial class CorridorsOverrideAddition : IOverride
	{
        public static string Name = "Corridors Addition";
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