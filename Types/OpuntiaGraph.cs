using System.Collections.Generic;
using System.Linq;
using Grasshopper;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace Opuntia
{
    /// <summary>
    /// Graph-Topologie: Knoten, Kanten und Adjazenzen.
    /// PP[v][i] und PL[v][i] gehoeren zusammen (Nachbar i ueber Kante i).
    /// </summary>
    public class OpuntiaGraph
    {
        public Point3d[] Vertices { get; }
        public Line[] Edges { get; }
        public int[][] PP { get; }   // Knoten -> Nachbarknoten
        public int[][] PL { get; }   // Knoten -> anliegende Kanten
        public int[][] LP { get; }   // Kante  -> [Start, Ende]

        public int VertexCount => Vertices.Length;
        public int EdgeCount => Edges.Length;

        public OpuntiaGraph(Point3d[] vertices, Line[] edges, int[][] pp, int[][] pl, int[][] lp)
        {
            Vertices = vertices;
            Edges = edges;
            PP = pp;
            PL = pl;
            LP = lp;
        }

        public bool IsValid =>
            Vertices != null && Edges != null && PP != null && PL != null && LP != null &&
            PP.Length == VertexCount && PL.Length == VertexCount && LP.Length == EdgeCount;

        public OpuntiaGraph Duplicate() => new OpuntiaGraph(
            (Point3d[])Vertices.Clone(),
            (Line[])Edges.Clone(),
            PP.Select(a => (int[])a.Clone()).ToArray(),
            PL.Select(a => (int[])a.Clone()).ToArray(),
            LP.Select(a => (int[])a.Clone()).ToArray());

        /// <summary>Jagged Array als DataTree, Pfad = Index.</summary>
        public static DataTree<int> ToTree(int[][] data)
        {
            var tree = new DataTree<int>();
            for (int i = 0; i < data.Length; i++)
                tree.AddRange(data[i], new GH_Path(i));
            return tree;
        }
    }

    /// <summary>Wrapper, damit der Graph durch Grasshopper-Draehte laeuft.</summary>
    public class GH_OpuntiaGraph : GH_Goo<OpuntiaGraph>
    {
        public GH_OpuntiaGraph() { }
        public GH_OpuntiaGraph(OpuntiaGraph graph) : base(graph) { }

        public override bool IsValid => Value != null && Value.IsValid;
        public override string TypeName => "Opuntia Graph";
        public override string TypeDescription => "Graph topology: vertices, edges and adjacency.";

        public override IGH_Goo Duplicate() => new GH_OpuntiaGraph(Value?.Duplicate());

        public override string ToString() =>
            Value == null ? "Null graph" : $"Opuntia Graph (V: {Value.VertexCount}, E: {Value.EdgeCount})";
    }
}
