using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Opuntia
{
    /*
    internal class RegionUnion
    {


        private void RunScript(
        List<Curve> Curves,
        Plane Plane,
        bool Combine,
        double Tolerance,
        ref object A)
        {
            if (Curves == null || Curves.Count == 0) return;

            var regs = Rhino.Geometry.Curve.CreateBooleanRegions(
                Curves, Plane, Combine, Tolerance);

            if (regs == null)
            {
                Print("Fehlgeschlagen.");
                return;
            }

            var result = new List<Curve>();

            for (int i = 0; i < regs.RegionCount; i++)
            {
                var crvs = regs.RegionCurves(i);
                result.AddRange(crvs);
            }

            Print("Regionen: " + regs.RegionCount);
            A = result;
        }

    */

    
}
