using System.Collections;
using UnityEngine;
using System.IO;
using MarchingCubesGPUProject;

public class DensitySnapshotManager : MonoBehaviour
{
    public float snapshotIntervalSeconds = 10f;
    private int snapshotId = 0;
    private ChunkManager chunkManager;
    private bool hasLoggedChamberStats = false;
    private SimulationSnapshotExporter exporter;

    void Start()
    {
        exporter = FindFirstObjectByType<SimulationSnapshotExporter>();
        chunkManager = FindFirstObjectByType<ChunkManager>();
        StartCoroutine(DumpSnapshots());
    }

    IEnumerator DumpSnapshots()
    {
        while (true)
        {
            yield return new WaitForSeconds(snapshotIntervalSeconds);

            if (chunkManager != null)
            {
                string chunkPath = Path.Combine(exporter.GetSimulationFolder(), "chunks");
                Directory.CreateDirectory(chunkPath);
                foreach (var chunk in chunkManager.GetAllChunks())
                {
                    chunk.SaveDensityToFile(chunkPath, snapshotId);
                }
                Debug.Log($"[SnapshotManager] Dumping snapshot #{snapshotId}");
            }

            if (!hasLoggedChamberStats && GameManager.Instance.GetCurrentAntCount() == 0)
            {
                hasLoggedChamberStats = true;
                AnalyzeDugZones();
            }
        }
    }

    void AnalyzeDugZones()
    {
        Debug.Log("[SnapshotManager] All ants dead. Analyzing chamber ratios...");

        int fungus = 0, nursery = 0, waste = 0, none = 0;

        foreach (var chunk in chunkManager.GetAllChunks())
        {
            var buffer = chunk.GetNoiseBuffer();
            if (buffer == null) continue;

            int padded = MarchingCubesGPU.N + 1 + 2;
            int count = padded * padded * padded;
            float[] density = new float[count];
            buffer.GetData(density);

            Vector3Int chunkBase = chunk.ChunkCoord * MarchingCubesGPU.N;

            for (int i = 0; i < count; i++)
            {
                int x = i % padded;
                int y = (i / padded) % padded;
                int z = i / (padded * padded);

                Vector3Int worldPos = chunkBase + new Vector3Int(x - 1, y - 1, z - 1); // account for padding
                if (worldPos.x < 0 || worldPos.y < 0 || worldPos.z < 0) continue;

                if (density[i] < 0f) // voxel has been dug
                {
                    var zone = chunkManager.GetZoneAtVoxel(worldPos);
                    switch (zone)
                    {
                        case ChunkManager.ChamberType.FungusGarden: fungus++; break;
                        case ChunkManager.ChamberType.Nursery: nursery++; break;
                        case ChunkManager.ChamberType.WasteDump: waste++; break;
                        default: none++; break;
                    }
                }
            }
        }

        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string basePath = Path.GetFullPath(Path.Combine(projectRoot, "..", "Ant-Colony-Results"));
        string chamberPath = Path.Combine(exporter.GetSimulationFolder(), "chambers");
        Directory.CreateDirectory(chamberPath);
        string outputPath = Path.Combine(chamberPath, $"chamber_summary_t{snapshotId}.csv");

        using StreamWriter writer = new StreamWriter(outputPath);
        writer.WriteLine("Zone,Count");
        writer.WriteLine($"FungusGarden,{fungus}");
        writer.WriteLine($"Nursery,{nursery}");
        writer.WriteLine($"WasteDump,{waste}");
        writer.WriteLine($"None,{none}");

        Debug.Log($"[SnapshotManager] Wrote chamber summary to {outputPath}");
    }
}
