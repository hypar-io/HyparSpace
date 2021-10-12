using Elements;
using System.Collections.Generic;

namespace SpacePlanningZones
{
	/// <summary>
	/// Override metadata for SplitZonesOverride
	/// </summary>
	public partial class SplitZonesOverride : IOverride
	{
        public static string Name = "Split Zones";
        public static string Dependency = "Levels";
        public static string Context = "[*discriminator=Elements.LevelVolume]";
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

		/// <summary>
		/// Get context elements that are applicable to this override.
		/// </summary>
		/// <param name="models">Dictionary of input models, or any other kind of dictionary of models.</param>
		/// <returns>List of context elements that match what is defined on the override.</returns>
		public static IEnumerable<ElementProxy<Elements.LevelVolume>> ContextProxies(Dictionary<string, Model> models) {
			return models.AllElementsOfType<Elements.LevelVolume>(Dependency).Proxies(Dependency);
		}
	}
}