using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class PheromoneField : MonoBehaviour
{
    public static PheromoneField Instance { get; private set; }

    private float[,,] digPheromones;
    private float[,,] trailPheromones;
    private Vector3Int worldSize;

    public float decayRate = 0.1f;
    public float trailDecayRate = 0.05f;

    [Header("Visuals")]
    public GameObject pheromoneVisualPrefab;  // Assign in Inspector
    private Dictionary<Vector3Int, GameObject> pheromoneVisuals = new();
    private float visualThreshold = 0.1f;

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
        trailPheromones = new float[worldSize.x, worldSize.y, worldSize.z];
    }

    void Update()
    {
        if (digPheromones == null) return;

        float digDecay = decayRate * Time.deltaTime;
        float trailDecay = trailDecayRate * Time.deltaTime;

        for (int x = 0; x < worldSize.x; x++)
            for (int y = 0; y < worldSize.y; y++)
                for (int z = 0; z < worldSize.z; z++)
                {
                    digPheromones[x, y, z] = Mathf.Max(0, digPheromones[x, y, z] - digDecay);
                    trailPheromones[x, y, z] = Mathf.Max(0, trailPheromones[x, y, z] - trailDecay);
                }
    }

    void LateUpdate()
    {
        UpdateVisuals();
    }

    void UpdateVisuals()
    {
        if (pheromoneVisualPrefab == null || digPheromones == null) return;

        for (int x = 0; x < worldSize.x; x += 2)
            for (int y = 0; y < worldSize.y; y += 2)
                for (int z = 0; z < worldSize.z; z += 2)
                {
                    Vector3Int pos = new Vector3Int(x, y, z);
                    float value = Mathf.Max(digPheromones[x, y, z], trailPheromones[x, y, z]);

                    if (value > visualThreshold)
                    {
                        if (!pheromoneVisuals.ContainsKey(pos))
                        {
                            GameObject go = Instantiate(pheromoneVisualPrefab, pos + Vector3.one * 0.5f, Quaternion.identity, transform);
                            pheromoneVisuals[pos] = go;
                        }

                        GameObject cube = pheromoneVisuals[pos];
                        var color = cube.GetComponent<MeshRenderer>().material.color;
                        color = Color.Lerp(Color.blue, Color.red, value); // red = strong
                        color.a = Mathf.Clamp01(value / 10f);
                        cube.GetComponent<MeshRenderer>().material.color = color;
                    }
                    else if (pheromoneVisuals.TryGetValue(pos, out GameObject go))
                    {
                        Destroy(go);
                        pheromoneVisuals.Remove(pos);
                    }
                }
    }

    // Public API
    public void DepositDig(Vector3 worldPos, float amount) => InternalDeposit(digPheromones, worldPos, amount);
    public void DepositTrail(Vector3 worldPos, float amount) => InternalDeposit(trailPheromones, worldPos, amount);

    public float GetDig(Vector3Int pos) => InternalGet(digPheromones, pos);
    public float GetTrail(Vector3Int pos) => InternalGet(trailPheromones, pos);

    // Helpers
    private void InternalDeposit(float[,,] grid, Vector3 worldPos, float amount)
    {
        Vector3Int pos = Vector3Int.FloorToInt(worldPos);
        if (IsInsideBounds(pos))
            grid[pos.x, pos.y, pos.z] += amount;
    }

    private float InternalGet(float[,,] grid, Vector3Int pos)
    {
        if (!IsInsideBounds(pos)) return 0f;
        return grid[pos.x, pos.y, pos.z];
    }

    private bool IsInsideBounds(Vector3Int pos)
    {
        return pos.x >= 0 && pos.x < worldSize.x &&
               pos.y >= 0 && pos.y < worldSize.y &&
               pos.z >= 0 && pos.z < worldSize.z;
    }
}
