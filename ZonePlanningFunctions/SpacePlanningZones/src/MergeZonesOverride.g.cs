using Elements;
using System.Collections.Generic;

namespace SpacePlanningZones
{
	/// <summary>
	/// Override metadata for MergeZonesOverride
	/// </summary>
	public partial class MergeZonesOverride : IOverride
	{
        public static string Name = "Merge Zones";
        public static string Dependency = null;
        public static string Context = "[*discriminator=Elements.SpaceBoundary]";
		public static string Paradigm = "Group";

        /// <summary>
        /// Get the override name for this override.
        /// </summary>
        public string GetName() {
			return Name;
		}

		public object GetIdentity() {
			return Identities;
		}

	}
}