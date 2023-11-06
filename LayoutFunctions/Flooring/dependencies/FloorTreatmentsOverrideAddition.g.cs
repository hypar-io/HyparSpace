using Elements;
using System.Collections.Generic;
using System;
using System.Linq;

namespace Flooring
{
	/// <summary>
	/// Override metadata for FloorTreatmentsOverrideAddition
	/// </summary>
	public partial class FloorTreatmentsOverrideAddition : IOverride
	{
        public static string Name = "Floor Treatments Addition";
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