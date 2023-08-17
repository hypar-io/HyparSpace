using Elements;
using Elements.Components;
using Elements.Geometry;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace LayoutFunctionCommon
{
    public static class Configurations
    {
        private static Dictionary<string, ContentElement> _mirroredElements { get; set; }
        private static Dictionary<string, ContentElement> _leftElements { get; set; }
        private static Dictionary<string, ContentElement> _rightElements { get; set; }
        private static SpaceConfiguration _originalConfigs { get; set; }
        private static SpaceConfiguration _yFlippedConfigs { get; set; }

        public static SpaceConfiguration OriginalConfigs { get { return _originalConfigs; } }
        public static SpaceConfiguration YFlippedConfigs { get { return _yFlippedConfigs; } }

        public static void Init(SpaceConfiguration configs)
        {
            _mirroredElements = ContentCatalogRetrieval.GetCatalog().Content
                .Where(c => c.Name.Contains("Mirrored"))
                .ToDictionary(me => me.Name.Replace(" Mirrored", ""), me => me);

            _originalConfigs = configs;
            _yFlippedConfigs = GetFlippedConfigs(configs);
        }

        public static (SpaceConfiguration Configs, Transform Transform) GetConfigs(Vector3 centroid, bool hFlip, bool vFlip)
        {
            var newTransform = new Transform();
            newTransform.RotateAboutPoint(centroid, Vector3.ZAxis, vFlip ? 180 : 0);

            return ((hFlip ^ vFlip ? _yFlippedConfigs : _originalConfigs), newTransform);
        }

        public static SpaceConfiguration GetFlippedConfigs(SpaceConfiguration configs)
        {
            Action<ContentConfiguration.ContentItem, Vector3, Vector3> flip = (item, min, max) =>
            {
                if (item.ContentElement.Name.Contains("Plinth Base - Pedestal - Mobile - Wood Top"))
                {

                }

                // Mirror origin
                var newOrigin = item.Transform.Origin;
                newOrigin.Y = max.Y - (item.Transform.Origin.Y - min.Y);

                // Mirror directional
                var flippedY = item.Transform.YAxis;
                flippedY.Y = -flippedY.Y;
                item.Transform.RotateAboutPoint(item.Transform.Origin, Vector3.ZAxis, item.Transform.YAxis.PlaneAngleTo(flippedY));

                // Move origin relative to the width of the object
                var t = _mirroredElements;
                if (_mirroredElements.TryGetValue(item.ContentElement.Name, out var mirroredElement) && mirroredElement != null)
                {
                    item.Name = mirroredElement.Name;
                    item.Url = mirroredElement.GltfLocation;
                    newOrigin += item.Transform.XAxis.Negate() * (item.ContentElement.BoundingBox.Max.X - Math.Abs(item.ContentElement.BoundingBox.Min.X));
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
                }
                else
                {
                    newOrigin += item.Transform.XAxis.Negate() * (item.ContentElement.BoundingBox.Max.X - Math.Abs(item.ContentElement.BoundingBox.Min.X));
                }

                item.Transform.Matrix.SetTranslation(newOrigin);

                item.Anchor = new Vector3(item.Anchor.X, -item.Anchor.Y, item.Anchor.Z);
            };

            return GetNewConfigs(configs, flip);
        }

        // public static SpaceConfiguration GetRotated180Configs(SpaceConfiguration configs)
        // {
        //     Action<ContentConfiguration.ContentItem, Vector3, Vector3> rotate = (item, min, max) =>
        //     {
        //         item.Transform.RotateAboutPoint((min + max) / 2, Vector3.ZAxis, 180);
        //         item.Anchor = item.Anchor.Negate();
        //     };

        //     return GetNewConfigs(configs, rotate);
        // }

        public static SpaceConfiguration GetNewConfigs(SpaceConfiguration configs, Action<ContentConfiguration.ContentItem, Vector3, Vector3> change)
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