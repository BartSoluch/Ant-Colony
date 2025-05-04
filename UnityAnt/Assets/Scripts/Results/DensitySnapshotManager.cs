using System.Collections;
using UnityEngine;

public class DensitySnapshotManager : MonoBehaviour
{
    public float snapshotIntervalSeconds = 10f;
    private int snapshotId = 0;
    private ChunkManager chunkManager;

    void Start()
    {
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
                chunkManager.SaveAllChunkDensities(snapshotId++);
                Debug.Log($"[SnapshotManager] Dumping snapshot #{snapshotId}");
            }
        }
    }
}
