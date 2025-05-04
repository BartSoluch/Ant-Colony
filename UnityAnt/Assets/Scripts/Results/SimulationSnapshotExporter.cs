using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;
using MarchingCubesGPUProject;
using System.Collections;

public class SimulationSnapshotExporter : MonoBehaviour
{
    public float snapshotInterval = 10f; // Time between snapshots
    private float nextSnapshotTime = 0f;
    private int snapshotId = 0;

    private ConcurrentQueue<SnapshotJob> snapshotQueue = new();
    private Thread workerThread;
    private bool isRunning = true;

    private string simulationRootPath;
    private string chunkPath;
    private string antPath;
    private string pheromonePath;
    void Start()
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string basePath = Path.GetFullPath(Path.Combine(projectRoot, "..", "Ant-Colony-Results"));

        string timestamp = System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        simulationRootPath = Path.Combine(basePath, $"Simulation_{timestamp}");

        chunkPath = Path.Combine(simulationRootPath, "chunks");
        antPath = Path.Combine(simulationRootPath, "ants");
        pheromonePath = Path.Combine(simulationRootPath, "pheromones");

        Directory.CreateDirectory(chunkPath);
        Directory.CreateDirectory(antPath);
        Directory.CreateDirectory(pheromonePath);

        workerThread = new Thread(ProcessSnapshots);
        workerThread.IsBackground = true;
        workerThread.Start();

        StartCoroutine(WaitUntilReadyAndCaptureInitial());
    }
    void Update()
    {
        if (Time.time >= nextSnapshotTime)
        {
            QueueSnapshot(snapshotId++);
            nextSnapshotTime = Time.time + snapshotInterval;
        }
    }

    void OnDestroy()
    {
        isRunning = false;
        workerThread?.Join();
    }

    void QueueSnapshot(int id)
    {
        // === 1. Get Ant Data ===
        var antLines = new List<string>();
        foreach (var ant in AntSpawner.AllAnts)
        {
            antLines.Add(ant.SerializeState());
        }

        // === 2. Get Chunk Density Data ===
        var chunkDataList = new List<ChunkSnapshot>();
        foreach (var chunk in GameObject.FindFirstObjectByType<ChunkManager>().GetAllChunks())
        {
            var buffer = chunk.GetNoiseBuffer();
            if (buffer == null) continue;

            int padded = MarchingCubesGPU.N + 1 + 2;
            int count = padded * padded * padded;
            float[] density = new float[count];
            buffer.GetData(density);

            var coords = chunk.ChunkCoord;
            chunkDataList.Add(new ChunkSnapshot
            {
                coords = coords,
                paddedWidth = padded,
                densities = density
            });
        }

        snapshotQueue.Enqueue(new SnapshotJob
        {
            id = id,
            timestamp = Time.time,
            antLines = antLines,
            chunkSnapshots = chunkDataList
        });

        Debug.Log($"[SnapshotExporter] Queued snapshot {id} at t={Time.time:F1}");
    }

    void ProcessSnapshots()
    {
        while (isRunning)
        {
            if (snapshotQueue.TryDequeue(out SnapshotJob job))
            {
                try
                {
                    SaveSnapshot(job);
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[SnapshotExporter] Failed to save snapshot {job.id}: {ex}");
                }
            }
            else
            {
                Thread.Sleep(10); // Wait briefly before polling again
            }
        }
    }
    void SaveSnapshot(SnapshotJob job)
    {
        // === Save Ant Data ===
        string antsFile = Path.Combine(antPath, $"ants_t{job.id}.csv");
        File.WriteAllLines(antsFile, job.antLines);

        // === Save Chunk Data ===
        foreach (var chunk in job.chunkSnapshots)
        {
            string chunkFile = Path.Combine(chunkPath, $"chunk_{chunk.coords.x}_{chunk.coords.y}_{chunk.coords.z}_t{job.id}.bin");
            using var fs = new FileStream(chunkFile, FileMode.Create);
            using var writer = new BinaryWriter(fs);

            writer.Write(chunk.paddedWidth);
            writer.Write(chunk.densities.Length);
            for (int i = 0; i < chunk.densities.Length; i++)
                writer.Write(chunk.densities[i]);
        }

        // === Save Pheromone Grids ===
        if (PheromoneField.Instance != null)
        {
            PheromoneField.Instance.SaveAllPheromones(pheromonePath, job.id);
        }

        Debug.Log($"[SnapshotExporter] ✔ Saved snapshot {job.id} in {simulationRootPath}");
    }

    // === Structs ===
    class SnapshotJob
    {
        public int id;
        public float timestamp;
        public List<string> antLines;
        public List<ChunkSnapshot> chunkSnapshots;
    }

    class ChunkSnapshot
    {
        public Vector3Int coords;
        public int paddedWidth;
        public float[] densities;
    }
    private IEnumerator WaitUntilReadyAndCaptureInitial()
    {
        // Wait until the AntSpawner has spawned the queen and workers
        while (AntSpawner.AllAnts == null || AntSpawner.AllAnts.Count == 0)
            yield return null;

        // Optional: wait another short moment for ComputeBuffers/fields to finish initializing
        yield return new WaitForSeconds(0.5f);

        // Take snapshot 0 after full system is ready
        QueueSnapshot(snapshotId++);

        // Start regular snapshot interval AFTER snapshot 0
        nextSnapshotTime = Time.time + snapshotInterval;
    }

}
