using Elements.Geometry;
using Elements.Spatial;
using static Elements.Components.ContentConfiguration;

namespace Elements.Components
{
    public class SpaceConfigurationCreator
    {
        public static SpaceConfiguration CreateSpaceConfigurationFromModel(Model model)
        {
            var elementInstances = model.AllElementsOfType<ElementInstance>();
            var spaceConfiguration = new SpaceConfiguration();
            foreach (var space in model.AllElementsOfType<SpaceBoundary>())
            {
                var bbox = new BBox3(space.Boundary);
                var boundaryDefinition = new BoundaryDefinition()
                {
                    Min = new Vector3(),
                    Max = new Vector3(bbox.XSize, bbox.YSize)
                };
                var contentItems = CreateContentItems(elementInstances, space.Boundary, boundaryDefinition);
                var contentConfiguration = new ContentConfiguration
                {
                    ContentItems = contentItems,
                    CellBoundary = boundaryDefinition
                };

                spaceConfiguration.Add(space.Name, contentConfiguration);
            }

            return spaceConfiguration;
        }

        private static List<ContentItem> CreateContentItems(IEnumerable<ElementInstance> elementInstances, Profile spaceBoundary, BoundaryDefinition boundaryDefinition)
        {
            var bbox = new BBox3(spaceBoundary);
            var t = new Transform(bbox.Min).Inverted();
            var contentItems = new List<ContentItem>();
            var spaceElementInstances = elementInstances.Where(ei => spaceBoundary.Contains(ei.Transform.Origin));
            var grid = new Grid2d(spaceBoundary.Perimeter);
            var firstSplitPosition = 0.25;
            var secondSplitPosition = 0.75;

            foreach (var elementInstance in spaceElementInstances)
            {
                var uPosition = grid.U.ClosestPosition(elementInstance.Transform.Origin).MapFromDomain(grid.U.Domain);
                var vPosition = grid.V.ClosestPosition(elementInstance.Transform.Origin).MapFromDomain(grid.V.Domain);
                var anchorPoint = new Vector3();
                anchorPoint.X = uPosition switch
                {
                    var p when p < firstSplitPosition => 0,
                    var p when p >= firstSplitPosition && p <= secondSplitPosition => boundaryDefinition.Width / 2,
                    var p when p > secondSplitPosition => boundaryDefinition.Width,
                    _ => 0
                };

                anchorPoint.Y = vPosition switch
                {
                    var p when p < firstSplitPosition => 0,
                    var p when p >= firstSplitPosition && p <= secondSplitPosition => boundaryDefinition.Depth / 2,
                    var p when p > secondSplitPosition => boundaryDefinition.Depth,
                    _ => 0
                };

                var contentItem = new ContentItem()
                {
                    Anchor = anchorPoint,
                    Url = (elementInstance.BaseDefinition as ContentElement)?.GltfLocation,
                    Name = elementInstance.Name,
                    Transform = elementInstance.Transform.Concatenated(t)
                };
                contentItems.Add(contentItem);
            }

            return contentItems;
        }
    }
}