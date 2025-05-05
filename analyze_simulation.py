import os
import struct
import numpy as np
import matplotlib.pyplot as plt
import pandas as pd
from glob import glob

RESULTS_DIR = "Ant-Colony-Results"

def load_chunk_volume(chunk_file):
    with open(chunk_file, "rb") as f:
        header = f.read(8)
        if len(header) < 8:
            print(f"Skipping corrupt chunk file: {chunk_file}")
            return 0
        padded, count = struct.unpack("ii", header)
        data = np.frombuffer(f.read(count * 4), dtype=np.float32)
        return np.sum(data < 0)  # count empty voxels

def load_ant_positions(csv_file):
    try:
        df = pd.read_csv(csv_file, header=None)

        # Get X, Y, Z columns: pos.x = col 2, pos.y = col 3, pos.z = col 4
        df_numeric = df.iloc[:, 2:5].apply(pd.to_numeric, errors="coerce")
        df_clean = df_numeric.dropna()

        if df_clean.empty:
            return None
        return df_clean.values
    except Exception as e:
        print(f"Failed to read ant CSV: {csv_file} -> {e}")
        return None

def load_pheromone_totals(folder, timestep):
    totals = {}
    for f in glob(os.path.join(folder, f"*_t{timestep}.bin")):
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
    queen_death_timestep = None

    for t in all_timesteps:
        ant_file = os.path.join(ant_dir, f"ants_t{t}.csv")
        try:
            with open(ant_file, "r") as f:
                lines = f.readlines()

            # Skip timesteps with no ants
            if not lines:
                continue

            # Now it's safe to process chunk data
            chunk_files = glob(os.path.join(chunk_dir, f"*t{t}.bin"))
            total_empty_voxels = sum(load_chunk_volume(f) for f in chunk_files)

            # Check if Queen is still alive (first line)
            if queen_death_timestep is None and "Queen" not in lines[0]:
                queen_death_timestep = t

            ant_count = len(lines)
            positions = load_ant_positions(ant_file)
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

        except Exception as e:
            print(f"Skipped timestep {t} due to error: {e}")
            continue


    return pd.DataFrame(results), queen_death_timestep

def plot_nest_volume(df, sim_path):
    plt.figure()
    plt.plot(df["timestep"], df["nest_volume"])
    plt.xlabel("Timestep")
    plt.ylabel("Nest Volume (voxels)")
    plt.title("Nest Volume Over Time")
    plt.grid()
    plt.savefig(os.path.join(sim_path, "nest_volume.png"))
    plt.close()

def plot_pheromones(df, sim_path):
    pher_cols = [col for col in df.columns if col.startswith("pheromone_")]
    if not pher_cols:
        return
    plt.figure()
    for col in pher_cols:
        plt.plot(df["timestep"], df[col], label=col)
    plt.xlabel("Timestep")
    plt.ylabel("Total Pheromone")
    plt.title("Pheromone Totals Over Time")
    plt.legend()
    plt.grid()
    plt.savefig(os.path.join(sim_path, "pheromones.png"))
    plt.close()

def plot_ant_count(df, sim_path, queen_death_timestep=None):
    plt.figure()
    plt.plot(df["timestep"], df["ant_count"])
    plt.xlabel("Timestep")
    plt.ylabel("Alive Ants")
    plt.title("Ant Population Over Time")
    plt.grid()
    if queen_death_timestep is not None and queen_death_timestep in df["timestep"].values:
        plt.axvline(queen_death_timestep, color='red', linestyle='--', label='Queen Died')
        plt.legend()
    plt.savefig(os.path.join(sim_path, "ant_count.png"))
    plt.close()

def plot_ant_center_of_mass(df, sim_path):
    plt.figure()
    plt.plot(df["timestep"], df["ant_y"], label="Y (depth)")
    plt.plot(df["timestep"], df["ant_x"], label="X")
    plt.plot(df["timestep"], df["ant_z"], label="Z")
    plt.xlabel("Timestep")
    plt.ylabel("Average Ant Position")
    plt.title("Ant Center of Mass Over Time")
    plt.legend()
    plt.grid()
    plt.savefig(os.path.join(sim_path, "ant_center_of_mass.png"))
    plt.close()

def plot_chamber_summary(sim_path):
    chamber_dir = os.path.join(sim_path, "chambers")
    summary_files = sorted(glob(os.path.join(chamber_dir, "chamber_summary_t*.csv")))
    if not summary_files:
        print(f"No chamber summary found at {chamber_dir}")
        return
    summary_file = summary_files[-1]

    df = pd.read_csv(summary_file)
    df = df.dropna()
    labels = df["Zone"].astype(str)
    sizes = df["Count"].astype(float)

    plt.figure()
    plt.pie(sizes, labels=labels, autopct='%1.1f%%', startangle=140)
    plt.axis("equal")
    plt.title("Chamber Zone Ratios (Dug Area Only)")
    plt.savefig(os.path.join(sim_path, "chamber_pie.png"))
    plt.close()

def plot_digging_rate_per_ant(df, sim_path):
    delta_volume = df["nest_volume"].diff()
    avg_ants = df["ant_count"].rolling(window=2).mean()
    digging_rate = delta_volume / avg_ants.replace(0, np.nan)

    plt.figure()
    plt.plot(df["timestep"], digging_rate)
    plt.xlabel("Timestep")
    plt.ylabel("Digging Rate per Ant (Volume/Ant)")
    plt.title("Digging Rate per Ant Over Time")
    plt.grid()
    plt.savefig(os.path.join(sim_path, "digging_rate_per_ant.png"))
    plt.close()

def main():
    all_simulations = sorted(glob(os.path.join(RESULTS_DIR, "Simulation_*")))

    for sim_path in all_simulations:
        print(f"\nAnalyzing: {sim_path}")
        df, queen_death = analyze_simulation(sim_path)
        if df.empty:
            print("No data found.")
            continue

        df.to_csv(os.path.join(sim_path, "summary.csv"), index=False)

        plot_nest_volume(df, sim_path)
        #plot_pheromones(df, sim_path)
        plot_digging_rate_per_ant(df, sim_path)
        plot_ant_count(df, sim_path, queen_death)
        plot_ant_center_of_mass(df, sim_path)
        plot_chamber_summary(sim_path)

        print(f"Done. Results saved in: {sim_path}")

if __name__ == "__main__":
    main()
