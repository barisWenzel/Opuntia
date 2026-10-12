using System;
using System.Collections.Generic;
using Grasshopper.Kernel;

namespace Opuntia
{
    /// <summary>
    /// Erzeugt ein ConstraintOptions-Objekt (Methode 1, laengenbasiert)
    /// zum Anhaengen an den Force Density Solver.
    /// </summary>
    public class TargetLengthComponent : GH_Component
    {
        public TargetLengthComponent()
            : base(
                "Target Length", "TargetL",
                "Target-length constraint for selected edges. Connect to the solver's O input.",
                 "Opuntia", "Options")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddIntegerParameter(
                "EdgeIndices", "EI",
                "Edge indices subject to the target-length constraint.",
                 GH_ParamAccess.list);

            pManager.AddNumberParameter(
                "TargetLengths", "Le",
                "Target lengths, paired with EdgeIndices.",
                GH_ParamAccess.list);

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
         

            if (!DA.GetDataList(0, fixedEdges)) return;
            if (!DA.GetDataList(1, targetLengths)) return;
            

            if (fixedEdges.Count != targetLengths.Count)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "FixedEdgeIndices and TargetLengths must have equal length.");
                return;
            }

            var opt = new TargetLengthOptions(fixedEdges, targetLengths);
            DA.SetData(0, new GH_SolverOption(opt));
        }

        protected override System.Drawing.Bitmap Icon
            => Opuntia.Properties.Resources.Opuntia_Icon_Zwang;
            

        public override Guid ComponentGuid =>
            new Guid("A1B2C3D4-0001-4001-9001-0123456789AB");
    }
}
