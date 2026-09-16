using Grasshopper.Kernel.Types;

namespace Opuntia
{
    /// <summary>
    /// Goo-Wrapper fuer ISolverOption, damit Options-Objekte
    /// zwischen Komponenten uebergeben werden koennen.
    /// </summary>
    public class GH_SolverOption : GH_Goo<ISolverOption>
    {
        public GH_SolverOption() { }
        public GH_SolverOption(ISolverOption value) { Value = value; }
        public GH_SolverOption(GH_SolverOption other) { Value = other.Value; }

        public override bool IsValid => Value != null;

        public override string TypeName => "SolverOption";

        public override string TypeDescription => "Optionales Solver-Zusatzverfahren (Constraint oder Entknickung)";

        public override IGH_Goo Duplicate() => new GH_SolverOption(this);

        public override string ToString() =>
            Value != null ? Value.ToString() : "Null SolverOption";
    }
}
