using System.Collections.Generic;

namespace Opuntia
{
    /// <summary>
    /// Marker-Interface fuer optionale Solver-Zusatzverfahren.
    /// Erlaubt es, verschiedene Options-Typen ueber einen einzigen
    /// Solver-Input (List&lt;ISolverOption&gt;) anzuhaengen.
    /// </summary>
    public interface ISolverOption
    {
    }

    /// <summary>
    /// Methode 1 (laengenbasiert): Target-Length-Korrektur.
    /// Wird waehrend der Relaxation in jeder Iteration angewandt.
    /// </summary>
    public class ConstraintOptions : ISolverOption
    {
        public List<int> FixedEdgeIndices { get; }
        public List<double> TargetLengths { get; }
        public int MaxIteration { get; }

        public ConstraintOptions(List<int> fixedEdgeIndices, List<double> targetLengths)
        {
            FixedEdgeIndices = fixedEdgeIndices ?? new List<int>();
            TargetLengths = targetLengths ?? new List<double>();
        }
        

        public bool IsValid =>
            FixedEdgeIndices.Count > 0 &&
            FixedEdgeIndices.Count == TargetLengths.Count;

        public override string ToString() =>
            $"ConstraintOptions ({FixedEdgeIndices.Count} edges)";
    }

    /// <summary>
    /// Methode 3 (Randentknickung): kollineare Randkorrektur mit
    /// Kraft-erhaltender q-Nachfuehrung. Wird nach Konvergenz
    /// smoothPasses-mal angewandt, jeweils gefolgt von Nachrelaxation.
    /// </summary>
    public class EntknickOptions : ISolverOption
    {
        public List<int> BoundaryIndices { get; }
        public double AngleTolerance { get; }
        public int SmoothPasses { get; }

        public EntknickOptions(List<int> boundaryIndices, double angleTolerance, int smoothPasses)
        {
            BoundaryIndices = boundaryIndices ?? new List<int>();
            AngleTolerance = angleTolerance;
            SmoothPasses = smoothPasses < 0 ? 0 : smoothPasses;
        }

        public bool IsValid => BoundaryIndices.Count > 0 && SmoothPasses > 0;

        public override string ToString() =>
            $"EntknickOptions ({BoundaryIndices.Count} boundary, {SmoothPasses}x)";
    }
}
