using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Opuntia
{
    public class LineGraphComponent : GH_Component
    {
        public LineGraphComponent()
            : base(
                "Line Graph",
                "LGraph",
                "Builds a graph topology from lines:\n" +
                "deduplicated vertices, edges, adjacency trees.\n" +
                "Points within tolerance are merged to their cluster centroid.\n" +
                "Duplicate and degenerate edges are removed.",
                "Opunita",
                "Graph")
        { }

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
            pManager.AddPointParameter(          // 0
                "Vertices", "V",
                "Deduplicated, merged vertex positions.",
                GH_ParamAccess.list);

            pManager.AddLineParameter(           // 1
                "Edges", "E",
                "Deduplicated edges.",
                GH_ParamAccess.list);

            pManager.AddIntegerParameter(        // 2
                "PP", "PP",
                "Point-to-Point adjacency.\n" +
                "Path = vertex index, values = neighbour vertex indices.",
                GH_ParamAccess.tree);

            pManager.AddIntegerParameter(        // 3
                "PL", "PL",
                "Point-to-Line adjacency.\n" +
                "Path = vertex index, values = incident edge indices.",
                GH_ParamAccess.tree);

            pManager.AddIntegerParameter(        // 4
                "LP", "LP",
                "Line-to-Point adjacency.\n" +
                "Path = edge index, values = [start, end] vertex indices.",
                GH_ParamAccess.tree);

            pManager.AddIntegerParameter(        // 5
                "Map", "Map",
                "Input-line to edge index map.\n" +
                "Null = degenerate/removed line.",
                GH_ParamAccess.list);

        }
        

        // ─────────────────────────────────────────────────────────────────────
        //  SolveInstance
        // ─────────────────────────────────────────────────────────────────────
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // ── Inputs ────────────────────────────────────────────────────────
            var ghLines = new List<GH_Line>();
            if (!DA.GetDataList(0, ghLines)) return;
            // HINWEIS: Wir lesen GH_Line (nicht Line direkt), um den Null-Bug zu
            // vermeiden. Point3d ist ein Struct – ein null würde stillschweigend
            // zu (0,0,0) konvertiert.
            // Quelle: https://discourse.mcneel.com/t/avoiding-null-point-conversion-


            double tolerance = tolerance = Rhino.RhinoDoc.ActiveDoc.ModelAbsoluteTolerance;
            DA.GetData(1, ref tolerance);
            

            // ── Build vertex list ─────────────────────────────────────────────
            var vertices = new List<Point3d>();

            int GetOrAdd(Point3d pt)
            {
                for (int i = 0; i < vertices.Count; i++)
                    if (vertices[i].DistanceTo(pt) <= tolerance) return i;
                vertices.Add(pt);
                return vertices.Count - 1;
            }

            // ── Build edges ───────────────────────────────────────────────────
            var edges = new List<Line>();
            var edgeSet = new HashSet<(int, int)>();   // dedup
            var map = new List<int?>();            // input index → edge index / null

            // adjacency
            var pp = new DataTree<int>();
            var pl = new DataTree<int>();
            var lp = new DataTree<int>();

            var edgeDict = new Dictionary<(int, int), int>();

            foreach (var ghLine in ghLines)
            {
                if (ghLine == null) { map.Add(null); continue; }

                Line line = ghLine.Value;

                int a = GetOrAdd(line.From);
                int b = GetOrAdd(line.To);

                // degenerate
                if (a == b) { map.Add(null); continue; }

                // normalise order for dedup
                var key = a < b ? (a, b) : (b, a);

                 if (edgeDict.TryGetValue(key, out int existingIdx))
                  {
                      map.Add(null);  // Duplikat → null
                     continue;
                 }

                 //Falls die duplikate in der map bleiben sollen
                //if (edgeDict.TryGetValue(key, out int existingIdx))
                //{
                //    map.Add(existingIdx);
               //     continue;
               // }

                int edgeIdx = edges.Count;
                edgeDict[key] = edgeIdx;
                edges.Add(new Line(vertices[a], vertices[b]));
                map.Add(edgeIdx);

                // LP
                var lpPath = new GH_Path(edgeIdx);
                lp.Add(a, lpPath);
                lp.Add(b, lpPath);

                // PP
                pp.Add(b, new GH_Path(a));
                pp.Add(a, new GH_Path(b));

                // PL
                pl.Add(edgeIdx, new GH_Path(a));
                pl.Add(edgeIdx, new GH_Path(b));
            }

            // ── Outputs ───────────────────────────────────────────────────────
            DA.SetDataList(0, vertices);
            DA.SetDataList(1, edges);
            DA.SetDataTree(2, pp);
            DA.SetDataTree(3, pl);
            DA.SetDataTree(4, lp);

            // Map: null → GH_Integer with no value (empty param slot)
            var mapOut = new List<GH_Integer>();
            foreach (var m in map)
                mapOut.Add(m.HasValue ? new GH_Integer(m.Value) : null);
            DA.SetDataList(5, mapOut);


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
