Hier die aktualisierte README mit beiden Komponenten:

---

# Opunita — Force Density Grasshopper Plugin

**A Grasshopper/Rhino plugin for structural form-finding using the Force Density Method (FDM).**

---

## Components

The plugin contains two components:

| Component | Nickname | Category |
|-----------|----------|----------|
| Line Graph | `LGraph` | Opunita / Graph |
| Force Density | `FD` | Opunita / Equilibrium |

A typical workflow:

```
Lines → [Line Graph] → Points, Edges, PP, PL, LP
                              ↓
                      [Force Density] → Equilibrium Geometry
```

---

## 1 · Line Graph `LGraph`

Builds a graph topology from a list of lines. Deduplicates vertices and edges, and outputs adjacency trees for use in the solver.

### Inputs

| Name | Type | Description |
|------|------|-------------|
| `L` | `List<Line>` | Input lines |
| `SE` | `bool` | Show edge index labels at midpoints |
| `SV` | `bool` | Show vertex index labels at points |

### Outputs

| Name | Type | Description |
|------|------|-------------|
| `P` | `List<Point3d>` | Deduplicated vertex positions |
| `E` | `List<Line>` | Deduplicated edges |
| `PP` | `DataTree<int>` | Point–Point adjacency — path = vertex index, values = neighbour indices |
| `PL` | `DataTree<int>` | Point–Edge adjacency — path = vertex index, values = edge indices |
| `LP` | `DataTree<int>` | Edge–Point adjacency — path = edge index, values = [start, end] vertex indices |
| `Labels` | `List<TextDot>` | Viewport labels for vertices and/or edges |

### Notes

- Vertex deduplication uses model tolerance (`RhinoDoc.ModelAbsoluteTolerance`)
- Degenerate edges (start == end) are skipped
- Duplicate edges are ignored regardless of direction

---

## 2 · Force Density Solver `FD`

Iterative Force Density solver for equilibrium form-finding of cable-net and shell structures.

### Inputs

| Name | Type | Description |
|------|------|-------------|
| `anchorIndices` | `List<int>` | Indices of fixed anchor points |
| `points` | `List<Point3d>` | Input point positions (from `LGraph`) |
| `edges` | `List<Line>` | Input edges (from `LGraph`) |
| `PP` | `DataTree<int>` | Point–Point adjacency (from `LGraph`) |
| `PL` | `DataTree<int>` | Point–Edge adjacency (from `LGraph`) |
| `P` | `List<Vector3d>` | External load vectors per node |
| `q` | `List<double>` | Force density per edge |
| `fixedEdges` | `List<int>` | Edge indices with target length *(optional)* |
| `targetLengths` | `List<double>` | Target lengths for fixed edges *(optional)* |
| `maxIterations` | `int` | Maximum iterations (capped at 10 000) |

### Outputs

| Name | Type | Description |
|------|------|-------------|
| `A` | `List<Point3d>` | Resulting node positions |
| `EdgeLines` | `List<Line>` | Resulting edge geometry |
| `F_out` | `List<double>` | Force per edge `q · L` |
| `L_out` | `List<double>` | Length per edge |

### Sign Convention

| Value | Meaning |
|-------|---------|
| `q > 0` | Tension (cable) |
| `q < 0` | Compression (strut/mast) |

### Convergence

The solver stops when:

$$\max_i \| \mathbf{x}_i^{(k)} - \mathbf{x}_i^{(k-1)} \| < \varepsilon$$

where $\varepsilon$ is the model tolerance. Iteration count and elapsed time are displayed on the component.

---

## Method

The Force Density Method (Schek, 1974) expresses nodal equilibrium as:

$$\sum_{j \in N(i)} q_{ij} \cdot (\mathbf{x}_j - \mathbf{x}_i) + \mathbf{p}_i = 0$$

Rearranged for the iterative update:

$$\mathbf{x}_i^{(k+1)} = \frac{\sum_{j \in N(i)} q_{ij} \cdot \mathbf{x}_j^{(k)} + \mathbf{p}_i}{\sum_{j \in N(i)} q_{ij}}$$

Anchor nodes remain fixed throughout iteration.

---

## Requirements

- Rhino 7 or 8
- Grasshopper (included in Rhino)
- .NET Framework 4.8

---

## Installation

1. Build in Visual Studio (Release)
2. Copy `.gha` to:
   ```
   %APPDATA%\Grasshopper\Libraries\
   ```
3. Rechtsklick → Eigenschaften → **Unblock**
4. Restart Rhino

---

## License

```
MIT License

Copyright (c) [YEAR] [YOUR NAME]

Permission is hereby granted, free of charge, to any person obtaining
a copy of this software and associated documentation files (the "Software"),
to deal in the Software without restriction, including without limitation
the rights to use, copy, modify, merge, publish, distribute, sublicense,
and/or sell copies of the Software, and to permit persons to whom the
Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included
in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND.
```

---

## Citation

```bibtex
@software{[yourname]_nephila_[year],
  author    = {Baris, Wenzel},
  title     = {Opunita — Force Density Equilibrium Plugin for Grasshopper},
  year      = {2026},
  url       = {https://github.com/barisWenzel/Opunita},
  note      = {Developed as part of doctoral dissertation, [Universitaet]}
}
```

---

## References

- Schek, H.-J. (1974). *The force density method for form finding and computation of general networks.* Computer Methods in Applied Mechanics and Engineering, 3(1), 115–134.

---

## Author

**[Baris Wenzel]**  
[Universitaet Stuttgart]  
