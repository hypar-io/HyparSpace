using Elements;
using System.Collections.Generic;
using System;
using System.Linq;

namespace TravelDistanceAnalyzer
{
	/// <summary>
	/// Override metadata for WalkingDistanceOverrideRemoval
	/// </summary>
	public partial class WalkingDistanceOverrideRemoval : IOverride
	{
        public static string Name = "Walking Distance Removal";
        public static string Dependency = null;
        public static string Context = "[*discriminator=Elements.WalkingDistanceConfiguration]";
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