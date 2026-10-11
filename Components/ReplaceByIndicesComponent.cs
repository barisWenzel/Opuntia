using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace Opuntia
{
    public class ReplaceByIndicesComponent : GH_Component
    {
        public ReplaceByIndicesComponent()
            : base(
                "Replace By Indices", "RBI",
                "Replaces values in a list at specified indices.\n" +
                "Branch 0 → all indices in branch → replace with values[0]\n",
                "Opuntia", "Util")
        { }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("List", "L", "Base list of values", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Indices", "I", "Tree of index groups per replacement value", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Values", "V", "Replacement value per branch.\n" +
                                                        "values[0] → replaces all indices in branch 0\n",
                                                        GH_ParamAccess.list);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("Result", "R", "List with replaced values", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var list = new List<double>();
            var indices = new GH_Structure<GH_Integer>();
            var values = new List<double>();

            if (!DA.GetDataList(0, list)) return;
            if (!DA.GetDataTree(1, out indices)) return;
            if (!DA.GetDataList(2, values)) return;

            var result = new List<double>(list);

            for (int b = 0; b < indices.Branches.Count; b++)
            {
                if (b >= values.Count) break;
                double val = values[b];

                foreach (var ghInt in indices.Branches[b])
                {
                    int idx = ghInt.Value;
                    if (idx < 0 || idx >= result.Count) continue;
                    result[idx] = val;
                }
            }

            DA.SetDataList(0, result);
            Message = "Replace By Indices";
        }

        public override Guid ComponentGuid => new Guid("DAC33FDB-7444-4ADF-8EAF-46476FF6E6C1");
    }
}
