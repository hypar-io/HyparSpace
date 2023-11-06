using Elements;
using System.Collections.Generic;
using System;
using System.Linq;

namespace Flooring
{
	/// <summary>
	/// Override metadata for FloorTypesOverrideRemoval
	/// </summary>
	public partial class FloorTypesOverrideRemoval : IOverride
	{
        public static string Name = "Floor Types Removal";
        public static string Dependency = null;
        public static string Context = "[*discriminator=Elements.FlooringType]";
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