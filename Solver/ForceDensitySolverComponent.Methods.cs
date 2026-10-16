using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;

namespace Opuntia
{
    public partial class ForceDensitySolverComponent
    {
        // ─────────────────────────────────────────────
        //  Methode: Target-Length-Korrektur 
        // ─────────────────────────────────────────────
        private void ApplyConstraint(TargetLengthOptions c)
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
                    // ── Druckstab ──
                    // Ein Stab trägt nur Axialkraft. q wird so gewählt, dass die Stabkraft
                    // q·L die axiale Komponente der übrigen Knotenkräfte (rest) ausgleicht.
                    // Der Querteil von rest bleibt stehen und wird im Positionsschritt
                    // (Gauss-Seidel) abgebaut.
                    bool freeA = !isAnchor[a];
                    bool freeB = !isAnchor[b];
                    if (!freeA && !freeB) continue;   // beide Anker: kein Gleichgewicht prüfbar

                    double qEq;
                    if (freeA && freeB)
                    {
                        // Beide Enden frei (z. B. zwischen Zugseilen):
                        // Der Stab hat nur ein q, das an beiden Enden passen muss.
                        // q_eq wird an a und b getrennt bestimmt und gemittelt,
                        // damit kein Ende bevorzugt wird.
                        double qa = AxialQEq(a, e, b);
                        double qb = AxialQEq(b, e, a);
                        if (double.IsNaN(qa) || double.IsNaN(qb)) continue;
                        qEq = 0.5 * (qa + qb);
                    }
                    else
                    {
                        // Ein Ende ist Anker: Der Anker nimmt beliebige Reaktionen auf,
                        // relevant ist nur das Gleichgewicht am freien Ende.
                        qEq = freeB ? AxialQEq(b, e, a) : AxialQEq(a, e, b);
                        if (double.IsNaN(qEq)) continue;
                    }

                    // Unterrelaxation: q nur halb auf q_eq ziehen.
                    // rest beruht auf noch nicht konvergierten q und Positionen der Nachbarn,
                    // q_eq ist daher nur eine Schätzung. Volle Übernahme würde oszillieren.
                    const double relax = 0.5;
                    double qNew = qEdge[e] + relax * (qEq - qEdge[e]);

                    // q in Kantenliste und in beide Knoten-Caches schreiben
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

        /// <summary>
        /// Kraftdichte q der Kante e, die am Knoten 'node' das axiale Gleichgewicht herstellt.
        /// Knotengleichgewicht KDM:  Σ q_i·(x_j − x_node) + P = 0
        /// Zerlegung:                rest + q_e·L·axis = 0
        /// Projektion auf Achse:     rest·axis + q_e·L = 0   →   q_e = −(rest·axis) / L
        /// q_e &lt; 0 bedeutet Druck (rest drückt in Richtung otherNode).
        /// </summary>
        private double AxialQEq(int node, int e, int otherNode)
        {
            // Einheitsvektor der Stabachse, vom betrachteten Knoten zum anderen Ende
            Vector3d axis = positions[otherNode] - positions[node];
            double L = axis.Length;
            if (L < 1e-10) return double.NaN;   // entarteter Stab
            axis /= L;

            // rest = Summe aller Stabkräfte am Knoten außer Kante e, plus Last.
            // Jeder Term q_i·(x_j − x_node) ist eine Stabkraft als Vektor (Betrag q_i·L_i).
            Vector3d rest = Vector3d.Zero;
            int[] nb = neighborCache[node];
            for (int i = 0; i < nb.Length; i++)
            {
                if (edgeCacheN[node][i] == e) continue;   // Druckstab selbst auslassen
                rest += qCache[node][i] * (positions[nb[i]] - positions[node]);
            }
            rest -= loadVec[node];

            // Nur die axiale Komponente von rest kann der Stab aufnehmen
            return -(rest * axis) / L;
        }

        // ─────────────────────────────────────────────
        //  Methode: Randentknickung 
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
