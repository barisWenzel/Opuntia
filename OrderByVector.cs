using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Opuntia
{
    public class OrderByVectorComponent : GH_Component
    {
        public OrderByVectorComponent()
            : base(
                "Order By Vector", "OBV",
                "Filters curves by alignment with a reference vector",
                "Opunita", "Util")
        { }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("Curve", "C", "Input curve", GH_ParamAccess.item);
            pManager.AddVectorParameter("RefVector", "V", "Reference vector", GH_ParamAccess.item);
            pManager.AddNumberParameter("Tolerance", "T", "Dot product tolerance", GH_ParamAccess.item, 0.5);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("Aligned", "A", "Curves aligned with reference vector", GH_ParamAccess.item);
            pManager.AddCurveParameter("Unaligned", "U", "Curves not aligned with reference vector", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Curve crv = null;
            Vector3d refVector = Vector3d.Unset;
            double tolerance = 0.5;

            if (!DA.GetData(0, ref crv)) return;
            if (!DA.GetData(1, ref refVector)) return;
            DA.GetData(2, ref tolerance);

            refVector.Unitize();

            Vector3d dir = crv.TangentAtStart;
            dir.Unitize();

            double dot = Math.Abs(Vector3d.Multiply(dir, refVector));

            if (dot > tolerance)
                DA.SetData(0, crv);
            else
                DA.SetData(1, crv);

            Message = "Order By Vector";
        }

        public override Guid ComponentGuid => new Guid("071029E1-A8A3-4160-9B1A-4D927A5980BD");
    }
}
