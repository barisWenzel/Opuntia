using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Opuntia
{
    // ─────────────────────────────────────────────────────────────────────────
    //  HINWEIS: Point3d ist ein Value-Type (Struct). Ein "null" Point3d würde
    //  stillschweigend zu (0,0,0) konvertiert – daher niemals direkt
    //  DA.GetDataList<Point3d>() verwenden, sondern immer GH_Point einlesen
    //  und danach auf null prüfen, bevor .Value abgerufen wird.
    //  Quelle: https://discourse.mcneel.com/t/avoiding-null-point-conversion-
    //          to-0-0-0-in-gh-components/208434/2
    // ─────────────────────────────────────────────────────────────────────────

    public class MergeRecord
    {
        public int VertexIndex { get; set; }
        public List<int> RawIndices { get; } = new List<int>();
        public Point3d VertexPosition { get; set; }
        public bool WasMerged => RawIndices.Count > 1;

        public override string ToString()
        {
            string raws = string.Join(" + ", RawIndices.Select(i => $"raw[{i}]"));
            Point3d p = VertexPosition;
            return $"{raws} → v[{VertexIndex}] @ ({p.X:F3}, {p.Y:F3}, {p.Z:F3})";
        }
    }

    public class EdgeMergeRecord
    {
        public int RemovedRawIndex { get; set; }
        public int KeptEdgeIndex { get; set; }
        public bool WasDegenerate { get; set; }
        public string Reason { get; set; }

        public override string ToString()
            => WasDegenerate
                ? $"raw_line[{RemovedRawIndex}] entfernt – degeneriert"
                : $"raw_line[{RemovedRawIndex}] entfernt – Duplikat von e[{KeptEdgeIndex}]";
    }

    internal static class GraphHelpers
    {
        // ─────────────────────────────────────────────────────────────────────
        //  BuildClusters
        //  Ansatz: wie GH "Duplicate Points" –
        //  Sortiere Punkte räumlich, vergleiche nur nahe Nachbarn,
        //  bilde Cluster, berechne Centroid.
        // ─────────────────────────────────────────────────────────────────────
        public static void BuildClusters(
            IList<Point3d> rawPoints,
            double tolerance,
            out List<Point3d> vertices,
            out int[] rawToVertex,
            out List<MergeRecord> mergeRecords)
        {
            int n = rawPoints.Count;
            rawToVertex = new int[n];
            for (int i = 0; i < n; i++) rawToVertex[i] = -1;

            vertices = new List<Point3d>();
            mergeRecords = new List<MergeRecord>();

            double tolSq = tolerance * tolerance;

            // Sortiere Indizes nach X, dann Y, dann Z
            // → Duplikate liegen danach nebeneinander
            int[] sorted = Enumerable.Range(0, n)
                .OrderBy(i => rawPoints[i].X)
                .ThenBy(i => rawPoints[i].Y)
                .ThenBy(i => rawPoints[i].Z)
                .ToArray();

            // Für jeden Punkt: suche alle noch nicht zugewiesenen Punkte
            // im Toleranzbereich (nur vorwärts nötig wegen Sortierung)
            for (int si = 0; si < n; si++)
            {
                int i = sorted[si];
                if (rawToVertex[i] != -1) continue; // bereits einem Cluster zugewiesen

                // Neuer Cluster – sammle alle Punkte in Toleranz
                var clusterRaw = new List<int> { i };
                Point3d pi = rawPoints[i];

                for (int sj = si + 1; sj < n; sj++)
                {
                    int j = sorted[sj];

                    // Frühzeitig abbrechen: wenn X-Abstand > tol, können
                    // keine weiteren Punkte in Reichweite sein
                    if (rawPoints[j].X - pi.X > tolerance) break;

                    if (rawToVertex[j] == -1 &&
                        pi.DistanceToSquared(rawPoints[j]) <= tolSq)
                    {
                        clusterRaw.Add(j);
                    }
                }

                // Centroid berechnen
                double cx = 0, cy = 0, cz = 0;
                foreach (int ri in clusterRaw)
                {
                    cx += rawPoints[ri].X;
                    cy += rawPoints[ri].Y;
                    cz += rawPoints[ri].Z;
                }
                int cnt = clusterRaw.Count;
                var centroid = new Point3d(cx / cnt, cy / cnt, cz / cnt);

                int vertexIdx = vertices.Count;
                vertices.Add(centroid);

                foreach (int ri in clusterRaw)
                    rawToVertex[ri] = vertexIdx;

                var record = new MergeRecord
                {
                    VertexIndex = vertexIdx,
                    VertexPosition = centroid
                };
                record.RawIndices.AddRange(clusterRaw);
                mergeRecords.Add(record);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  EnsureVertexDicts
        // ─────────────────────────────────────────────────────────────────────
        public static void EnsureVertexDicts(
            int idx,
            Dictionary<int, HashSet<int>> ppDict,
            Dictionary<int, HashSet<int>> plDict)
        {
            if (!ppDict.ContainsKey(idx)) ppDict[idx] = new HashSet<int>();
            if (!plDict.ContainsKey(idx)) plDict[idx] = new HashSet<int>();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  RegisterEdge
        // ─────────────────────────────────────────────────────────────────────
        public static void RegisterEdge(
            int a, int b,
            int rawLineIndex,
            List<Point3d> vertices,
            List<Line> edges,
            Dictionary<long, int> edgeSet,
            Dictionary<int, HashSet<int>> ppDict,
            Dictionary<int, HashSet<int>> plDict,
            Dictionary<int, int[]> lpDict,
            List<EdgeMergeRecord> edgeReports)
        {
            // Degenerierte Kante
            if (a == b)
            {
                edgeReports.Add(new EdgeMergeRecord
                {
                    RemovedRawIndex = rawLineIndex,
                    WasDegenerate = true,
                    Reason = "Beide Endpoints im selben Vertex-Cluster"
                });
                return;
            }

            // Kanonischer Key: kleinerer Index zuerst
            int lo = Math.Min(a, b);
            int hi = Math.Max(a, b);
            long key = ((long)lo << 32) | (uint)hi;

            if (edgeSet.TryGetValue(key, out int existingEdge))
            {
                edgeReports.Add(new EdgeMergeRecord
                {
                    RemovedRawIndex = rawLineIndex,
                    KeptEdgeIndex = existingEdge,
                    WasDegenerate = false,
                    Reason = $"Identisch mit e[{existingEdge}]"
                });
                return;
            }

            int edgeIdx = edges.Count;
            edgeSet[key] = edgeIdx;
            edges.Add(new Line(vertices[a], vertices[b]));
            lpDict[edgeIdx] = new[] { a, b };

            ppDict[a].Add(b);
            ppDict[b].Add(a);
            plDict[a].Add(edgeIdx);
            plDict[b].Add(edgeIdx);
        }
    }
}
