using UnityEngine;

public class AntSpawner : MonoBehaviour
{
    public GameObject antPrefab;
    public int numberOfAnts = 10;

    void Start()
    {
        // Try to locate the ChunkManager
        ChunkManager chunkManager = FindFirstObjectByType<ChunkManager>();
        if (chunkManager == null)
        {
            Debug.LogError("ChunkManager not found. Cannot determine center world position.");
            return;
        }

        // For example, assume the world center is at the ChunkManager's position.
        // Adjust this if your world center is determined differently.
        Vector3 center = chunkManager.transform.position;

        for (int i = 0; i < numberOfAnts; i++)
        {
            Vector3 spawnPos = center + Random.insideUnitSphere * 2f;
            spawnPos.y = center.y; // ensure spawning on the same horizontal level as the center
            Instantiate(antPrefab, spawnPos, Quaternion.identity);
        }
    }
}
