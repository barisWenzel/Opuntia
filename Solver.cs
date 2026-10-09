using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Geometry;

namespace Opuntia
{
    public partial class ForceDensitySolver : GH_Component
    {
        public ForceDensitySolver()
            : base(
                "Force Density Solver", "FDSolver",
                "Solves structures using the Force Density Method (FDM). "
              + "Optional Constraint and Entknick via Options input.Version =" + Ver,
                "Opuntia", "Solver")
        {
        }

        // ── Solver-Zustand (fuer Methoden sichtbar) ──
        private Point3d[] positions;
        private Point3d[] startPositions;  // Referenz fuer gesperrte Achsen
        private bool[][] fixAxis;          // [v][0..2] = x/y/z gesperrt
        private bool[] isAnchor;           // true = alle drei Achsen gesperrt
        private Vector3d[] loadVec;
        private int[][] neighborCache;     // Nachbarknoten je Knoten (PP)
        private int[][] edgeCacheN;        // Kantenindex je Nachbar (PL)
        private double[][] qCache;         // q je Nachbar (synchron mit qEdge)
        private double[] qEdge;            // q je Kante (massgeblich)
        private int[] edgeA;
        private int[] edgeB;
        private HashSet<int> hashBoundary;
        private int vCount;
        private int edgeCount;
        private int maxIterations;
        private double tol;
        private double[] nodeDelta;
        private HashSet<int> comprEdges;
        private static readonly string Ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();

        // ─────────────────────────────────────────────
        //  Inputs
        // ─────────────────────────────────────────────
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Fix", "Fix",
                "Supports. Plain index = fixed in x, y and z (e.g. \"3\").\n" +
                "Index + axes = only these axes fixed (e.g. \"3xy\", \"7z\").",
                GH_ParamAccess.list);

            pManager.AddGenericParameter("Graph", "G",
                "Graph from Line Graph.", GH_ParamAccess.item);

            pManager.AddVectorParameter("Loads", "P",
                "External load vectors per node.", GH_ParamAccess.list);

            pManager.AddNumberParameter("ForceDensity", "q",
                "Force density per edge (q>0 = tension, q<0 = compression).",
                GH_ParamAccess.list);

            pManager.AddGenericParameter("Options", "O",
                "Optional solver methods (ConstraintOptions / EntknickOptions).",
                GH_ParamAccess.list);

            pManager.AddIntegerParameter("MaxIterations", "MaxIt",
                "Maximum number of iterations.", GH_ParamAccess.item, 1000);

            pManager.AddNumberParameter("Tolerance", "Tol",
                "Convergence tolerance.", GH_ParamAccess.item,
                Rhino.RhinoDoc.ActiveDoc.ModelAbsoluteTolerance);

            // Optional
            pManager[0].Optional = true;  // Fix
            pManager[2].Optional = true;  // Loads
            pManager[4].Optional = true;  // Options
        }

        // ─────────────────────────────────────────────
        //  Outputs
        // ─────────────────────────────────────────────
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddPointParameter("Vertices", "V", "Optimised node positions.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Forces", "N", "Axial forces N = q * L per member.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Lengths", "L", "Computed member lengths.", GH_ParamAccess.list);
            pManager.AddLineParameter("Lines", "E", "Result edges as lines.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Residuals", "Res", "Force-equilibrium residual per node (free axes only).", GH_ParamAccess.list);
            pManager.AddNumberParameter("NodeDelta", "dV", "Displacement of each node in the final iteration.", GH_ParamAccess.list);
        }

        // ─────────────────────────────────────────────
        //  Solve
        // ─────────────────────────────────────────────
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // ── Inputs ────────────────────────────────
            var fixInput = new List<string>();
            IGH_Goo graphGoo = null;
            var loads = new List<Vector3d>();
            var q = new List<double>();
            var options = new List<IGH_Goo>();
            maxIterations = 1000;
            tol = 0;

            if (!DA.GetDataList(0, fixInput))
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "No supports provided – system may be unstable unless self-stressed (tensegrity).");
            if (!DA.GetData(1, ref graphGoo)) return;
            if (!(graphGoo is GH_OpuntiaGraph gg) || !gg.IsValid)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Input G is not a valid Opuntia graph.");
                return;
            }
            OpuntiaGraph graph = gg.Value;
            if (!DA.GetDataList(2, loads)) loads = new List<Vector3d>();
            if (!DA.GetDataList(3, q)) return;
            DA.GetDataList(4, options);
            DA.GetData(5, ref maxIterations);
            DA.GetData(6, ref tol);

            // ── Options entpacken ─────────────────────
            ConstraintOptions constraint = null;
            EntknickOptions entknick = null;
            foreach (var goo in options)
            {
                if (goo is GH_SolverOption so)
                {
                    if (so.Value is ConstraintOptions c) constraint = c;
                    else if (so.Value is EntknickOptions e) entknick = e;
                }
            }

            // ── Setup ─────────────────────────────────
            vCount = graph.VertexCount;
            edgeCount = graph.EdgeCount;

            if (maxIterations > 10000)
            {
                maxIterations = 10000;
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Iterations capped at 10000.");
            }

            // ── Lager je Achse ────────────────────────
            fixAxis = new bool[vCount][];
            for (int v = 0; v < vCount; v++) fixAxis[v] = new bool[3];
            ParseFix(fixInput);

            isAnchor = new bool[vCount];
            for (int v = 0; v < vCount; v++)
                isAnchor[v] = fixAxis[v][0] && fixAxis[v][1] && fixAxis[v][2];

            // ── Loads ─────────────────────────────────
            while (loads.Count < vCount)
                loads.Add(loads.Count > 0 ? loads[0] : Vector3d.Zero);
            loadVec = loads.ToArray();

            // ── Edge endpoints from LP ────────────────
            edgeA = new int[edgeCount];
            edgeB = new int[edgeCount];
            for (int e = 0; e < edgeCount; e++)
            {
                edgeA[e] = graph.LP[e][0];
                edgeB[e] = graph.LP[e][1];
            }

            // ── q je Kante ────────────────────────────
            qEdge = new double[edgeCount];
            for (int e = 0; e < edgeCount; e++)
                qEdge[e] = (e < q.Count) ? q[e] : 1.0;

            // Druckstaebe einmal aus Start-q festhalten
            comprEdges = new HashSet<int>();
            for (int e = 0; e < edgeCount; e++)
                if (qEdge[e] < 0) comprEdges.Add(e);

            // ── Neighbour / q / edge cache ────────────
            neighborCache = new int[vCount][];
            qCache = new double[vCount][];
            edgeCacheN = new int[vCount][];

            for (int v = 0; v < vCount; v++)
            {
                neighborCache[v] = (int[])graph.PP[v].Clone();
                edgeCacheN[v] = (int[])graph.PL[v].Clone();
                qCache[v] = new double[neighborCache[v].Length];
                for (int i = 0; i < edgeCacheN[v].Length; i++)
                {
                    int ei = edgeCacheN[v][i];
                    qCache[v][i] = (ei >= 0 && ei < qEdge.Length) ? qEdge[ei] : 1.0;
                }
            }

            bool useConstraint = constraint != null && constraint.IsValid;
            bool useEntknick = entknick != null && entknick.IsValid;
            hashBoundary = useEntknick
                ? new HashSet<int>(entknick.BoundaryIndices)
                : new HashSet<int>();

            // ── Solve ─────────────────────────────────
            var sw = Stopwatch.StartNew();
            positions = (Point3d[])graph.Vertices.Clone();
            startPositions = (Point3d[])graph.Vertices.Clone();

            // 1) Relaxation bis Konvergenz (mit optionalem Constraint)
            int initIt = RelaxToEquilibrium(useConstraint ? constraint : null);

            // 2) Entknickung nach Konvergenz, smoothPasses-mal
            if (useEntknick)
            {
                double dotThreshold = Math.Cos(entknick.AngleTolerance * Math.PI / 180.0);
                for (int pass = 0; pass < entknick.SmoothPasses; pass++)
                {
                    DeknickRand(entknick.BoundaryIndices, dotThreshold);
                    EnforceFixed();
                    RelaxToEquilibrium(useConstraint ? constraint : null);
                }
            }
            sw.Stop();

            // ── Residuals (nur freie Achsen) ──────────
            var residuals = new List<double>();
            for (int v = 0; v < vCount; v++)
            {
                if (isAnchor[v]) { residuals.Add(0.0); continue; }
                int[] nb = neighborCache[v];
                Vector3d residual = Vector3d.Zero;
                for (int i = 0; i < nb.Length; i++)
                    residual += qCache[v][i] * (positions[nb[i]] - positions[v]);
                residual += loadVec[v];
                if (fixAxis[v][0]) residual.X = 0;   // Auflagerreaktion
                if (fixAxis[v][1]) residual.Y = 0;
                if (fixAxis[v][2]) residual.Z = 0;
                residuals.Add(residual.Length);
            }

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
                double length = line.Length;
                lengths.Add(length);
                forces.Add(qEdge[e] * length);
            }

            Message = $"it:{initIt} \n t:{sw.ElapsedMilliseconds}ms";

            // ── Outputs ───────────────────────────────
            DA.SetDataList(0, positions.ToList());
            DA.SetDataList(1, forces);
            DA.SetDataList(2, lengths);
            DA.SetDataList(3, resultLines);
            DA.SetDataList(4, residuals);
            DA.SetDataList(5, nodeDelta.ToList());
        }

        // ─────────────────────────────────────────────
        //  Fix-Eingabe lesen: "3" = xyz, "3xy" = nur x und y
        // ─────────────────────────────────────────────
        private void ParseFix(List<string> fixInput)
        {
            foreach (string raw in fixInput)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                string s = raw.Trim().ToLowerInvariant();

                int k = 0;
                while (k < s.Length && char.IsDigit(s[k])) k++;
                if (k == 0 || !int.TryParse(s.Substring(0, k), out int idx) || idx < 0 || idx >= vCount)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Invalid Fix entry ignored: \"{raw}\"");
                    continue;
                }

                string axes = s.Substring(k).Trim();
                if (axes.Length == 0)
                {
                    fixAxis[idx][0] = fixAxis[idx][1] = fixAxis[idx][2] = true;
                    continue;
                }

                foreach (char c in axes)
                {
                    if (c == 'x') fixAxis[idx][0] = true;
                    else if (c == 'y') fixAxis[idx][1] = true;
                    else if (c == 'z') fixAxis[idx][2] = true;
                    else AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Unknown axis '{c}' in Fix entry \"{raw}\"");
                }
            }
        }

        // ─────────────────────────────────────────────
        //  Gesperrte Achsen auf Startkoordinate zuruecksetzen
        // ─────────────────────────────────────────────
        private void EnforceFixed()
        {
            for (int v = 0; v < vCount; v++)
            {
                bool[] f = fixAxis[v];
                if (!f[0] && !f[1] && !f[2]) continue;
                Point3d p = positions[v];
                Point3d s = startPositions[v];
                positions[v] = new Point3d(
                    f[0] ? s.X : p.X,
                    f[1] ? s.Y : p.Y,
                    f[2] ? s.Z : p.Z);
            }
        }

        // ─────────────────────────────────────────────
        //  Methode: Jacobi-Relaxation bis Konvergenz
        //  (mit optionaler Target-Length-Korrektur, Methode 1)
        //  Rueckgabe: Anzahl benoetigter Iterationen
        // ─────────────────────────────────────────────
        private int RelaxToEquilibrium(ConstraintOptions constraint)
        {
            Point3d[] prevPositions = (Point3d[])positions.Clone();
            int lastIt = maxIterations;

            for (int it = 0; it < maxIterations; it++)
            {
                // Jacobi-Schritt
                Point3d[] result = (Point3d[])positions.Clone();
                for (int v = 0; v < vCount; v++)
                {
                    if (isAnchor[v]) continue;

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
                    sum += loadVec[v];

                    if (Math.Abs(sumQ) > 1e-10)
                    {
                        Point3d cur = positions[v];
                        bool[] f = fixAxis[v];
                        result[v] = new Point3d(
                            f[0] ? cur.X : sum.X / sumQ,
                            f[1] ? cur.Y : sum.Y / sumQ,
                            f[2] ? cur.Z : sum.Z / sumQ);
                    }
                }
                positions = result;

                // Optional: Target-Length-Korrektur (Methode 1)
                if (constraint != null)
                {
                    ApplyConstraint(constraint);
                    EnforceFixed();
                }

                // Konvergenz
                if (nodeDelta == null || nodeDelta.Length != vCount)
                    nodeDelta = new double[vCount];
                double maxDelta = 0.0;
                for (int v = 0; v < vCount; v++)
                {
                    double d = positions[v].DistanceTo(prevPositions[v]);
                    nodeDelta[v] = d;
                    if (d > maxDelta) maxDelta = d;
                }
                if (maxDelta < tol) { lastIt = it + 1; break; }

                prevPositions = (Point3d[])positions.Clone();
            }
            return lastIt;
        }

        // ─────────────────────────────────────────────
        //  Icon & Guid
        // ─────────────────────────────────────────────
        protected override System.Drawing.Bitmap Icon =>
            Opuntia.Properties.Resources.Opuntia_Icon_Spinne;

        public override Guid ComponentGuid =>
            new Guid("87654321-4321-8765-4321-876543218765");
    }
}
