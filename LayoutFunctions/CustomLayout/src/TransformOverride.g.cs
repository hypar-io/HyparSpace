using Elements;
using System.Collections.Generic;

namespace CustomSpaceType
{
	/// <summary>
	/// Override metadata for TransformOverride
	/// </summary>
	public partial class TransformOverride : IOverride
	{
        public static string Name = "Transform";
        public static string Dependency = null;
        public static string Context = "[*discriminator=Elements.ElementInstance]";
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