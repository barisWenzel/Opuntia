using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Geometry;

namespace Nephila
{
    public class ForceDensitySolver : GH_Component
    {
        public ForceDensitySolver()
            : base(
                "Force Density Solver", "FDSolver",
                "Solves structures using the Force Density Method (FDM).",
                "Nephila", "Solver")
        {
        }

        // ─────────────────────────────────────────────
        //  Inputs
        // ─────────────────────────────────────────────
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddIntegerParameter(
                "AnchorIndices", "AI",
                "Indices of fixed (anchor) nodes.",
                GH_ParamAccess.list);

            pManager.AddPointParameter(
                "Vertices", "V",
                "Node positions.",
                GH_ParamAccess.list);

            pManager.AddIntegerParameter(
                "PP", "PP",
                "Point-to-Point adjacency. Path = vertex index, values = neighbour indices.",
                GH_ParamAccess.tree);

            pManager.AddIntegerParameter(
                "PL", "PL",
                "Point-to-Line adjacency. Path = vertex index, values = edge indices.",
                GH_ParamAccess.tree);

            pManager.AddIntegerParameter(
                "LP", "LP",
                "Line-to-Point adjacency. Path = edge index, values = [start, end] vertex indices.",
                GH_ParamAccess.tree);

            pManager.AddVectorParameter(
                "Loads", "P",
                "External load vectors per node.",
                GH_ParamAccess.list);

            pManager.AddNumberParameter(
                "ForceDensity", "q",
                "Force density per edge (q>0 = tension, q<0 = compression).",
                GH_ParamAccess.list);

            pManager.AddIntegerParameter(
                "FixedEdges", "FixedE",
                "Edge indices subject to target-length constraint.",
                GH_ParamAccess.list);

            pManager.AddNumberParameter(
                "TargetLengths", "TargetL",
                "Target lengths for fixed edges.",
                GH_ParamAccess.list);

            pManager.AddIntegerParameter(
                "MaxIterations", "MaxIt",
                "Maximum number of iterations.",
                GH_ParamAccess.item, 1000);

            pManager.AddNumberParameter(
                "Tolerance", "Tol",
                "Convergence tolerance.",
                GH_ParamAccess.item, Rhino.RhinoDoc.ActiveDoc.ModelAbsoluteTolerance);

            pManager.AddIntegerParameter(
                "CorrectionPasses", "CorPasses",
                "Number of length-correction passes per iteration (for fixed edges).",
                GH_ParamAccess.item, 10);

            // Optional
            pManager[2].Optional = true;
            pManager[3].Optional = true;
            pManager[7].Optional = true;
            pManager[8].Optional = true;
        }

        // ─────────────────────────────────────────────
        //  Outputs
        // ─────────────────────────────────────────────
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddPointParameter(
                "Vertices", "V",
                "Optimised node positions.",
                GH_ParamAccess.list);

            pManager.AddNumberParameter(
                "Forces", "N",
                "Axial forces N = q * L per member. q > 0 → Tension, q < 0 → Compression.",
                GH_ParamAccess.list);

            pManager.AddNumberParameter(
                "Lengths", "L",
                "Computed member lengths.",
                GH_ParamAccess.list);

            pManager.AddLineParameter(
                "Lines", "E",
                "Result edges as lines.",
                GH_ParamAccess.list);
        }

        // ─────────────────────────────────────────────
        //  Solve
        // ─────────────────────────────────────────────
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // ── Inputs ────────────────────────────────
            var anchorIndices = new List<int>();
            var points = new List<Point3d>();
            var PP = new GH_Structure<GH_Integer>();
            var PL = new GH_Structure<GH_Integer>();
            var LP = new GH_Structure<GH_Integer>();
            var loads = new List<Vector3d>();
            var q = new List<double>();
            var fixedEdges = new List<int>();
            var targetLengths = new List<double>();
            int maxIterations = 1000;
            double tol = 0;
            int correctionPasses = 10;

            if (!DA.GetDataList(0, anchorIndices)) return;
            if (!DA.GetDataList(1, points)) return;
            DA.GetDataTree(2, out PP);
            DA.GetDataTree(3, out PL);
            if (!DA.GetDataTree(4, out LP)) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "LP tree required."); return; }
            if (!DA.GetDataList(5, loads)) loads = new List<Vector3d>();
            if (!DA.GetDataList(6, q)) return;
            DA.GetDataList(7, fixedEdges);
            DA.GetDataList(8, targetLengths);
            DA.GetData(9, ref maxIterations);
            DA.GetData(10, ref tol);
            DA.GetData(11, ref correctionPasses);

            // ── Setup ─────────────────────────────────
            int vCount = points.Count;
            int edgeCount = LP.Branches.Count;

            if (maxIterations > 10000)
            {
                maxIterations = 10000;
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Iterations capped at 10000.");
            }

            if (correctionPasses < 1)
            {
                correctionPasses = 1;
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "CorrectionPasses set to minimum of 1.");
            }

            // ── Anchors ───────────────────────────────
            bool[] naked = new bool[vCount];
            foreach (int idx in anchorIndices)
                if (idx >= 0 && idx < vCount) naked[idx] = true;

            // ── Fill load vector ──────────────────────
            while (loads.Count < vCount)
                loads.Add(loads.Count > 0 ? loads[0] : Vector3d.Zero);

            // ── Edge endpoints from LP ─────────────────
            int[] edgeA = new int[edgeCount];
            int[] edgeB = new int[edgeCount];

            for (int e = 0; e < edgeCount; e++)
            {
                var branch = LP.Branches[e];
                edgeA[e] = branch.Count > 0 ? branch[0].Value : -1;
                edgeB[e] = branch.Count > 1 ? branch[1].Value : -1;
            }

            // ── Neighbour / q cache ───────────────────
            int[][] neighborCache = new int[vCount][];
            double[][] qCache = new double[vCount][];

            for (int v = 0; v < vCount; v++)
            {
                var nb = PP.Branches[v];
                var li = PL.Branches[v];
                neighborCache[v] = nb.Select(x => x.Value).ToArray();
                qCache[v] = new double[nb.Count];

                for (int i = 0; i < nb.Count; i++)
                {
                    int ei = li[i].Value;
                    qCache[v][i] = (ei >= 0 && ei < q.Count) ? q[ei] : 1.0;
                }
            }

            // ── Fixed-length constraint flag ──────────
            bool hasFixed = fixedEdges.Count > 0 &&
                            targetLengths.Count == fixedEdges.Count;

            // ── Iteration loop ────────────────────────
            Point3d[] positions = points.ToArray();
            Point3d[] prevPositions = (Point3d[])positions.Clone();
            int lastIt = maxIterations - 1;
            var sw = Stopwatch.StartNew();

            for (int it = 0; it < maxIterations; it++)
            {
                // Force Density step
                Point3d[] result = (Point3d[])positions.Clone();
                for (int v = 0; v < vCount; v++)
                {
                    if (naked[v]) continue;

                    int[] nb = neighborCache[v];
                    Vector3d sum = Vector3d.Zero;
                    double sumQ = 0.0;

                    for (int i = 0; i < nb.Length; i++)
                    {
                        var p = positions[nb[i]];
                        sum.X += p.X * qCache[v][i];
                        sum.Y += p.Y * qCache[v][i];
                        sum.Z += p.Z * qCache[v][i];
                        sumQ += qCache[v][i];
                    }

                    sum += loads[v];

                    if (Math.Abs(sumQ) > 1e-10)
                        result[v] = new Point3d(sum / sumQ);
                }
                positions = result;

                // Target-length correction – MEHRFACH PASSAGEN
                if (hasFixed)
                {
                    for (int pass = 0; pass < correctionPasses; pass++)
                    {
                        for (int k = 0; k < fixedEdges.Count; k++)
                        {
                            int e = fixedEdges[k];
                            if (e < 0 || e >= edgeCount) continue;

                            int a = edgeA[e], b = edgeB[e];
                            if (a < 0 || b < 0) continue;

                            double target = targetLengths[k];
                            Vector3d dir = positions[b] - positions[a];
                            double len = dir.Length;
                            if (len < 1e-10) continue;

                            dir /= len;
                            double diff = len - target;

                            if (!naked[a] && !naked[b])
                            {
                                positions[a] += dir * (diff * 0.5);
                                positions[b] -= dir * (diff * 0.5);
                            }
                            else if (!naked[a]) { positions[a] += dir * diff; }
                            else if (!naked[b]) { positions[b] -= dir * diff; }
                        }
                    }
                }

                // Convergence check
                double maxDelta = 0.0;
                for (int v = 0; v < vCount; v++)
                {
                    double d = positions[v].DistanceTo(prevPositions[v]);
                    if (d > maxDelta) maxDelta = d;
                }

                if (maxDelta < tol) { lastIt = it; break; }

                prevPositions = (Point3d[])positions.Clone();
            }

            sw.Stop();

            // ── Component message ─────────────────────
            Message = $"it:{lastIt + 1}  t:{sw.ElapsedMilliseconds}ms  cor:{correctionPasses}";

            // ── Forces & lengths ──────────────────────
            var forces = new List<double>();
            var lengths = new List<double>();
            var resultLines = new List<Line>();
            for (int e = 0; e < edgeCount; e++)
            {
                int a = edgeA[e], b = edgeB[e];
                if (a < 0 || b < 0) continue;
                Line line = new Line(positions[a], positions[b]);
                resultLines.Add(line);

                double qi = (e < q.Count) ? q[e] : 1.0;
                double length = line.Length;
                lengths.Add(length);
                forces.Add(qi * length);
            }

            // ── Outputs ───────────────────────────────
            DA.SetDataList(0, positions.ToList());
            DA.SetDataList(1, forces);
            DA.SetDataList(2, lengths);
            DA.SetDataList(3, resultLines);
        }

        // ─────────────────────────────────────────────
        //  Icon & Guid
        // ─────────────────────────────────────────────
        protected override System.Drawing.Bitmap Icon =>
            Nephila.Properties.Resources.NephilaIcon_Spinne;

        public override Guid ComponentGuid =>
            new Guid("87654321-4321-8765-4321-876543218765");
    }
}
