using Elements;
using System.Collections.Generic;

namespace SpacePlanningZones
{
	/// <summary>
	/// Override metadata for ProgramAssignmentsOverride
	/// </summary>
	public partial class ProgramAssignmentsOverride : IOverride
	{
        public static string Name = "Program Assignments";
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