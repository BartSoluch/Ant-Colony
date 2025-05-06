
# Stigmergy-Driven 3D Ant Nest Simulation

A biologically inspired 3D simulation of leafcutter ants constructing underground nests using stigmergic principles. Built in Unity, with both CPU and GPU implementations of voxel-based terrain and marching cubes meshing.

---

## Getting Started

### Requirements
- Unity 2022.3 LTS (or compatible)
- Windows or macOS
- GPU with compute shader support (for GPU version)

---

## Project Structure

### Scenes
- `Scenes/CPUMarchingCubes`: CPU-based voxel terrain and simulation.
- `Scenes/GPUMarchingCubes`: GPU-accelerated version using compute shaders. **Recommended** for performance and large-scale tests.

---

## Running the Simulation

1. Open the Unity project.
2. Load the desired scene (`GPUMarchingCubes` for GPU, `CPUMarchingCubes` for fallback).
3. Press **Play**.

The simulation initializes with a queen ant and a number of workers and scouts. The colony proceeds to build an underground nest based on stigmergic cues and environmental biases.

---

## Features

### Agent-Based Simulation
- **Ant Roles**:
  - `Queen`: Lays eggs and settles at optimal locations.
  - `Worker`: Digs and expands chamber zones.
  - `Scout`: Explores and deposits trail pheromones.
- **Lifespan & Energy**: Ants age, consume energy, and die. New ants are spawned based on colony needs.
- **Emergent Behavior**: Agents follow simple rules, leading to complex tunnel systems.

### 3D Voxel Terrain
- Terrain is generated with **Marching Cubes**.
- Diggable terrain is updated in real-time (CPU or GPU depending on scene).
- Chambers are classified as:
  - Fungus Garden
  - Nursery
  - Waste Dump

### Environmental Gradients
- Water and CO₂ fields influence digging preferences.
- Chamber zones are assigned based on depth and environmental conditions.

### Stigmergy System
- Ants use pheromone grids:
  - **Trail**
  - **Dig**
  - **Nest**
  - **Chamber**
- Pheromones are deposited, decayed, and sensed to guide decision-making.

### Snapshot Exporting
- Data saved every X seconds:
  - Ant states (CSV)
  - Terrain density (binary)
  - Chamber distribution
- Output directory: `../Ant-Colony-Results/Simulation_<timestamp>`

---

## Tunable Parameters

### `AntSpawner.cs`
- `numberOfAnts`: Initial non-queen ants.

### `AntAgent.cs`
- `moveSpeed`, `digRadius`, `digCooldown`: Movement and digging behaviors.
- `waterBias`, `co2Bias`, `trailPheroBias`, `randomnessBias`, etc.: Environmental sensitivities.
- `maxAge`, `energyDecayRate`: Lifespan dynamics.

### `ChunkManager.cs`
- `worldSizeX/Y/Z`: Size of the voxel world.
- `seed`: Controls terrain randomness.
- `groundVariationMultiplier`, `baseGroundHeightMultiplier`: Terrain features.

### `TimeScaleController.cs`
- UI slider that adjusts simulation speed (0.1x to 5x).

---

## Controls

| Action                    | Key/Mouse            |
|---------------------------|----------------------|
| Move (WASD)               | W, A, S, D           |
| Ascend/Descend            | E / Q                |
| Rotate camera             | Mouse drag           |
| Dig at cursor             | Left click           |
| Toggle mouse lock         | Escape               |
| Change time scale         | UI slider (top left) |

---

## Output Files

When simulation is running, snapshots are saved to:
```
../Ant-Colony-Results/Simulation_<timestamp>/
├── ants/         # CSV logs of all ant agents
├── chunks/       # Binary density data for terrain
├── pheromones/   # (optional) pheromone fields
├── chambers/     # CSV summary of chamber volume by type
```

---

## Metrics & Evaluation

- Colony size is capped by excavated volume and food availability.
- Each ant logs its biases, role, state, and position.
- Chamber classification reflects environmental adaptation (e.g., fungus gardens in moist zones).

---

## Developer Tips

- **Debug Density**: Select ants in scene view to see local density/gradient lines.
- **Export Data**: Customize `SimulationSnapshotExporter.cs` for custom output formats or intervals.
- **Performance**: Use GPU scene for large simulations (100+ ants); CPU scene for debugging.

---

## Known Issues
- Ants may occasionally get stuck in zero-normal areas. They recover over time.
- Partial remeshing is not yet implemented; full remeshes occur based on voxel dirty thresholds.
- Pheromone visualizations are stubbed but not fully enabled.

---
