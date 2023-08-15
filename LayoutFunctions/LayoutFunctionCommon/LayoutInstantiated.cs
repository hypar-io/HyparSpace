using Elements;
using Elements.Components;
using Elements.Geometry;
using Elements.Spatial;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace LayoutFunctionCommon
{
    public class LayoutInstantiated
    {
        public ComponentInstance Instance { get; set; }
        public ContentConfiguration Config { get; set; }
        public string ConfigName { get; internal set; }
    }
}