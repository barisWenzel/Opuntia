using System.Collections.Generic;

namespace Opuntia
{
    /// <summary>
    /// Methode 1 (laengenbasiert): Target-Length-Korrektur.
    /// Wird waehrend der Relaxation in jeder Iteration angewandt.
    /// </summary>
    public class TargetLengthOptions : ISolverOption
    {
        public List<int> FixedEdgeIndices { get; }
        public List<double> TargetLengths { get; }
        public int MaxIteration { get; }

        public TargetLengthOptions(List<int> fixedEdgeIndices, List<double> targetLengths)
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
}
