using Elements;
using System.Collections.Generic;

namespace OpenOfficeLayout
{
	/// <summary>
	/// Override metadata for SpaceSettingsOverride
	/// </summary>
	public partial class SpaceSettingsOverride : IOverride
	{
        public static string Name = "Space Settings";
        public static string Dependency = null;
        public static string Context = "[*discriminator=Elements.SpaceBoundary&Name=DeskArea]";
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