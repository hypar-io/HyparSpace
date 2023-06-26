using Elements;
using Elements.GeoJSON;
using Elements.Geometry;
using Elements.Geometry.Solids;
using Elements.Spatial;
using Elements.Validators;
using Elements.Serialization.JSON;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using Line = Elements.Geometry.Line;
using Polygon = Elements.Geometry.Polygon;

namespace Elements
{
    public partial class WallCandidate
    {
        public double Height { get; set; }

        public Transform LevelTransform { get; set; }

        public string AddId { get; set; }

        public WallCandidate(Line @line, string @type, double height, Transform levelTransform, IList<SpaceBoundary> @spaceAdjacencies = null, System.Guid @id = default, string @name = null)
            : this(@line, @type, @spaceAdjacencies, id, name)
        {
            Height = height;
            LevelTransform = levelTransform;
        }
    }
}