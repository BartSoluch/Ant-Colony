using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MarchingCubesGPUProject;

public class AntSpawner : MonoBehaviour
{
    public GameObject antPrefab;
    public int numberOfAnts = 10;
    public static List<AntAgent> AllAnts { get; private set; } = new();

    void Start()
    {
        StartCoroutine(WaitAndSpawn());
    }
    IEnumerator WaitAndSpawn()
    {
        ChunkManager chunkManager = FindFirstObjectByType<ChunkManager>();
        if (chunkManager == null)
        {
            Debug.LogError("ChunkManager not found. Cannot determine world center.");
            yield break;
        }

        yield return new WaitForSeconds(0.5f); // Wait for marching cubes to generate

        int chunkSize = ChunkManager.chunkSize;
        Vector3 worldDimensions = new Vector3(
            chunkManager.worldSizeX * chunkSize,
            chunkManager.worldSizeY * chunkSize,
            chunkManager.worldSizeZ * chunkSize
        );

        float topY = chunkManager.worldSizeY * chunkSize;

        Vector3 centerXZ = new Vector3(
            worldDimensions.x / 2f,
            0f,
            worldDimensions.z / 2f
        );

        // === Spawn Queen ===
        Vector3 queenSpawnPos = new Vector3(
            centerXZ.x,
            topY,
            centerXZ.z
        );

        if (FindSurfaceBelow(ref queenSpawnPos))
        {
            GameObject queen = Instantiate(antPrefab, queenSpawnPos, Quaternion.identity);
            AntAgent queenAgent = queen.GetComponent<AntAgent>();
            queenAgent.currentRole = AntAgent.Role.Queen;
            GameManager.Instance.RegisterAnt(queenAgent);
            AllAnts.Add(queenAgent); //  Track queen
            Debug.Log("Spawned Queen at " + queenSpawnPos);
        }
        else
        {
            Debug.LogError("Could not place queen — aborting spawn.");
            yield break;
        }

        //Spawn Worker/Scout Ants
        for (int i = 0; i < numberOfAnts; i++)
        {
            Vector2 randomCircle = Random.insideUnitCircle * 10f;
            Vector3 spawnPos = new Vector3(
                centerXZ.x + randomCircle.x,
                topY,
                centerXZ.z + randomCircle.y
            );

            if (FindSurfaceBelow(ref spawnPos))
            {
                GameObject ant = Instantiate(antPrefab, spawnPos, Quaternion.identity);
                AntAgent agent = ant.GetComponent<AntAgent>();
                agent.currentRole = Random.value < GameManager.Instance.scoutRatio
                    ? AntAgent.Role.Scout
                    : AntAgent.Role.Worker;

                GameManager.Instance.RegisterAnt(agent);
                AllAnts.Add(agent);
                Debug.Log($"Spawned {agent.currentRole} Ant {i} at {spawnPos}");
            }
            else
            {
                Debug.LogWarning($"Could not find ground for Ant {i}, skipping.");
            }
        }
    }

    bool FindSurfaceBelow(ref Vector3 spawnPos)
    {
        ChunkManager chunkManager = FindFirstObjectByType<ChunkManager>();
        if (chunkManager == null)
            return false;

        float step = 0.5f;
        float maxDistance = 100f;
        Vector3 checkPos = spawnPos;
        bool wasAir = true; // Assume starting in air

        for (float y = 0; y < maxDistance; y += step)
        {
            checkPos.y -= step;
            MarchingCubesGPU chunk = chunkManager.GetChunkAtWorldPosition(checkPos);
            if (chunk == null)
                continue;

            float density = chunk.SampleDensityAtWorldPosition(checkPos);

            if (wasAir && density > 0f)
            {
                // Found air -> ground transition!
                spawnPos = checkPos + Vector3.up * 1.5f; // Place just above ground
                return true;
            }

            wasAir = density < 0f;
        }

        return false;
    }
}
