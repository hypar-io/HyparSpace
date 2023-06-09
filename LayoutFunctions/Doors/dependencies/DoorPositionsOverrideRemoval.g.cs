using Elements;
using System.Collections.Generic;
using System;
using System.Linq;

namespace Doors
{
	/// <summary>
	/// Override metadata for DoorPositionsOverrideRemoval
	/// </summary>
	public partial class DoorPositionsOverrideRemoval : IOverride
	{
        public static string Name = "Door Positions Removal";
        public static string Dependency = null;
        public static string Context = "[*discriminator=Elements.Door]";
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