using Elements;
using System.Collections.Generic;
using System;
using System.Linq;

namespace Flooring
{
	/// <summary>
	/// Override metadata for FloorTreatmentsOverrideRemoval
	/// </summary>
	public partial class FloorTreatmentsOverrideRemoval : IOverride
	{
        public static string Name = "Floor Treatments Removal";
        public static string Dependency = null;
        public static string Context = "[*discriminator=Elements.FlooringRegion]";
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