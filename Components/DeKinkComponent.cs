using System;
using System.Collections.Generic;
using Grasshopper.Kernel;

namespace Opuntia
{
    /// <summary>
    /// Erzeugt ein EntknickOptions-Objekt (Methode 3, Randentknickung)
    /// zum Anhaengen an den Force Density Solver.
    /// </summary>
    public class DeKinkComponent : GH_Component
    {
        public DeKinkComponent()
            : base(
                 "De-Kink", "DeKink",
                "Smooths kinks along boundary vertices after convergence (Method 3). " +
                "Connect to the solver's O input.",
                "Opuntia", "Options")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddIntegerParameter(
                "BoundaryIndices", "BI",
                "Indices of boundary nodes to unknot.",
                GH_ParamAccess.list);

            pManager.AddNumberParameter(
                "AngleTolerance", "Tol",
                "Minimum collinearity angle (degrees) for extrapolation.",
                GH_ParamAccess.item, 30.0);

            pManager.AddIntegerParameter(
                "SmoothPasses", "It",
                "Number of unknot + re-relaxation passes after convergence.",
                GH_ParamAccess.item, 3);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter(
                "Options", "O",
                "EntknickOptions object for the solver.",
                GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var boundary = new List<int>();
            double angleTol = 30.0;
            int smoothPasses = 3;

            if (!DA.GetDataList(0, boundary)) return;
            DA.GetData(1, ref angleTol);
            DA.GetData(2, ref smoothPasses);

            var opt = new DeKinkOptions(boundary, angleTol, smoothPasses);
            DA.SetData(0, new GH_SolverOption(opt));
        }

        protected override System.Drawing.Bitmap Icon
             => Opuntia.Properties.Resources.Opuntia_Icon_EntKnick;

        public override Guid ComponentGuid =>
            new Guid("A1B2C3D4-0002-4002-9002-0123456789AB");
    }
}