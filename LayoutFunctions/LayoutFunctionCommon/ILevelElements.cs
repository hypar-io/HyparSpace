using System;
using System.Collections.Generic;
using Elements;

namespace Elements
{
    public interface ILevelElements
    {
        IList<Element> Elements { get; set; }

        Guid Level { get; set; }
    }

}