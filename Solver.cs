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
    public class ForceDensitySolver : GH_Component
    {
        public ForceDensitySolver()
            : base(
                "Force Density Solver", "FDSolver",
                "Solves structures using the Force Density Method (FDM). "
              + "Optional Constraint and Entknick via Options input.Version ="+ Ver,
                "Opuntia", "Solver")
        {
        }

        // ── Solver-Zustand (fuer Methoden sichtbar) ──
        private Point3d[] positions;
        private bool[] isAnchor;
        private Vector3d[] loadVec;
        private int[][] neighborCache;   // Nachbarknoten je Knoten (PP)
        private int[][] edgeCacheN;      // Kantenindex je Nachbar (PL)
        private double[][] qCache;       // q je Nachbar (synchron mit qEdge)
        private double[] qEdge;          // q je Kante (maßgeblich)
        private int[] edgeA;
        private int[] edgeB;
        private HashSet<int> hashBoundary;
        private int vCount;
        private int edgeCount;
        private int maxIterations;
        private double tol;
        private double[] nodeDelta;
        private HashSet<int> comprEdges;
        private static readonly string Ver =  System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();

        // ─────────────────────────────────────────────
        //  Inputs
        // ─────────────────────────────────────────────
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddIntegerParameter("AnchorIndices", "AI",
                "Indices of fixed (anchor) nodes.", GH_ParamAccess.list);

            pManager.AddPointParameter("Vertices", "V",
                "Node positions.", GH_ParamAccess.list);

            pManager.AddIntegerParameter("PP", "PP",
                "Point-to-Point adjacency. Path = vertex index, values = neighbour indices.",
                GH_ParamAccess.tree);

            pManager.AddIntegerParameter("PL", "PL",
                "Point-to-Line adjacency. Path = vertex index, values = edge indices.",
                GH_ParamAccess.tree);

            pManager.AddIntegerParameter("LP", "LP",
                "Line-to-Point adjacency. Path = edge index, values = [start, end] vertex indices.",
                GH_ParamAccess.tree);

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
            pManager[0].Optional = true;  // Anchors
            pManager[2].Optional = true;  // PP
            pManager[3].Optional = true;  // PL
            pManager[5].Optional = true;  // Loads
            pManager[7].Optional = true;  // Options

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
            pManager.AddNumberParameter("Residuals", "Res", "Force-equilibrium residual per node.", GH_ParamAccess.list);
            pManager.AddNumberParameter("NodeDelta", "dV", "Displacement of each node in the final iteration.", GH_ParamAccess.list);
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
            var options = new List<IGH_Goo>();
            maxIterations = 1000;
            tol = 0;

            if (!DA.GetDataList(0, anchorIndices))
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "No anchors provided – system may be unstable unless self-stressed (tensegrity).");
            if (!DA.GetDataList(1, points)) return;
            DA.GetDataTree(2, out PP);
            DA.GetDataTree(3, out PL);
            if (!DA.GetDataTree(4, out LP)) return;
            if (!DA.GetDataList(5, loads)) loads = new List<Vector3d>();
            if (!DA.GetDataList(6, q)) return;
            DA.GetDataList(7, options);
            DA.GetData(8, ref maxIterations);
            DA.GetData(9, ref tol);

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
            vCount = points.Count;
            edgeCount = LP.Branches.Count;

            if (maxIterations > 10000)
            {
                maxIterations = 10000;
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Iterations capped at 10000.");
            }

            // ── Anchors ───────────────────────────────
            isAnchor = new bool[vCount];
            foreach (int idx in anchorIndices)
                if (idx >= 0 && idx < vCount) isAnchor[idx] = true;

            // ── Loads ─────────────────────────────────
            while (loads.Count < vCount)
                loads.Add(loads.Count > 0 ? loads[0] : Vector3d.Zero);
            loadVec = loads.ToArray();

            // ── Edge endpoints from LP ────────────────
            edgeA = new int[edgeCount];
            edgeB = new int[edgeCount];
            for (int e = 0; e < edgeCount; e++)
            {
                var branch = LP.Branches[e];
                edgeA[e] = branch.Count > 0 ? branch[0].Value : -1;
                edgeB[e] = branch.Count > 1 ? branch[1].Value : -1;
            }

            // ── q je Kante ────────────────────────────
            qEdge = new double[edgeCount];
            for (int e = 0; e < edgeCount; e++)
                qEdge[e] = (e < q.Count) ? q[e] : 1.0;

            // Druckstäbe einmal aus Start-q festhalten
            comprEdges = new HashSet<int>();
            for (int e = 0; e < edgeCount; e++)
                if (qEdge[e] < 0) comprEdges.Add(e);

            // ── Neighbour / q / edge cache ────────────
            neighborCache = new int[vCount][];
            qCache = new double[vCount][];
            edgeCacheN = new int[vCount][];

            for (int v = 0; v < vCount; v++)
            {
                var nb = PP.Branches[v];
                var li = PL.Branches[v];
                neighborCache[v] = nb.Select(x => x.Value).ToArray();
                qCache[v] = new double[nb.Count];
                edgeCacheN[v] = new int[nb.Count];

                for (int i = 0; i < nb.Count; i++)
                {
                    int ei = li[i].Value;
                    edgeCacheN[v][i] = ei;
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
            positions = points.ToArray();

            // 1) Relaxation bis Konvergenz (mit optionalem Constraint)
            int initIt = RelaxToEquilibrium(useConstraint ? constraint : null);

            // 2) Entknickung nach Konvergenz, smoothPasses-mal
            int smoothCount = 0;
            if (useEntknick)
            {
                double dotThreshold = Math.Cos(entknick.AngleTolerance * Math.PI / 180.0);
                for (int pass = 0; pass < entknick.SmoothPasses; pass++)
                {
                    DeknickRand(entknick.BoundaryIndices, dotThreshold);
                    smoothCount++;
                    RelaxToEquilibrium(useConstraint ? constraint : null);
                }
            }
            sw.Stop();

            // ── Residuals ─────────────────────────────
            var residuals = new List<double>();
            for (int v = 0; v < vCount; v++)
            {
                if (isAnchor[v]) { residuals.Add(0.0); continue; }
                int[] nb = neighborCache[v];
                Vector3d residual = Vector3d.Zero;
                for (int i = 0; i < nb.Length; i++)
                    residual += qCache[v][i] * (positions[nb[i]] - positions[v]);
                residual += loadVec[v];
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
                        result[v] = new Point3d(sum / sumQ);
                }
                positions = result;

                // Optional: Target-Length-Korrektur (Methode 1)
                if (constraint != null)
                    ApplyConstraint(constraint);

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
                if (maxDelta < 0.0001) { lastIt = it + 1; break; } //hier tolerance

                prevPositions = (Point3d[])positions.Clone();
            }
            return lastIt;
        }




        // ─────────────────────────────────────────────
        //  Methode: Target-Length-Korrektur (Methode 1)
        // ─────────────────────────────────────────────
        // Hinweis: seit q-Nachfuehrung konvergiert der Constraint meist in 1 Pass.
        // MaxIteration > 1 vermutlich unnoetig, fuer stark gekoppelte Netze aber sicherheitshalber belassen.


        /*    private void ApplyConstraint(ConstraintOptions c)
    {
        for (int its = 0; its < c.MaxIteration; its++)
        {
            for (int k = 0; k < c.FixedEdgeIndices.Count; k++)
            {
                int e = c.FixedEdgeIndices[k];
                if (e < 0 || e >= edgeCount) continue;

                int a = edgeA[e], b = edgeB[e];
                if (a < 0 || b < 0) continue;

                double target = c.TargetLengths[k];
                if (target < 1e-10) continue;

                // freier Endknoten bestimmen
                int free = !isAnchor[b] ? b : (!isAnchor[a] ? a : -1);
                int other = (free == b) ? a : b;

                // ── 1) q aus Gleichgewicht VOR der Positionskorrektur ──
                //    (Zustand direkt nach dem Jacobi-Schritt = echte Netzlast)
                if (free >= 0)
                {
                    Vector3d axis = positions[other] - positions[free];
                    double L = axis.Length;
                    if (L > 1e-10)
                    {
                        axis /= L;

                        // Restkraft am freien Knoten OHNE diesen Stab
                        Vector3d rest = Vector3d.Zero;
                        int[] nbf = neighborCache[free];
                        for (int i = 0; i < nbf.Length; i++)
                        {
                            if (edgeCacheN[free][i] == e) continue;
                            rest += qCache[free][i] * (positions[nbf[i]] - positions[free]);
                        }
                        rest += loadVec[free];

                        // Stab traegt nur axial: q so, dass axiale Restkraft getragen wird
                        // Kraftvektor des Stabs auf 'free' = q * (positions[other]-positions[free]) = q * L * axis
                        // Gleichgewicht axial: q*L + (rest·axis) = 0
                        double restAxial = rest * axis;
                        double qEq = -restAxial / L;

                        double relax = 0.5;
                        double qNew = qEdge[e] + relax * (qEq - qEdge[e]);
                        qEdge[e] = qNew;
                        SyncQ(a, e, qNew);
                        SyncQ(b, e, qNew);
                    }
                }

                // ── 2) Laenge auf Ziel schieben (nach der q-Messung) ──
                Vector3d dir = positions[b] - positions[a];
                double len = dir.Length;
                if (len < 1e-10) continue;
                dir /= len;
                double diff = len - target;

                if (!isAnchor[a] && !isAnchor[b])
                {
                    positions[a] += dir * (diff * 0.5);
                    positions[b] -= dir * (diff * 0.5);
                }
                else if (!isAnchor[a]) { positions[a] += dir * diff; }
                else if (!isAnchor[b]) { positions[b] -= dir * diff; }
            }
        }
            }
    ////////unten die einfache, bei der druckstäbe nciht gingnge
        private void ApplyConstraint(ConstraintOptions c)
        {
            for (int its = 0; its < c.MaxIteration; its++)
            {
                for (int k = 0; k < c.FixedEdgeIndices.Count; k++)
                {
                    int e = c.FixedEdgeIndices[k];
                    if (e < 0 || e >= edgeCount) continue;

                    int a = edgeA[e], b = edgeB[e];
                    if (a < 0 || b < 0) continue;

                    double target = c.TargetLengths[k];
                    Vector3d dir = positions[b] - positions[a];
                    double len = dir.Length;
                    if (len < 1e-10) continue;

                    dir /= len;
                    double diff = len - target;

                    // Knoten auf Ziellänge schieben
                    if (!isAnchor[a] && !isAnchor[b])
                    {
                        positions[a] += dir * (diff * 0.5);
                        positions[b] -= dir * (diff * 0.5);
                    }
                    else if (!isAnchor[a]) { positions[a] += dir * diff; }
                    else if (!isAnchor[b]) { positions[b] -= dir * diff; }

                    // q nachfuehren: N = q * L konstant halten
                    // neue Laenge ist target (bzw. nahe target), alte war len
                    if (target > 1e-10)
                    {
                        double qNew = qEdge[e] * len / target;   // N_alt = qEdge*len bleibt erhalten
                        qEdge[e] = qNew;

                        // qCache beider Endknoten synchronisieren
                        SyncQ(a, e, qNew);
                        SyncQ(b, e, qNew);
                    }
                }
            }
        }
       
*/

private void ApplyConstraint(ConstraintOptions c)
        {
            for (int its = 0; its < c.MaxIteration; its++)
            {
                for (int k = 0; k < c.FixedEdgeIndices.Count; k++)
                {
                    int e = c.FixedEdgeIndices[k];
                    if (e < 0 || e >= edgeCount) continue;

                    int a = edgeA[e], b = edgeB[e];
                    if (a < 0 || b < 0) continue;

                    double target = c.TargetLengths[k];
                    if (target < 1e-10) continue;

                    // ── 1) Länge auf Ziel schieben ──
                    Vector3d dir = positions[b] - positions[a];
                    double len = dir.Length;
                    if (len < 1e-10) continue;
                    dir /= len;
                    double diff = len - target;

                    if (!isAnchor[a] && !isAnchor[b])
                    {
                        positions[a] += dir * (diff * 0.5);
                        positions[b] -= dir * (diff * 0.5);
                    }
                    else if (!isAnchor[a]) { positions[a] += dir * diff; }
                    else if (!isAnchor[b]) { positions[b] -= dir * diff; }

                    // ── 2) q nachführen ──
                    if (!comprEdges.Contains(e))
                    {
                        // Zugkante: Krafterhalt N = q*L (bewährt, konvergiert)
                        double qNew = qEdge[e] * len / target;
                        qEdge[e] = qNew;
                        SyncQ(a, e, qNew);
                        SyncQ(b, e, qNew);
                    }
                    else
                    {
                        // Druckstab: q aus axialem Gleichgewicht am freien Knoten
                        int free = !isAnchor[b] ? b : (!isAnchor[a] ? a : -1);
                        int other = (free == b) ? a : b;
                        if (free < 0) continue;

                        Vector3d axis = positions[other] - positions[free];
                        double L = axis.Length;                 // ~ target nach dem Shift
                        if (L < 1e-10) continue;
                        axis /= L;

                        Vector3d rest = Vector3d.Zero;
                        int[] nbf = neighborCache[free];
                        for (int i = 0; i < nbf.Length; i++)
                        {
                            if (edgeCacheN[free][i] == e) continue;
                            rest += qCache[free][i] * (positions[nbf[i]] - positions[free]);
                        }
                        rest += loadVec[free];

                        double qEq = -(rest * axis) / L;
                        double relax = 0.5;
                        double qNew = qEdge[e] + relax * (qEq - qEdge[e]);
                        qEdge[e] = qNew;
                        SyncQ(a, e, qNew);
                        SyncQ(b, e, qNew);
                    }
                }
            }
        }



        // Hilfsmethode: q im Nachbar-Cache eines Knotens fuer Kante e setzen
        private void SyncQ(int v, int e, double qNew)
        {
            int[] nb = neighborCache[v];
            for (int i = 0; i < nb.Length; i++)
            {
                if (edgeCacheN[v][i] == e) { qCache[v][i] = qNew; return; }
            }
        }

        // ─────────────────────────────────────────────
        //  Methode: Randentknickung (Methode 3)
        //  Randknoten kollinear zum inneren Anschlussseil,
        //  Rand-q per Krafterhalt (F = q_alt * L_alt) nachfuehren.
        // ─────────────────────────────────────────────
        private void DeknickRand(List<int> boundaryIndices, double dotThreshold)
        {
            var moved = new Dictionary<int, Point3d>();

            foreach (int bi in boundaryIndices)
            {
                if (bi < 0 || bi >= vCount) continue;
                if (isAnchor[bi]) continue; // Festpunkte nie verschieben

                int[] nb = neighborCache[bi];
                var innerNeighbors = nb.Where(n => !hashBoundary.Contains(n)).ToList();
                if (innerNeighbors.Count == 0) continue;

                int inner = innerNeighbors.OrderBy(n => positions[n].DistanceTo(positions[bi])).First();

                Vector3d dirBI = positions[inner] - positions[bi];
                double lenBI = dirBI.Length;
                if (lenBI < 1e-10) continue;
                dirBI.Unitize();

                int bestII = -1;
                double bestDot = -999;
                foreach (int candidate in neighborCache[inner].Where(n => n != bi))
                {
                    Vector3d dirInner = positions[candidate] - positions[inner];
                    double len = dirInner.Length;
                    if (len < 1e-10) continue;
                    dirInner.Unitize();
                    double dot = dirBI * dirInner;
                    if (dot > bestDot) { bestDot = dot; bestII = candidate; }
                }
                if (bestII < 0 || bestDot < dotThreshold) continue;

                Vector3d ex = positions[inner] - positions[bestII];
                if (ex.Length < 1e-10) continue;
                ex.Unitize();
                moved[bi] = positions[inner] + ex * lenBI;
            }

            // Positionen setzen und q der Randkanten per Krafterhalt nachfuehren
            foreach (var kv in moved)
            {
                int bi = kv.Key;
                Point3d before = positions[bi];
                Point3d after = kv.Value;

                int[] nb = neighborCache[bi];
                for (int i = 0; i < nb.Length; i++)
                {
                    int n = nb[i];
                    int ei = edgeCacheN[bi][i];
                    if (ei < 0 || ei >= qEdge.Length) continue;

                    double lenBefore = before.DistanceTo(positions[n]);
                    double lenAfter = after.DistanceTo(positions[n]);
                    if (lenAfter > 1e-10)
                    {
                        double qNew = qEdge[ei] * lenBefore / lenAfter; // F = q*L bleibt
                        qEdge[ei] = qNew;
                        qCache[bi][i] = qNew;

                        int[] nnb = neighborCache[n];
                        for (int j = 0; j < nnb.Length; j++)
                            if (nnb[j] == bi) { qCache[n][j] = qNew; break; }
                    }
                }
                positions[bi] = after;
            }
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
