using MarchingCubesGPUProject;
using UnityEngine;

public class ChunkManager : MonoBehaviour
{
    public GameObject chunkPrefab;
    public int chunkSize = 64;
    public int worldSizeX = 2;
    public int worldSizeY = 1;
    public int worldSizeZ = 2;

    private MarchingCubesGPU[,,] chunks;

    void Start()
    {
        chunks = new MarchingCubesGPU[worldSizeX, worldSizeY, worldSizeZ];

        for (int x = 0; x < worldSizeX; x++)
        {
            for (int y = 0; y < worldSizeY; y++)
            {
                for (int z = 0; z < worldSizeZ; z++)
                {
                    Vector3 position = new Vector3(x, -y, z) * chunkSize;
                    GameObject chunk = Instantiate(chunkPrefab, position, Quaternion.identity, transform); // keep as is

                    MarchingCubesGPU mc = chunk.GetComponent<MarchingCubesGPU>();
                    chunks[x, y, z] = mc;
                    mc.ChunkCoord = new Vector3Int(x, y, z);
                    mc.ChunkWorldPosition = chunk.transform.position; // don't reuse `position` directly
                    Debug.Log($"[{mc.ChunkCoord}] chunk at world position: {transform.position}, chunkWorldPos: {mc.ChunkWorldPosition}");
                }
            }
        }
    }

    public MarchingCubesGPU GetChunkAtWorldPosition(Vector3 worldPos)
    {
        int x = Mathf.FloorToInt(worldPos.x / chunkSize);
        int y = Mathf.FloorToInt(worldPos.y / chunkSize);
        int z = Mathf.FloorToInt(worldPos.z / chunkSize);

        if (x < 0 || y < 0 || z < 0 || x >= worldSizeX || y >= worldSizeY || z >= worldSizeZ)
            return null;

        return chunks[x, y, z];
    }

    public void DigAtWorldPosition(Vector3 worldPos, float radius)
    {
        int minX = Mathf.FloorToInt((worldPos.x - radius) / chunkSize);
        int maxX = Mathf.FloorToInt((worldPos.x + radius) / chunkSize);
        int minY = Mathf.FloorToInt((worldPos.y - radius) / chunkSize);
        int maxY = Mathf.FloorToInt((worldPos.y + radius) / chunkSize);
        int minZ = Mathf.FloorToInt((worldPos.z - radius) / chunkSize);
        int maxZ = Mathf.FloorToInt((worldPos.z + radius) / chunkSize);

        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                for (int z = minZ; z <= maxZ; z++)
                {
                    if (x < 0 || y < 0 || z < 0 || x >= worldSizeX || y >= worldSizeY || z >= worldSizeZ)
                        continue;

                    var chunk = chunks[x, y, z];
                    Vector3 localPos = worldPos - chunk.ChunkWorldPosition;
                    chunk.DigAtLocal(localPos, radius);
                    RemeshBorderingChunks(x, y, z, localPos, radius);
                }
            }
        }
    }
    void RemeshBorderingChunks(int cx, int cy, int cz, Vector3 localPos, float radius)
    {
        // Check if this dig is near the border
        bool nearMinX = localPos.x - radius <= 1;
        bool nearMaxX = localPos.x + radius >= chunkSize - 1;
        bool nearMinY = localPos.y - radius <= 1;
        bool nearMaxY = localPos.y + radius >= chunkSize - 1;
        bool nearMinZ = localPos.z - radius <= 1;
        bool nearMaxZ = localPos.z + radius >= chunkSize - 1;

        void Remesh(int x, int y, int z)
        {
            if (x < 0 || y < 0 || z < 0 || x >= worldSizeX || y >= worldSizeY || z >= worldSizeZ)
                return;

            chunks[x, y, z].Remesh();
        }

        if (nearMinX) Remesh(cx - 1, cy, cz);
        if (nearMaxX) Remesh(cx + 1, cy, cz);
        if (nearMinY) Remesh(cx, cy - 1, cz);
        if (nearMaxY) Remesh(cx, cy + 1, cz);
        if (nearMinZ) Remesh(cx, cy, cz - 1);
        if (nearMaxZ) Remesh(cx, cy, cz + 1);
    }
}
