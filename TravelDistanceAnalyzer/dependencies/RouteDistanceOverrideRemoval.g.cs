using Elements;
using System.Collections.Generic;
using System;
using System.Linq;

namespace TravelDistanceAnalyzer
{
	/// <summary>
	/// Override metadata for RouteDistanceOverrideRemoval
	/// </summary>
	public partial class RouteDistanceOverrideRemoval : IOverride
	{
        public static string Name = "Route Distance Removal";
        public static string Dependency = null;
        public static string Context = "[*discriminator=Elements.RouteDistanceConfiguration]";
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