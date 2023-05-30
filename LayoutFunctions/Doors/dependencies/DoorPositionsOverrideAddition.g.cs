using Elements;
using System.Collections.Generic;
using System;
using System.Linq;

namespace Doors
{
	/// <summary>
	/// Override metadata for DoorPositionsOverrideAddition
	/// </summary>
	public partial class DoorPositionsOverrideAddition : IOverride
	{
        public static string Name = "DoorPositions Addition";
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