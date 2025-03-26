using UnityEngine;
using System.Collections;

public class PheromoneField : MonoBehaviour
{
    public static PheromoneField Instance { get; private set; }

    private float[,,] digPheromones;
    private Vector3Int worldSize;

    [Header("Pheromone Settings")]
    public float decayRate = 0.1f;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    void Start()
    {
        StartCoroutine(InitAfterVoxelWorld());
    }

    IEnumerator InitAfterVoxelWorld()
    {
        while (VoxelWorld.Instance == null)
            yield return null;

        worldSize = VoxelWorld.Instance.WorldSize;
        digPheromones = new float[worldSize.x, worldSize.y, worldSize.z];
        Debug.Log("Pheromone grid size: " + worldSize);

        // ✅ Safe test: seed at a guaranteed valid coordinate
        Vector3Int seedPos = new Vector3Int(worldSize.x / 2, 1, worldSize.z / 2);
        Deposit(seedPos, 100f);
        Debug.Log($"Seeded pheromone at {seedPos}: " + Get(seedPos));
    }

    public void Deposit(Vector3 worldPos, float amount)
    {
        Vector3Int pos = Vector3Int.FloorToInt(worldPos);
        Deposit(pos, amount);
    }

    public void Deposit(Vector3Int pos, float amount)
    {
        if (IsInsideBounds(pos))
        {
            digPheromones[pos.x, pos.y, pos.z] += amount;
        }
    }

    public float Get(Vector3Int pos)
    {
        if (!IsInsideBounds(pos)) return 0f;
        return digPheromones[pos.x, pos.y, pos.z];
    }

    void Update()
    {
        if (digPheromones == null) return;

        float decay = decayRate * Time.deltaTime;
        for (int x = 0; x < worldSize.x; x++)
            for (int y = 0; y < worldSize.y; y++)
                for (int z = 0; z < worldSize.z; z++)
                {
                    digPheromones[x, y, z] = Mathf.Max(0, digPheromones[x, y, z] - decay);
                }
    }

    private bool IsInsideBounds(Vector3Int pos)
    {
        return pos.x >= 0 && pos.x < worldSize.x &&
               pos.y >= 0 && pos.y < worldSize.y &&
               pos.z >= 0 && pos.z < worldSize.z;
    }
}
