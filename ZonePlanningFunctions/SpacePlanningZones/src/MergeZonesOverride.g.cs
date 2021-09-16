using Elements;
using System.Collections.Generic;

namespace SpacePlanningZones
{
	/// <summary>
	/// Override metadata for MergeZonesOverride
	/// </summary>
	public partial class MergeZonesOverride
	{
        public static string Name = "Merge Zones";
        public static string Dependency = null;
        public static string Context = "[*discriminator=Elements.SpaceBoundary]";

	}
}