using Elements;
using Elements.Components;
using Elements.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LayoutFunctionCommon
{
    public static class FlippedConfigurations
    {
        private static Dictionary<string, ContentElement> _mirroredElements { get; set; }
        private static SpaceConfiguration _originalConfigs { get; set; }
        private static SpaceConfiguration _yFlippedConfigs { get; set; }
        private static SpaceConfiguration _xFlippedConfigs { get; set; }
        private static SpaceConfiguration _xyFlippedConfigs { get; set; }

        public static void Init(SpaceConfiguration configs)
        {
            _mirroredElements = ContentCatalogRetrieval.GetCatalog().Content
                .Where(c => c.Name.Contains("Mirrored"))
                .ToDictionary(me => me.Name.Replace(" Mirrored", ""), me => me);

            _originalConfigs = configs;
            _yFlippedConfigs = GetFlippedConfigs(configs);
            _xFlippedConfigs = GetRotated180Configs(_yFlippedConfigs);
            _xyFlippedConfigs = GetRotated180Configs(configs);
        }

        public static SpaceConfiguration GetConfigs(bool primaryFlip, bool secondaryFlip)
        {
            return
                primaryFlip && !secondaryFlip ? _yFlippedConfigs :
                !primaryFlip && secondaryFlip ? _xFlippedConfigs :
                primaryFlip && secondaryFlip ? _xyFlippedConfigs :
                _originalConfigs;
        }

        private static SpaceConfiguration GetFlippedConfigs(SpaceConfiguration configs)
        {
            return GetNewConfigs(configs, Flip);
        }

        private static SpaceConfiguration GetRotated180Configs(SpaceConfiguration configs)
        {
            return GetNewConfigs(configs, Rotate);
        }

        private static void Rotate(ContentConfiguration.ContentItem item, Vector3 min, Vector3 max)
        {
            item.Transform.RotateAboutPoint((min + max) / 2, Vector3.ZAxis, 180);
            item.Anchor = new Vector3(min.X + max.X - item.Anchor.X, min.Y + max.Y - item.Anchor.Y, item.Anchor.Z);
        }

        private static void Flip(ContentConfiguration.ContentItem item, Vector3 min, Vector3 max)
        {
            // Mirror origin
            var newOrigin = item.Transform.Origin;
            newOrigin.Y = min.Y + max.Y - item.Transform.Origin.Y;

            // Mirror anchor
            item.Anchor = new Vector3(item.Anchor.X, min.Y + max.Y - item.Anchor.Y, item.Anchor.Z);

            var flippedYByY = item.Transform.YAxis;
            flippedYByY.Y *= -1;

            // Move origin relative to the width of the object
            if (_mirroredElements.TryGetValue(item.ContentElement.Name, out var mirroredElement) && mirroredElement != null)
            {
                item.Name = mirroredElement.Name;
                item.Url = mirroredElement.GltfLocation;

                // Mirror directional by x
                var flippedYByX = item.Transform.YAxis;
                flippedYByX.X *= -1;
                item.Transform.RotateAboutPoint(item.Transform.Origin, Vector3.ZAxis, item.Transform.YAxis.PlaneAngleTo(flippedYByX));
            }
            else if (item.ContentElement.Name.Contains("Left") || item.ContentElement.Name.Contains("Right"))
            {
                var oppositeElemName = string.Join(" ", item.ContentElement.Name.Split(" ").Select(w => w == "Right" ? "Left" : (w == "Left" ? "Right" : w)));
                var oppositeElem = ContentCatalogRetrieval.GetCatalog().Content.FirstOrDefault(e => e.Name == oppositeElemName);
                if (oppositeElem != null)
                {
                    item.Name = oppositeElem.Name;
                    item.Url = oppositeElem.GltfLocation;
                }

                // Mirror directional by y
                item.Transform.RotateAboutPoint(item.Transform.Origin, Vector3.ZAxis, item.Transform.YAxis.PlaneAngleTo(flippedYByY));
            }
            else
            {
                // Mirror directional by y
                item.Transform.RotateAboutPoint(item.Transform.Origin, Vector3.ZAxis, item.Transform.YAxis.PlaneAngleTo(flippedYByY));

                // When moving a symmetrical object, there is a need to shift its location by the value of its width
                newOrigin += item.Transform.XAxis.Negate() * (item.ContentElement.BoundingBox.Max.X - Math.Abs(item.ContentElement.BoundingBox.Min.X));
            }

            item.Transform.Matrix.SetTranslation(newOrigin);
        }

        private static SpaceConfiguration GetNewConfigs(SpaceConfiguration configs, Action<ContentConfiguration.ContentItem, Vector3, Vector3> change)
        {
            var newConfigs = new SpaceConfiguration();
            foreach (var config in configs)
            {
                // Create new config
                var newConfig = new ContentConfiguration()
                {
                    CellBoundary = new ContentConfiguration.BoundaryDefinition()
                    {
                        Min = config.Value.CellBoundary.Min,
                        Max = config.Value.CellBoundary.Max,
                    },
                    ContentItems = new List<ContentConfiguration.ContentItem>()
                };

                foreach (var item in config.Value.ContentItems)
                {
                    var newItem = new ContentConfiguration.ContentItem()
                    {
                        Url = item.Url,
                        Name = item.Name,
                        Transform = new Transform(item.Transform),
                        Anchor = item.Anchor,
                    };

                    change(newItem, config.Value.CellBoundary.Min, config.Value.CellBoundary.Max);
                    newConfig.ContentItems.Add(newItem);
                }

                newConfigs.Add(config.Key, newConfig);
            }

            return newConfigs;
        }
    }
}