import os
import struct
import numpy as np
import matplotlib.pyplot as plt
import pandas as pd
from glob import glob

RESULTS_DIR = "Ant-Colony-Results"

def load_chunk_volume(chunk_file):
    with open(chunk_file, "rb") as f:
        padded = struct.unpack("i", f.read(4))[0]
        count = struct.unpack("i", f.read(4))[0]
        data = np.frombuffer(f.read(count * 4), dtype=np.float32)
        return np.sum(data < 0)  # count empty voxels

def load_ant_positions(csv_file):
    try:
        # Try to read CSV with or without header
        df = pd.read_csv(csv_file, header=None)

        # Find columns with numerical X, Y, Z (assumed to be 1:4)
        df_numeric = df.iloc[:, 1:4].apply(pd.to_numeric, errors="coerce")

        # Drop rows with invalid values
        df_clean = df_numeric.dropna()

        if df_clean.empty:
            return None
        return df_clean.values
    except Exception as e:
        print(f"Failed to read ant CSV: {csv_file} -> {e}")
        return None

def load_pheromone_totals(folder, timestep):
    totals = {}
    for f in glob(os.path.join(folder, f"*_{timestep}.bin")):
        name = os.path.basename(f).split("_")[0]
        with open(f, "rb") as binf:
            width = struct.unpack("i", binf.read(4))[0]
            count = struct.unpack("i", binf.read(4))[0]
            data = np.frombuffer(binf.read(count * 4), dtype=np.float32)
            totals[name] = float(np.sum(data))
    return totals

def analyze_simulation(sim_path):
    chunk_dir = os.path.join(sim_path, "chunks")
    ant_dir = os.path.join(sim_path, "ants")
    pher_dir = os.path.join(sim_path, "pheromones")

    all_timesteps = sorted(set([
        int(os.path.basename(f).split("_t")[1].split(".")[0])
        for f in glob(os.path.join(chunk_dir, "*.bin"))
    ]))

    results = []

    for t in all_timesteps:
        chunk_files = glob(os.path.join(chunk_dir, f"*t{t}.bin"))
        total_empty_voxels = sum(load_chunk_volume(f) for f in chunk_files)

        ant_file = os.path.join(ant_dir, f"ants_t{t}.csv")
        positions = load_ant_positions(ant_file)
        ant_count = len(positions) if positions is not None else 0
        center_of_mass = np.mean(positions, axis=0) if positions is not None else [np.nan] * 3

        pher_totals = load_pheromone_totals(pher_dir, t)

        results.append({
            "timestep": t,
            "nest_volume": total_empty_voxels,
            "ant_count": ant_count,
            "ant_x": center_of_mass[0],
            "ant_y": center_of_mass[1],
            "ant_z": center_of_mass[2],
            **{f"pheromone_{k}": v for k, v in pher_totals.items()}
        })

    return pd.DataFrame(results)

def main():
    all_simulations = sorted(glob(os.path.join(RESULTS_DIR, "Simulation_*")))

    for sim_path in all_simulations:
        print(f"\nAnalyzing: {sim_path}")
        df = analyze_simulation(sim_path)
        if df.empty:
            print("No data found.")
            continue

        # Save to CSV
        df.to_csv(os.path.join(sim_path, "summary.csv"), index=False)

        # Plot: nest volume
        plt.figure()
        plt.plot(df["timestep"], df["nest_volume"])
        plt.xlabel("Timestep")
        plt.ylabel("Nest Volume (voxels)")
        plt.title("Nest Volume Over Time")
        plt.grid()
        plt.savefig(os.path.join(sim_path, "nest_volume.png"))

        # Plot: pheromone levels
        pher_cols = [col for col in df.columns if col.startswith("pheromone_")]
        if pher_cols:
            plt.figure()
            for col in pher_cols:
                plt.plot(df["timestep"], df[col], label=col)
            plt.xlabel("Timestep")
            plt.ylabel("Total Pheromone")
            plt.title("Pheromone Totals Over Time")
            plt.legend()
            plt.grid()
            plt.savefig(os.path.join(sim_path, "pheromones.png"))

        print(f"Done. Results saved in: {sim_path}")

if __name__ == "__main__":
    main()
