using Elements;
using System.Collections.Generic;
using System;
using System.Linq;

namespace InteriorPartitions
{
	/// <summary>
	/// Override metadata for InteriorPartitionTypesOverrideRemoval
	/// </summary>
	public partial class InteriorPartitionTypesOverrideRemoval : IOverride
	{
        public static string Name = "Interior Partition Types Removal";
        public static string Dependency = null;
        public static string Context = "[*discriminator=Elements.WallCandidate]";
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