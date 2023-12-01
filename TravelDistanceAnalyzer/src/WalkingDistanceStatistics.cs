using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Elements
{
    public class WalkingDistanceStatistics : Element
    {
        public WalkingDistanceStatistics(string spaceType, double closest, double longest, double average) 
        {
            ProgramType = spaceType;
            ShortestDistance = closest;
            LongestDistance = longest;
            AverageDistance = average;
        }

        public string ProgramType { get; set; }

        public double ShortestDistance { get; set; }

        public double LongestDistance { get; set; }

        public double AverageDistance { get; set; }
    }
}
