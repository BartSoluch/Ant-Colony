using UnityEngine;

public class AntSpawner : MonoBehaviour
{
    [Header("Ant Prefabs (must have DiggerAnt or ScoutAnt on them)")]
    public GameObject diggerPrefab;
    public GameObject scoutPrefab;

    [Header("How many of each to spawn")]
    public int numDiggers = 5;
    public int numScouts = 5;

    [Header("Spawn Radius Around Center")]
    public float spawnRadius = 2f;

    void Start()
    {
        Vector3 center = VoxelWorld.Instance.GetCenterWorldPosition();

        // Spawn diggers
        SpawnAnts(diggerPrefab, numDiggers, center);

        // Spawn scouts
        SpawnAnts(scoutPrefab, numScouts, center);
    }

    void SpawnAnts(GameObject prefab, int count, Vector3 center)
    {
        if (prefab == null)
        {
            Debug.LogError("[AntSpawner] Prefab is null!");
            return;
        }

        for (int i = 0; i < count; i++)
        {
            // random horizontal disc around center
            Vector3 pos = center + Random.insideUnitSphere * spawnRadius;
            pos.y = center.y;

            GameObject go = Instantiate(prefab, pos, Quaternion.identity);
            go.name = prefab.name + "-" + i;

            AntAgent agent = go.GetComponent<AntAgent>();
            if (agent == null)
            {
                Debug.LogWarning("[AntSpawner] " + go.name + " has no AntAgent!");
            }
        }
    }
}
