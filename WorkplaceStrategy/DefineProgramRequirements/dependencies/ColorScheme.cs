using System.Collections.Generic;
using Elements.Geometry;

namespace Elements
{
    public partial class ColorScheme
    {
        public static ColorScheme ProgramColors => new ColorScheme
        {
            PropertyName = "Program Type",
            Name = "Program Colors",
            Mapping = new Mapping {
                {"unspecified", new Color(0.8, 0.8, 0.8, 0.3)},
                {"Unassigned Space Type", new Color(0.8, 0.8, 0.8, 0.3)},
                {"unrecognized", new Color(0.8, 0.8, 0.2, 0.3)},
                {"Circulation", new Color(0.996,0.965,0.863,0.5)},
                {"Open Office", new Color(0.435,0.627,0.745,0.5)},
                {"Private Office", new Color(0.122,0.271,0.361,0.5)},
                {"Lounge", new Color(1.000,0.584,0.196,0.5)},
                {"Classroom", new Color(0.796,0.914,0.796,0.5)},
                {"Pantry", new Color(0.5,0.714,0.745,0.5)},
                {"Meeting Room", new Color(0.380,0.816,0.608,0.5)},
                {"Phone Booth", new Color(0.976,0.788,0.129,0.5)},
                {"Support", new Color(0.447,0.498,0.573,0.5)},
                {"Reception", new Color(0.576,0.463,0.753,0.5)},
                {"Open Collaboration", new Color(209.0/255, 224.0/255, 178.0/255, 0.5)},
                {"Data Hall", new Color(0.46,0.46,0.48,0.5)}
            }
        };
    }
}