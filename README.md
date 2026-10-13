# Opuntia
Force Density Framework for Grasshopper

[![Download](https://img.shields.io/github/v/release/barisWenzel/Opuntia?label=Download&style=for-the-badge)](https://github.com/barisWenzel/Opuntia/releases/latest)

![GitHub downloads](https://img.shields.io/github/downloads/barisWenzel/Opuntia/total?label=GitHub%20downloads)
![License](https://img.shields.io/github/license/barisWenzel/Opuntia)
![Rhino](https://img.shields.io/badge/Rhino-8-blue)

<p align="center">
<img src="docs/githubIcon.svg" width="500">
</p>

## Description
Opuntia is a framework for structural form-finding with the Force Density Method (FDM) according to Linkwitz and Schek for the algorithmic modelling software [Grasshopper](https://www.grasshopper3d.com/), which is a plug-in of the commercial 3D computer graphics and computer-aided design application software [Rhinoceros](https://www.rhino3d.com/) (short Rhino3D). The main contribution is an iterative solver based on Gauss-Seidel relaxation that computes equilibrium geometries of cable nets and shells directly on a line graph. Constraints such as prescribed edge lengths or the dekinking of boundary nodes are added through a module-based option mechanism, so that further solver methods can be integrated without changing the core solver. The framework was developed in the context of the digital reconstruction of historical cable net structures at the University of Stuttgart. Results were validated against STIFF3D on several examples with deviations below 0.1 %.

The name refers to the opuntia spider (*Cyrtophora citricola*), which is commonly found on prickly pear cacti and builds pretensioned, anticlastic webs. Webs of this kind are documented in the publications of Frei Otto's Institute for Lightweight Structures (IL) in Stuttgart.

<p align="center">
  <img src="docs/opuntia_net_wiki.jpg" width="500"><br>
  <sub>Photo: Olaf Leillinger, Wikimedia Commons, <a href="https://creativecommons.org/licenses/by/2.5/">CC BY 2.5</a></sub>
</p>


## Method
Each node $i$ is in equilibrium when the edge forces to its neighbours and the external load cancel out:

$$\sum_{j \in N(i)} q_{ij}\,(x_j - x_i) + p_i = 0$$

Solving for $x_i$ gives the Gauss-Seidel update used in each iteration $k$:

$$x_i^{(k+1)} = \frac{\sum_{j \in N(i),\, j<i} q_{ij}\,x_j^{(k+1)} + \sum_{j \in N(i),\, j>i} q_{ij}\,x_j^{(k)} + p_i}{\sum_{j \in N(i)} q_{ij}}$$

where $x_i$ is the position of node $i$, $N(i)$ the set of its neighbour nodes, $q_{ij}$ the force density of the edge between $i$ and $j$, and $p_i$ the external load at node $i$. In words: each free node moves to the force-density-weighted average of its neighbours, shifted by its load. Nodes are updated one after another, and each update already uses the new positions of the neighbours processed before it ($j<i$). This makes the method converge faster than updating all nodes simultaneously. Fixed axes keep their initial coordinates. Target Length options act during iteration; De-Kink options act after convergence.

### Convergence
The solver stops when the largest node displacement between two iterations falls below the tolerance, or when It is reached:

$$\max_i \left| x_i^{(k)} - x_i^{(k-1)} \right| < \text{Tol}$$

## Overview

### Typical workflow
All example images use the same labels. Opuntia components: **OP1** Line Graph · **OP2** Solver · **OP3** Target Length · **OP4** De-Kink · **OP5** Replace By Indices. Inputs: **in1** Lines · **in2** Supports · **in3** Loads · **in4** Force densities. Outputs: **out1** Axial forces (N) · **out2** Edges (E). out1 shows the axial forces as an example, all solver outputs are described under OP2. out2 is displayed with a colour expression (blue = tension, red = compression), which is not part of Opuntia. Labels that do not occur in an example are omitted.

![Typical workflow](docs/examples/1.png)

## Examples
Example definitions are provided in the `examples/` folder. Each definition follows the same structure:

1. **Graph (OP1)**: To work with the FDM, the input lines must first be converted into a graph using the LGraph component. It merges vertices within the tolerance, removes duplicate edges and outputs a graph object G, which is passed directly to the solver. Vertices, edges and the adjacency trees (PP, PL, LP) can be exposed as additional outputs by zooming in on the component.
2. **Solver (OP2)**: The solver returns the equilibrium node positions (V), the axial force (N = q · L) and length (L) of each edge and the resulting edge geometry (E). The residual force (Res) and the final displacement (dV) of each node can be exposed by zooming in on the component and are used to check convergence.

All index lists (edges, vertices) refer to the order of LGraph. If the input geometry is changed, they must be updated accordingly. Force densities and index lists can be created in any way, for example with a panel or from an Excel file.

### Example 1 · Four-point net
![Four-point net](docs/examples/1.png)

Form-finding of a classic four-point cable net. Two anchor points are raised and two are lowered, resulting in an anticlastic, saddle-shaped surface. Coordinates, force densities and load vectors are taken from the [STIFF example dataset of TU Munich](https://www.cee.ed.tum.de/fileadmin/w00cbe/st/Software/Education/Stiff/Beispiele/Kraftdichte_4P-Zelt_1_3D.xlsx).

1. **Force densities (in4)**: All edges are given q = 1. The edge cables listed in `Indices_Edge_Cables` are then overwritten with q = 5.
2. **Loads (in3)**: For the form-finding of prestressed tension structures the load vector is {0, 0, 0}. As this is the default, P can be left empty.

Note: STIFF uses a downward z-axis. For the example files, the coordinates and load vectors were converted to the Rhino convention (z-axis pointing up). The force densities are unaffected.

### Example 2 · Net with masts and target length
![Net with masts](docs/examples/2.png)

Cable net supported by two masts with guy ropes.

1. **Force densities (in4)**: All edges are given q = 1. The edge cables are then overwritten with q = 5, the struts with q = −5 and the guy ropes with q = 10.
2. **Loads (in3)**: Prestressed form-finding, P is left empty.
3. **Target length (OP3)**: The struts (red) are given a prescribed length.

### Example 3 · Shell under load
![Shell under load](docs/examples/3.png)

Compression shell (red) under vertical loads.

1. **Loads (in3)**: All vertices are given the vector {0, 0, −1}. The edge vertices are then overwritten with {0, 0, −2}.
2. **Force densities (in4)**: All edges are given q = −1. The edge cables are then overwritten with q = −5. Compression members require a negative force density.

### Example 4 · Shell with target length
![Shell with target length](docs/examples/4.png)

As Example 3, with a prescribed length for the inner members. Green: constrained edges.

1. **Loads (in3)** and **force densities (in4)**: as in Example 3.
2. **Target length (OP3)**: Each constrained member is assigned the desired length.

### Example 5 · De-Kink
![De-Kink](docs/examples/5.png)

Top view of a shell with De-Kink applied to the boundary vertices. Green: result without De-Kink.

1. **Loads (in3)** and **force densities (in4)**: as in Example 3.
2. **De-Kink (OP4)**: The boundary vertices are fed into BI and an angle tolerance is assigned. The number of smoothing passes (It) is adjusted until the desired result is obtained.

### Example 6 · Olympic roof (4 panels)
![Olympic roof](docs/examples/6.png)

Four panels of the cable net roof of the Munich Olympic Stadium, supported by four masts with guy ropes.

1. **Force densities (in4)**: The values are taken from STIFF3D (z-axis converted, see Example 1).
2. **Loads (in3)**: Prestressed form-finding, P is left empty.
3. **Target length (OP3)**: All four masts are assigned their desired length.

### Example 7 · Per-axis supports
![Per-axis supports](docs/examples/7.png)

Supports can be fixed per axis. The example uses the four-point net from Example 1. Green: result with all axes of the free vertices released.

1. **Supports (in2)**: Each entry of `Fix` consists of a vertex index and the axes to be fixed (e.g. `3xy`). The value lists switch the x, y and z axes of one anchor and of the free vertices on or off. The selected axes are joined to the index and passed to the solver.
2. Try fixing individual axes and observe the result. For example, a free vertex fixed in x and y can only move vertically, like a ring sliding along a mast.

Note: If all axes of the free vertices are fixed, no vertex can move and no equilibrium shape can be found.

### Example 8 · Assigning force densities
![Assigning force densities](docs/examples/8.png)

Different force densities are assigned to groups of edges with Replace By Indices (OP5). All edges start with q = 1. The edges listed in each index group (edge cables, guy ropes, struts) are overwritten with the corresponding value.


---

### OP1 · Line Graph `LGraph`

<table>
<tr><th>Inputs</th><th></th><th>Outputs</th></tr>
<tr>
<td valign="top">

| Name | Description |
|---|---|
| L | Input lines |
| *Tol* | *Point merge tolerance (default: model tolerance)* |

</td>
<td valign="middle" align="center" width="170"><img src="docs/components/lgraph.png" width="150"></td>
<td valign="top">

| Name | Description |
|---|---|
| G | Graph object for the solver |
| *V* | *Vertex positions* |
| *E* | *Edges* |
| *PP* | *Point-to-point adjacency (path = vertex, values = neighbour vertices)* |
| *PL* | *Point-to-line adjacency (path = vertex, values = incident edges)* |
| *LP* | *Line-to-point adjacency (path = edge, values = start and end vertex)* |

</td>
</tr>
</table>

Builds a graph from a list of lines. Points within the tolerance are merged to the first point found; duplicate and degenerate edges are removed. The graph `G` is passed to the solver as a single object.

> **Zoomable interface (ZUI):** *Italic* parameters are optional and hidden by default. Zoom in on the component until **+** and **−** appear next to the parameters, then click **+** to add the next optional parameter or **−** to remove the last one. If an optional input is not shown, its default value is used.

---

### OP2 · Force Density Solver `FDSolver`

<table>
<tr><th>Inputs</th><th></th><th>Outputs</th></tr>
<tr>
<td valign="top">

| Name | Description |
|---|---|
| Fix | Supports, see below (optional) |
| G | Graph from LGraph |
| P | Load vector per node (optional) |
| q | Force density per edge |
| O | Target Length and/or De-Kink options (optional) |
| *It* | *Max. iterations (default 1000, max. 10 000)* |
| *Tol* | *Convergence tolerance (default: model tolerance)* |

</td>
<td valign="middle" align="center" width="170"><img src="docs/components/solver.png" width="150"></td>
<td valign="top">

| Name | Description |
|---|---|
| V | Node positions in equilibrium |
| N | Axial force per member, N = q · L |
| L | Member lengths |
| E | Result edges |
| *Res* | *Residual force per node (fixed axes excluded)* |
| *dV* | *Node displacement in the final iteration* |

</td>
</tr>
</table>

Iterative Force Density solver (Gauss-Seidel relaxation) for equilibrium form-finding of cable nets and shells. Optional parameters are added via ZUI (see OP1). The message below the component shows the number of iterations (`it`), the largest nodal residual (`Res`) and the computation time (`t`). For the underlying method, see [Method](#method).

#### Supports
Each entry of `Fix` is a vertex index, optionally followed by the axes to be fixed:

| Entry | Meaning |
|---|---|
| `3` | Vertex 3 fixed in x, y and z |
| `3xy` | Vertex 3 fixed in x and y, free in z |
| `7z` | Vertex 7 fixed in z, free in x and y |

A plain list of integers can be connected directly. Vertex indices can be checked with *Point List* or found with *Closest Point*.

#### Sign convention
| Value | Meaning |
|---|---|
| q > 0 | Tension (cable) |
| q < 0 | Compression (strut/mast) |

#### Warnings
| Warning | Cause |
|---|---|
| Not converged | The tolerance was not reached within It iterations. Increase It or check the input. |
| No equilibrium | The displacements have converged, but the largest residual exceeds 0.1 % of the largest member force. Supports, fixed axes or constraints over-determine the system. |
| No supports provided | No `Fix` input. Only self-stressed systems can be solved without supports. |

---

### OP3 · Target Length `TargetL`

<table>
<tr><th>Inputs</th><th></th><th>Outputs</th></tr>
<tr>
<td valign="top">

| Name | Description |
|---|---|
| EI | Edge indices subject to the target-length constraint |
| Le | Target lengths, paired with EI |

</td>
<td valign="middle" align="center" width="170"><img src="docs/components/targetlength.png" width="150"></td>
<td valign="top">

| Name | Description |
|---|---|
| O | Option object, connect to the solver's O input |

</td>
</tr>
</table>

Target-length constraint for selected edges (e.g. compression struts). The force density of a constrained edge is not held constant: in each correction step it is derived from local nodal equilibrium, so that the edge carries exactly the residual force along its axis.

A target length can only be reached if the system has enough free degrees of freedom. If supports, fixed axes and force densities already determine the position of the affected nodes, the system is over-determined and the target length cannot be met. In this case the solver issues a warning (see *Warnings* under OP2).

---

### OP4 · De-Kink `DeKink`

<table>
<tr><th>Inputs</th><th></th><th>Outputs</th></tr>
<tr>
<td valign="top">

| Name | Description |
|---|---|
| BI | Indices of boundary nodes to dekink |
| Tol | Angle tolerance in degrees for extrapolation (default 30) |
| It | Number of dekink and re-relaxation passes (default 3) |

</td>
<td valign="middle" align="center" width="170"><img src="docs/components/dekink.png" width="150"></td>
<td valign="top">

| Name | Description |
|---|---|
| O | Option object, connect to the solver's O input |

</td>
</tr>
</table>

Boundary dekinking. Removes kinks at selected boundary nodes by extrapolation along collinear neighbours, followed by re-relaxation. It acts after convergence of the solver.

---

### OP5 · Replace By Indices `ReplaceIndices`

<table>
<tr><th>Inputs</th><th></th><th>Outputs</th></tr>
<tr>
<td valign="top">

| Name | Description |
|---|---|
| L | Input list |
| I | Indices of the items to replace |
| V | Replacement values, paired with I |

</td>
<td valign="middle" align="center" width="170"><img src="docs/components/replace.png" width="150"></td>
<td valign="top">

| Name | Description |
|---|---|
| R | Resulting list |

</td>
</tr>
</table>

Replaces items of a list at given indices, e.g. to assign individual force densities to groups of edges without data tree handling (see Example 8).

---

## Requirements
- Rhino 8
- .NET Framework 4.8

## Installation
1. Download the `.gha` from the latest [release](../../releases) or build it in Visual Studio (Release).
2. Right-click the file → Properties → **Unblock**.
3. Copy the `.gha` to `%APPDATA%\Grasshopper\Libraries\`.
4. Restart Rhino.

## License
MIT License © 2026 Baris Wenzel. See `LICENSE` for details.
