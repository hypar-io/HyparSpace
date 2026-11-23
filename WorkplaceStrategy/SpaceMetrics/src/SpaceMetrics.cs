using Elements;
using Elements.Geometry;
using System.Collections.Generic;

namespace SpaceMetrics
{
    public static class SpaceMetrics
    {
        private static readonly List<ElementProxy<SpaceBoundary>> proxies = new List<ElementProxy<SpaceBoundary>>();
        private static readonly string SpaceMetricDependencyName = SpaceMetricsOverride.Dependency;

        private const string _openOffice = "Open Office";
        private const string _openCollab = "Open Collaboration";

        /// <summary>
        /// 
        /// </summary>
        /// <param name="model">The input model.</param>
        /// <param name="input">The arguments to the execution.</param>
        /// <returns>A SpaceMetricsOutputs instance containing computed results and the model with any new elements.</returns>
        public static SpaceMetricsOutputs Execute(Dictionary<string, Model> inputModels, SpaceMetricsInputs input)
        {
            proxies.Clear();
            var output = new SpaceMetricsOutputs();

            var spaceMetrics = new List<SpaceMetric>();
            if (inputModels.TryGetValue("Space Planning Zones", out var zonesModel))
            {
                inputModels.TryGetValue(_openOffice + " Layout", out var openOfficeModel);
                inputModels.TryGetValue(_openCollab + " Layout", out var openCollabModel);

                var allSpaceBoundaries = zonesModel?.AllElementsAssignableFromType<SpaceBoundary>().ToList();
                var openOfficeBoundaries = openOfficeModel?.AllElementsAssignableFromType<SpaceBoundary>().ToList();
                var openCollabSpaceMetrics = openCollabModel?.AllElementsAssignableFromType<SpaceMetric>().ToList();

                var layoutNames = new string[] { _openOffice, _openCollab, "Meeting Room", "Classroom", "Phone Booth", "Private Office", "Lounge", "Reception", "Pantry" };
                foreach (var layoutName in layoutNames)
                {
                    spaceMetrics.AddRange(UpdateSpaceMetricsByLayoutType(inputModels, input.Overrides.SpaceMetrics.ToList(), layoutName, allSpaceBoundaries, openOfficeBoundaries, openCollabSpaceMetrics));
                }
            }

            output.Model.AddElements(proxies);
            output.Model.AddElements(spaceMetrics);
            return output;
        }

        private static List<SpaceMetric> UpdateSpaceMetricsByLayoutType(
            Dictionary<string, Model> inputModels,
            List<SpaceMetricsOverride> overrides,
            string layoutName,
            List<SpaceBoundary> boundaries,
            List<SpaceBoundary> openOfficeBoundaries,
            List<SpaceMetric> openCollabSpaceMetrics)
        {
            var spaceMetrics = new List<SpaceMetric>();
            if (!inputModels.TryGetValue(layoutName + " Layout", out var layoutModel))
            {
                return spaceMetrics;
            }

            foreach (var sm in layoutModel.AllElementsOfType<SpaceMetric>())
            {
                var room = boundaries.FirstOrDefault(b => b.Id == sm.Space);
                if (room == null)
                {
                    continue;
                }

                if (layoutName == _openOffice && openOfficeBoundaries != null && openCollabSpaceMetrics != null)
                {
                    var openCollabBoundaries = openOfficeBoundaries.Where(b => room.Boundary.Perimeter.Contains(b.Boundary.Perimeter.Centroid()));
                    foreach (var openCollabBoundary in openCollabBoundaries)
                    {
                        var openCollabSM = openCollabSpaceMetrics.FirstOrDefault(osm => osm.Space == openCollabBoundary.Id);
                        sm.Seats += openCollabSM.Seats;
                        sm.Headcount += openCollabSM.Headcount;
                        sm.Desks += openCollabSM.Desks;
                        sm.CollaborationSeats += openCollabSM.CollaborationSeats;
                    }
                }

                var proxy = GetElementProxy(room, boundaries.Proxies(SpaceMetricDependencyName));
                var config = MatchApplicableOverride(overrides, proxy, sm);
                spaceMetrics.Add(new SpaceMetric(room.Id, config.Value.Seats, config.Value.Headcount, config.Value.Desks, config.Value.CollaborationSeats));
            }

            return spaceMetrics;
        }

        private static SpaceMetricsOverride MatchApplicableOverride(
            List<SpaceMetricsOverride> overridesById,
            ElementProxy<SpaceBoundary> boundaryProxy,
            SpaceMetric defaultMetric = null)
        {
            var overrideName = SpaceMetricsOverride.Name;
            SpaceMetricsOverride config = null;

            // See if we already have matching override attached
            var existingOverrideId = boundaryProxy.OverrideIds<SpaceMetricsOverride>(overrideName).FirstOrDefault();
            if (existingOverrideId != null)
            {
                config = overridesById.Find(o => o.Id == existingOverrideId);
                if (config != null)
                {
                    return config;
                }
            }

            // Try to match from identity in configs
            config ??= overridesById.Find(o => o.Identity.ParentCentroid.IsAlmostEqualTo(boundaryProxy.Element.ParentCentroid.Value));

            // Use a default in case none found
            if (config == null)
            {
                config = new SpaceMetricsOverride(
                    Guid.NewGuid().ToString(),
                    new SpaceMetricsIdentity(boundaryProxy.Element.ParentCentroid.Value),
                    new SpaceMetricsValue(
                        defaultMetric?.Seats ?? 0,
                        defaultMetric?.Headcount ?? 0,
                        defaultMetric?.Desks ?? 0,
                        defaultMetric?.CollaborationSeats ?? 0)
                );
                overridesById.Add(config);
            }

            // Attach the identity and values data to the proxy
            boundaryProxy.AddOverrideIdentity(overrideName, config.Id, config.Identity);
            boundaryProxy.AddOverrideValue(overrideName, config.Value);

            // Make sure proxies list has the proxy so that it will serialize in the model.
            if (!proxies.Contains(boundaryProxy))
            {
                proxies.Add(boundaryProxy);
            }

            return config;
        }

        private static ElementProxy<SpaceBoundary> GetElementProxy(SpaceBoundary spaceBoundary, IEnumerable<ElementProxy<SpaceBoundary>> allSpaceBoundaries)
        {
            return allSpaceBoundaries.Proxy(spaceBoundary) ?? spaceBoundary.Proxy(SpaceMetricDependencyName);
        }
    }
}