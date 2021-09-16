using Elements;
using System.Collections.Generic;

namespace SpacePlanningZones
{
	/// <summary>
	/// Override metadata for ProgramAssignmentsOverride
	/// </summary>
	public partial class ProgramAssignmentsOverride
	{
        public static string Name = "Program Assignments";
        public static string Dependency = null;
        public static string Context = "[*discriminator=Elements.SpaceBoundary]";

	}
}