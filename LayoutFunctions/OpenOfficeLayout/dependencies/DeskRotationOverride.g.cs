using Elements;
using System.Collections.Generic;

namespace OpenOfficeLayout
{
	/// <summary>
	/// Override metadata for DeskRotationOverride
	/// </summary>
	public partial class DeskRotationOverride : IOverride
	{
        public static string Name = "Desk Rotation";
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