using System;
using System.Collections.Generic;
using System.Linq;
using Elements.Geometry;

namespace Elements.Components
{
    public class PolylineBasedElementPlacementRule : PolylinePlacementRule
    {
        private Func<Polyline, IEnumerable<Element>> postProcessOperation = null;
        public PolylineBasedElementPlacementRule(Polyline polyline, IList<int> anchorIndices, IList<Vector3> anchorDisplacements, string name)
         : base(polyline, anchorIndices, anchorDisplacements, name)
        {

        }

        public PolylineBasedElementPlacementRule(PolylinePlacementRule p) : base(p.Curve, p.AnchorIndices, p.AnchorDisplacements, p.Name)
        {

        }

        public override List<Element> Instantiate(ComponentDefinition definition)
        {
            List<Element> elementsOut = new List<Element>();
            var curves = base.Instantiate(definition).OfType<ModelCurve>();
            if (postProcessOperation != null)
            {
                foreach (var crv in curves)
                {
                    var baseCurve = crv.Curve;
                    elementsOut.AddRange(postProcessOperation(baseCurve as Polyline));
                }
            }
            else
            {
                elementsOut.AddRange(curves);
            }

            return elementsOut;
        }
        public void SetPostProcessOperation(Func<Polyline, IEnumerable<Element>> postProcessOperation)
        {
            this.postProcessOperation = postProcessOperation;
        }
    }
}