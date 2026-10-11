using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Opuntia
{
    public class LineGraphComponent : GH_Component, IGH_VariableParameterComponent
    {
        public LineGraphComponent()
            : base(
                "Line Graph",
                "LGraph",
                "Builds a graph topology from lines.\n" +
                "Points within tolerance are merged to the first point found.\n" +
                "Duplicate and degenerate edges are removed.\n" +
                "Zoom in to add outputs (V, E, PP, PL, LP).",
                "Opuntia",
                "Graph")
        { }

        // ─────────────────────────────────────────────────────────────────────
        //  Optionale Ausgaenge (feste Reihenfolge, per ZUI zuschaltbar)
        // ─────────────────────────────────────────────────────────────────────
        private static readonly (string Name, string Nick, string Desc)[] Extra =
        {
            ("Vertices", "V",  "Deduplicated, merged vertex positions."),
            ("Edges",    "E",  "Deduplicated edges."),
            ("PP",       "PP", "Point-to-Point adjacency.\nPath = vertex index, values = neighbour vertex indices."),
            ("PL",       "PL", "Point-to-Line adjacency.\nPath = vertex index, values = incident edge indices."),
            ("LP",       "LP", "Line-to-Point adjacency.\nPath = edge index, values = [start, end] vertex indices."),
        };

        // ─────────────────────────────────────────────────────────────────────
        //  Parameters
        // ─────────────────────────────────────────────────────────────────────
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddLineParameter(           // 0
                "Lines", "L",
                "Input lines.",
                GH_ParamAccess.list);

            pManager.AddNumberParameter(         // 1
                "Tolerance", "T",
                "Point merge tolerance.",
                GH_ParamAccess.item, 0.001);

            pManager[1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter(        // 0
                "Graph", "G",
                "Graph topology for the solver.",
                GH_ParamAccess.item);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  ZUI
        // ─────────────────────────────────────────────────────────────────────
        public bool CanInsertParameter(GH_ParameterSide side, int index) =>
            side == GH_ParameterSide.Output &&
            index == Params.Output.Count &&
            index <= Extra.Length;

        public bool CanRemoveParameter(GH_ParameterSide side, int index) =>
            side == GH_ParameterSide.Output &&
            index == Params.Output.Count - 1 &&
            index > 0;

        public IGH_Param CreateParameter(GH_ParameterSide side, int index)
        {
            IGH_Param p;
            switch (index)
            {
                case 1: p = new Param_Point(); break;
                case 2: p = new Param_Line(); break;
                default: p = new Param_Integer(); break;
            }
            ApplyDefinition(p, index);
            return p;
        }

        public bool DestroyParameter(GH_ParameterSide side, int index) => true;

        public void VariableParameterMaintenance()
        {
            for (int i = 1; i < Params.Output.Count; i++)
                ApplyDefinition(Params.Output[i], i);
        }

        private static void ApplyDefinition(IGH_Param p, int index)
        {
            var d = Extra[index - 1];
            p.Name = d.Name;
            p.NickName = d.Nick;
            p.Description = d.Desc;
            p.Access = index <= 2 ? GH_ParamAccess.list : GH_ParamAccess.tree;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  SolveInstance
        // ─────────────────────────────────────────────────────────────────────
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // ── Inputs ────────────────────────────────────────────────────────
            var ghLines = new List<GH_Line>();
            if (!DA.GetDataList(0, ghLines)) return;
            // HINWEIS: GH_Line statt Line lesen, sonst wird null still zu (0,0,0).

            double tolerance = Rhino.RhinoDoc.ActiveDoc.ModelAbsoluteTolerance;
            DA.GetData(1, ref tolerance);

            // ── Vertices ──────────────────────────────────────────────────────
            var vertices = new List<Point3d>();
            var pp = new List<List<int>>();
            var pl = new List<List<int>>();

            int GetOrAdd(Point3d pt)
            {
                for (int i = 0; i < vertices.Count; i++)
                    if (vertices[i].DistanceTo(pt) <= tolerance) return i;
                vertices.Add(pt);
                pp.Add(new List<int>());
                pl.Add(new List<int>());
                return vertices.Count - 1;
            }

            // ── Edges ─────────────────────────────────────────────────────────
            var edges = new List<Line>();
            var lp = new List<int[]>();
            var edgeSet = new HashSet<(int, int)>();

            foreach (var ghLine in ghLines)
            {
                if (ghLine == null) continue;
                Line line = ghLine.Value;

                int a = GetOrAdd(line.From);
                int b = GetOrAdd(line.To);
                if (a == b) continue;                          // degeneriert

                var key = a < b ? (a, b) : (b, a);
                if (!edgeSet.Add(key)) continue;               // doppelt

                int edgeIdx = edges.Count;
                edges.Add(new Line(vertices[a], vertices[b]));
                lp.Add(new[] { a, b });

                pp[a].Add(b); pl[a].Add(edgeIdx);
                pp[b].Add(a); pl[b].Add(edgeIdx);
            }

            var graph = new OpuntiaGraph(
                vertices.ToArray(),
                edges.ToArray(),
                pp.Select(x => x.ToArray()).ToArray(),
                pl.Select(x => x.ToArray()).ToArray(),
                lp.ToArray());

            // ── Outputs ───────────────────────────────────────────────────────
            int n = Params.Output.Count;
            DA.SetData(0, new GH_OpuntiaGraph(graph));
            if (n > 1) DA.SetDataList(1, graph.Vertices);
            if (n > 2) DA.SetDataList(2, graph.Edges);
            if (n > 3) DA.SetDataTree(3, OpuntiaGraph.ToTree(graph.PP));
            if (n > 4) DA.SetDataTree(4, OpuntiaGraph.ToTree(graph.PL));
            if (n > 5) DA.SetDataTree(5, OpuntiaGraph.ToTree(graph.LP));

            Message = $"{vertices.Count} vertices\n{edges.Count} edges";
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Icon & Guid
        // ─────────────────────────────────────────────────────────────────────
        protected override System.Drawing.Bitmap Icon
            => Opuntia.Properties.Resources.Opuntia_Icon_Topo;

        public override Guid ComponentGuid
            => new Guid("A1B2C3D4-E5F6-7890-ABCD-EF1234567890");
    }
}
