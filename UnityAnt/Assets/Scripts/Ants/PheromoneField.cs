using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class PheromoneField : MonoBehaviour
{
    public static PheromoneField Instance { get; private set; }

    public ChunkManager chunkManager;

    private float[,,] digPheromones;
    private float[,,] trailPheromones;
    private float[,,] nestPheromones;
    private float[,,] chamberPheromones;

    private Vector3Int worldSize = new Vector3Int(192, 192, 192);
    private Vector3 worldOrigin;

    private bool isInitialized = false;

    private List<Vector3Int> activeDigCells = new();
    private List<Vector3Int> activeTrailCells = new();
    private List<Vector3Int> activeNestCells = new();
    private List<Vector3Int> activeChamberCells = new();

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
        if (chunkManager == null)
            chunkManager = FindObjectOfType<ChunkManager>();

        // compute your grid dimensions—e.g. total voxels across all chunks
        int totalX = chunkManager.worldSizeX * ChunkManager.chunkSize;
        int totalY = chunkManager.worldSizeY * ChunkManager.chunkSize;
        int totalZ = chunkManager.worldSizeZ * ChunkManager.chunkSize;

        worldSize = new Vector3Int(totalX, totalY, totalZ);
        worldOrigin = chunkManager.transform.position;  // usually (0,0,0)

        // allocate all four grids
        digPheromones = new float[totalX, totalY, totalZ];
        trailPheromones = new float[totalX, totalY, totalZ];
        nestPheromones = new float[totalX, totalY, totalZ];
        chamberPheromones = new float[totalX, totalY, totalZ];

        isInitialized = true;
    }

    IEnumerator InitAfterVoxelWorld()
    {
        while (VoxelWorld.Instance == null)
            yield return null;

        worldSize = VoxelWorld.Instance.WorldSize;

        if (worldSize == Vector3Int.zero)
        {
            worldSize = new Vector3Int(192, 192, 192);
        }

        digPheromones = new float[worldSize.x, worldSize.y, worldSize.z];
        trailPheromones = new float[worldSize.x, worldSize.y, worldSize.z];
        nestPheromones = new float[worldSize.x, worldSize.y, worldSize.z];
    }

    void Update()
    {
        if (!isInitialized) return;

        float digDecay = decayRate * Time.deltaTime;
        float trailDecay = trailDecayRate * Time.deltaTime;
        float nestDecay = decayRate * 0.5f * Time.deltaTime;
        float chamberDecay = decayRate * 0.5f * Time.deltaTime;

        // 2) Decay only active dig cells:
        for (int i = activeDigCells.Count - 1; i >= 0; i--)
        {
            var pos = activeDigCells[i];
            ref float v = ref digPheromones[pos.x, pos.y, pos.z];
            v = Mathf.Max(0f, v - digDecay);
            if (v == 0f) activeDigCells.RemoveAt(i);
        }

        // 3) Decay only active trail cells:
        for (int i = activeTrailCells.Count - 1; i >= 0; i--)
        {
            var pos = activeTrailCells[i];
            ref float v = ref trailPheromones[pos.x, pos.y, pos.z];
            v = Mathf.Max(0f, v - trailDecay);
            if (v == 0f) activeTrailCells.RemoveAt(i);
        }

        // 4) Decay only active nest cells:
        for (int i = activeNestCells.Count - 1; i >= 0; i--)
        {
            var pos = activeNestCells[i];
            ref float v = ref nestPheromones[pos.x, pos.y, pos.z];
            v = Mathf.Max(0f, v - nestDecay);
            if (v == 0f) activeNestCells.RemoveAt(i);
        }

        // 5) Decay only active chamber cells:
        for (int i = activeChamberCells.Count - 1; i >= 0; i--)
        {
            var pos = activeChamberCells[i];
            ref float v = ref chamberPheromones[pos.x, pos.y, pos.z];
            v = Mathf.Max(0f, v - chamberDecay);
            if (v == 0f) activeChamberCells.RemoveAt(i);
        }
    }

    void LateUpdate()
    {
        //UpdateVisuals();
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
    public float GetDig(Vector3Int pos) => InternalGet(digPheromones, pos);
    public float GetTrail(Vector3Int pos) => InternalGet(trailPheromones, pos);
    public float GetNest(Vector3Int pos) => InternalGet(nestPheromones, pos);
    public float GetChamber(Vector3Int pos) => InternalGet(chamberPheromones, pos);


    // Helpers
    public void DepositDig(Vector3 worldPos, float amount)
    {
        if (!isInitialized) return;
        Vector3Int pos = Vector3Int.FloorToInt(worldPos - worldOrigin);
        if (!IsInsideBounds(pos)) return;

        if (digPheromones[pos.x, pos.y, pos.z] == 0f)
            activeDigCells.Add(pos);

        digPheromones[pos.x, pos.y, pos.z] += amount;
    }

    public void DepositTrail(Vector3 worldPos, float amount)
    {
        if (!isInitialized) return;
        Vector3Int pos = Vector3Int.FloorToInt(worldPos - worldOrigin);
        if (!IsInsideBounds(pos)) return;

        if (trailPheromones[pos.x, pos.y, pos.z] == 0f)
            activeTrailCells.Add(pos);

        trailPheromones[pos.x, pos.y, pos.z] += amount;
    }

    public void DepositNest(Vector3 worldPos, float amount)
    {
        if (!isInitialized) return;
        Vector3Int pos = Vector3Int.FloorToInt(worldPos - worldOrigin);
        if (!IsInsideBounds(pos)) return;

        if (nestPheromones[pos.x, pos.y, pos.z] == 0f)
            activeNestCells.Add(pos);

        nestPheromones[pos.x, pos.y, pos.z] += amount;
    }

    public void DepositChamber(Vector3 worldPos, float amount)
    {
        if (!isInitialized) return;
        Vector3Int pos = Vector3Int.FloorToInt(worldPos - worldOrigin);
        if (!IsInsideBounds(pos)) return;

        if (chamberPheromones[pos.x, pos.y, pos.z] == 0f)
            activeChamberCells.Add(pos);

        chamberPheromones[pos.x, pos.y, pos.z] += amount;
    }

    private void InternalDeposit(float[,,] grid, Vector3 worldPos, float amount)
    {
        if (!isInitialized) return;
        Vector3 local = worldPos - worldOrigin;
        Vector3Int pos = Vector3Int.FloorToInt(local);
        if (IsInsideBounds(pos))
            grid[pos.x, pos.y, pos.z] += amount;
    }

    private float InternalGet(float[,,] grid, Vector3Int pos)
    {
        if (!isInitialized || !IsInsideBounds(pos)) return 0f;
        return grid[pos.x, pos.y, pos.z];
    }

    private bool IsInsideBounds(Vector3Int pos)
    {
        return pos.x >= 0 && pos.x < worldSize.x &&
               pos.y >= 0 && pos.y < worldSize.y &&
               pos.z >= 0 && pos.z < worldSize.z;
    }

}
