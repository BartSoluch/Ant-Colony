using UnityEngine;

public class AntSpawner : MonoBehaviour
{
    [Header("Shared Ant Prefab")]
    public GameObject antPrefab;

    [Header("How many of each role?")]
    public int numDiggers = 5;
    public int numScouts = 5;

    [Header("Spawn Radius Around Center")]
    public float spawnRadius = 2f;

    void Start()
    {
        if (antPrefab == null)
        {
            Debug.LogError("[AntSpawner] No antPrefab assigned!");
            return;
        }

        Vector3 center = VoxelWorld.Instance.GetCenterWorldPosition();

        // Spawn all diggers
        SpawnAnts(numDiggers, AntAgent.Role.Digger, center);

        // Spawn all scouts
        SpawnAnts(numScouts, AntAgent.Role.Scout, center);
    }

    void SpawnAnts(int count, AntAgent.Role role, Vector3 center)
    {
        for (int i = 0; i < count; i++)
        {
            // random position in a horizontal disc
            Vector3 spawnPos = center + Random.insideUnitSphere * spawnRadius;
            spawnPos.y = center.y;

            GameObject go = Instantiate(antPrefab, spawnPos, Quaternion.identity);
            go.name = $"{antPrefab.name}-{role}-{i}";

            AntAgent agent = go.GetComponent<AntAgent>();
            if (agent != null)
            {
                agent.role = role;
            }
            else
            {
                Debug.LogWarning($"Spawned object {go.name} is missing AntAgent!");
            }
        }
    }
}
