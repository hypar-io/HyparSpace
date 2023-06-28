using Elements;
using System.Collections.Generic;
using System;
using System.Linq;

namespace PlantEntourage
{
	/// <summary>
	/// Override metadata for PlantsOverrideAddition
	/// </summary>
	public partial class PlantsOverrideAddition : IOverride
	{
        public static string Name = "Plants Addition";
        public static string Dependency = null;
        public static string Context = "[*discriminator=Elements.Plant]";
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