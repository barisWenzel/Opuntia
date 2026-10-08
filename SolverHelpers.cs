using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;

namespace Opuntia
{
    public partial class ForceDensitySolver
    {
        // ─────────────────────────────────────────────
        //  Methode: Target-Length-Korrektur (Methode 1)
        // ─────────────────────────────────────────────
        private void ApplyConstraint(ConstraintOptions c)
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
                    // Zugkante: Krafterhalt N = q*L
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
                    double L = axis.Length;
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

        // q im Nachbar-Cache eines Knotens für Kante e setzen
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
        //  Rand-q per Krafterhalt (F = q_alt * L_alt) nachführen.
        // ─────────────────────────────────────────────
        private void DeknickRand(List<int> boundaryIndices, double dotThreshold)
        {
            var moved = new Dictionary<int, Point3d>();

            foreach (int bi in boundaryIndices)
            {
                if (bi < 0 || bi >= vCount) continue;
                if (isAnchor[bi]) continue;

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
                    if (dirInner.Length < 1e-10) continue;
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
                        double qNew = qEdge[ei] * lenBefore / lenAfter;
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
    }
}
