using System;
using System.Collections.Generic;
using Grasshopper.Kernel;

namespace Opuntia
{
    /// <summary>
    /// Erzeugt ein ConstraintOptions-Objekt (Methode 1, laengenbasiert)
    /// zum Anhaengen an den Force Density Solver.
    /// </summary>
    public class ConstraintOptionsComponent : GH_Component
    {
        public ConstraintOptionsComponent()
            : base(
                "Constraint Options", "Constraint",
                "Target-length constraint (Method 1). Attach to the solver's Options input.",
                "Opuntia", "Options")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddIntegerParameter(
                "FixedEdgeIndices", "FixedEI",
                "Edge indices subject to target-length constraint.",
                GH_ParamAccess.list);

            pManager.AddNumberParameter(
                "TargetLengths", "TargetL",
                "Target lengths for fixed edges (paired with FixedEdgeIndices).",
                GH_ParamAccess.list);

            pManager.AddIntegerParameter(
                "Passes", "Passes",
                "Number of Passes.",
                 GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter(
                "Options", "O",
                "ConstraintOptions object for the solver.",
                GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var fixedEdges = new List<int>();
            var targetLengths = new List<double>();
            int maxIterations = 10;

            if (!DA.GetDataList(0, fixedEdges)) return;
            if (!DA.GetDataList(1, targetLengths)) return;
            DA.GetData(2, ref maxIterations);

            if (fixedEdges.Count != targetLengths.Count)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "FixedEdgeIndices and TargetLengths must have equal length.");
                return;
            }

            var opt = new ConstraintOptions(fixedEdges, targetLengths, maxIterations);
            DA.SetData(0, new GH_SolverOption(opt));
        }

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid =>
            new Guid("A1B2C3D4-0001-4001-9001-0123456789AB");
    }
}
