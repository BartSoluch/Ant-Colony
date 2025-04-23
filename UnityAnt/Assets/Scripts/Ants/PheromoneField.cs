using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class PheromoneField : MonoBehaviour
{
    public static PheromoneField Instance { get; private set; }

    private float[,,] digPheromones;
    private float[,,] trailPheromones;
    private Vector3Int worldSize;
    private bool visualsEnabled = true;

    // REPLACED: we no longer need a 'gridOffset' int—use a true worldOrigin instead
    private Vector3 worldOrigin;

    public float digDecayRate = 0.01f;
    public float trailDecayRate = 0.05f;

    [Header("Visuals")]
    public GameObject pheromoneVisualPrefab;
    public Material transparentMaterial;
    public Material normalMaterial;
    private Dictionary<Vector3Int, GameObject> pheromoneVisuals = new();
    private float visualThreshold = 0.05f;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    IEnumerator InitAfterVoxelWorld()
    {
        // wait for VoxelWorld to initialize
        while (VoxelWorld.Instance == null)
            yield return null;

        // grab its size
        worldSize = VoxelWorld.Instance.WorldSize;

        digPheromones = new float[worldSize.x, worldSize.y, worldSize.z];
        trailPheromones = new float[worldSize.x, worldSize.y, worldSize.z];

        // compute the world-space minimum corner of your grid
        Vector3 center = VoxelWorld.Instance.GetCenterWorldPosition();
        worldOrigin = center - new Vector3(worldSize.x, worldSize.y, worldSize.z) * 0.5f;
    }

    void Start()
    {
        StartCoroutine(InitAfterVoxelWorld());
    }

    void Update()
    {
        if (digPheromones == null) return;

        float dDecay = digDecayRate * Time.deltaTime;
        float tDecay = trailDecayRate * Time.deltaTime;

        for (int x = 0; x < worldSize.x; x++)
            for (int y = 0; y < worldSize.y; y++)
                for (int z = 0; z < worldSize.z; z++)
                {
                    digPheromones[x, y, z] = Mathf.Max(0f, digPheromones[x, y, z] - dDecay);
                    trailPheromones[x, y, z] = Mathf.Max(0f, trailPheromones[x, y, z] - tDecay);
                }
    }

    void LateUpdate()
    {
        if (visualsEnabled)
            UpdateVisuals();
    }

    public void SetVisualsEnabled(bool enabled)
    {
        Debug.Log("[PheromoneField] SetVisualsEnabled: " + enabled);
        visualsEnabled = enabled;
        foreach (var go in pheromoneVisuals.Values)
        {
            if (go == null) continue;
            var mr = go.GetComponent<MeshRenderer>();
            if (mr == null) continue;
            mr.material = enabled
                ? normalMaterial
                : transparentMaterial;
        }
    }

    void UpdateVisuals()
    {
        if (pheromoneVisualPrefab == null || digPheromones == null) return;

        for (int x = 0; x < worldSize.x; x += 1)
            for (int y = 0; y < worldSize.y; y += 1)
                for (int z = 0; z < worldSize.z; z += 1)
                {
                    float value = Mathf.Max(digPheromones[x, y, z], trailPheromones[x, y, z]);
                    Vector3Int cell = new Vector3Int(x, y, z);

                    if (value > visualThreshold)
                    {
                        if (!pheromoneVisuals.ContainsKey(cell))
                        {
                            // convert grid cell → world‐pos
                            Vector3 worldPos = worldOrigin + (Vector3)cell + Vector3.one * 0.5f;
                            var go = Instantiate(
                                pheromoneVisualPrefab,
                                worldPos,
                                Quaternion.identity,
                                transform
                            );
                            var mr = go.GetComponent<MeshRenderer>();
                            if (mr != null)
                                mr.material = visualsEnabled
                                    ? normalMaterial
                                    : transparentMaterial;
                            pheromoneVisuals[cell] = go;
                        }

                        var cube = pheromoneVisuals[cell];
                        Color c = Color.Lerp(Color.blue, Color.red, value);
                        c.a = Mathf.Clamp01(value / 10f);
                        cube.GetComponent<MeshRenderer>().material.color = c;
                    }
                    else if (pheromoneVisuals.TryGetValue(cell, out var old))
                    {
                        Destroy(old);
                        pheromoneVisuals.Remove(cell);
                    }
                }
    }

    // Public API unchanged
    public void DepositDig(Vector3 worldPos, float amt) => InternalDeposit(digPheromones, worldPos, amt);
    public void DepositTrail(Vector3 worldPos, float amt) => InternalDeposit(trailPheromones, worldPos, amt);

    public float GetDig(Vector3 worldPos) => InternalGet(digPheromones, worldPos);
    public float GetTrail(Vector3 worldPos) => InternalGet(trailPheromones, worldPos);

    // —— Helpers —— 

    private void InternalDeposit(float[,,] grid, Vector3 worldPos, float amount)
    {
        // map world‐space → local grid cell
        Vector3 local = worldPos - worldOrigin;
        Vector3Int cell = Vector3Int.FloorToInt(local);
        if (cell.x >= 0 && cell.x < worldSize.x
         && cell.y >= 0 && cell.y < worldSize.y
         && cell.z >= 0 && cell.z < worldSize.z)
        {
            grid[cell.x, cell.y, cell.z] += amount;
        }
    }

    private float InternalGet(float[,,] grid, Vector3 worldPos)
    {
        Vector3 local = worldPos - worldOrigin;
        Vector3Int cell = Vector3Int.FloorToInt(local);
        if (cell.x < 0 || cell.x >= worldSize.x
         || cell.y < 0 || cell.y >= worldSize.y
         || cell.z < 0 || cell.z >= worldSize.z)
            return 0f;
        return grid[cell.x, cell.y, cell.z];
    }
}
