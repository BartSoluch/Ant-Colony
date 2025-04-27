using MarchingCubesGPUProject;
using UnityEngine;
using System.Collections.Generic;

public class ChunkManager : MonoBehaviour
{
    public GameObject chunkPrefab;
    public static int chunkSize = 64;  // Use as ChunkManager.chunkSize everywhere.
    public int worldSizeX = 2;
    public int worldSizeY = 1;
    public int worldSizeZ = 2;


    private MarchingCubesGPU[,,] chunks;
    // A dictionary to hold a normals RenderTexture for each chunk.
    private Dictionary<Vector3Int, RenderTexture> normalsPool = new Dictionary<Vector3Int, RenderTexture>();

    void Start()
    {
        chunks = new MarchingCubesGPU[worldSizeX, worldSizeY, worldSizeZ];

        // First: Spawn all chunks
        for (int x = 0; x < worldSizeX; x++)
        {
            for (int y = 0; y < worldSizeY; y++)
            {
                for (int z = 0; z < worldSizeZ; z++)
                {
                    Vector3 position = new Vector3(x, y, z) * chunkSize;
                    GameObject chunk = Instantiate(chunkPrefab, position, Quaternion.identity, transform);

                    MarchingCubesGPU mc = chunk.GetComponent<MarchingCubesGPU>();
                    chunks[x, y, z] = mc;
                    Vector3Int coord = new Vector3Int(x, y, z);
                    mc.ChunkCoord = coord;
                    mc.ChunkWorldPosition = chunk.transform.position;

                    RenderTexture normalsBuffer = CreateNormalsBuffer();
                    normalsPool[coord] = normalsBuffer;
                    mc.SetNormalsBuffer(normalsBuffer);
                    mc.SetChunkManager(this);

                    Debug.Log($"Chunk {coord} → worldPos = {chunk.transform.position}");
                }
            }
        }

        // After all chunks created
        for (int x = 0; x < worldSizeX; x++)
        {
            for (int y = 0; y < worldSizeY; y++)
            {
                for (int z = 0; z < worldSizeZ; z++)
                {
                    MarchingCubesGPU chunk = chunks[x, y, z];
                    if (chunk == null) continue;

                    // ONLY sync to positive neighbors to avoid double copying
                    if (x + 1 < worldSizeX) SyncSharedFace(chunk, chunks[x + 1, y, z], Vector3Int.right);
                    if (y + 1 < worldSizeY) SyncSharedFace(chunk, chunks[x, y + 1, z], Vector3Int.up);
                    if (z + 1 < worldSizeZ) SyncSharedFace(chunk, chunks[x, y, z + 1], Vector3Int.forward);
                }
            }
        }
    }

    // Helper method to create a normals RenderTexture for a single chunk.
    // Adjust resolution if needed; here we use N (64) as width/height and depth.
    RenderTexture CreateNormalsBuffer()
    {
        int rtWidth = MarchingCubesGPU.N; // Using the constant from MarchingCubesGPU.
        RenderTexture rt = new RenderTexture(rtWidth, rtWidth, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
        rt.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
        rt.enableRandomWrite = true;
        rt.useMipMap = false;
        rt.volumeDepth = rtWidth;
        rt.Create();
        return rt;
    }

    // Release all normals RenderTextures when the manager is destroyed.
    void OnDestroy()
    {
        foreach (var rt in normalsPool.Values)
        {
            if (rt != null)
                rt.Release();
        }
    }

    public MarchingCubesGPU GetChunkAtWorldPosition(Vector3 worldPos)
    {
        int x = Mathf.FloorToInt(worldPos.x / chunkSize);
        int y = Mathf.FloorToInt(worldPos.y / chunkSize);
        int z = Mathf.FloorToInt(worldPos.z / chunkSize);

        Debug.Log($"[ChunkManager] Lookup chunk at ({x},{y},{z})");

        if (x < 0 || y < 0 || z < 0 || x >= worldSizeX || y >= worldSizeY || z >= worldSizeZ)
            return null;

        return chunks[x, y, z];
    }


    public MarchingCubesGPU GetChunk(int x, int y, int z)
    {

        if (x < 0 || y < 0 || z < 0 || x >= worldSizeX || y >= worldSizeY || z >= worldSizeZ)
            return null;
        return chunks[x, y, z];
    }

    public void DigAtWorldPosition(Vector3 worldPos, float radius)
    {
        int cs = chunkSize;
        // Compute the axis‑aligned bounding box (AABB) of the affected area.
        Vector3 minWorld = worldPos - Vector3.one * radius;
        Vector3 maxWorld = worldPos + Vector3.one * radius;

        int minX = Mathf.FloorToInt(minWorld.x / cs);
        int maxX = Mathf.FloorToInt(maxWorld.x / cs);
        int minY = Mathf.FloorToInt(minWorld.y / cs);
        int maxY = Mathf.FloorToInt(maxWorld.y / cs);
        int minZ = Mathf.FloorToInt(minWorld.z / cs);
        int maxZ = Mathf.FloorToInt(maxWorld.z / cs);

        for (int x = minX; x <= maxX; x++)
        {
            for (int y = Mathf.Max(minY, 0); y <= maxY; y++)
            {
                for (int z = minZ; z <= maxZ; z++)
                {
                    if (x < 0 || y < 0 || z < 0 || x >= worldSizeX || y >= worldSizeY || z >= worldSizeZ)
                        continue;

                    MarchingCubesGPU chunk = GetChunk(x, y, z);
                    if (chunk == null)
                        continue;

                    Vector3 localHit = worldPos - chunk.ChunkWorldPosition;

                    chunk.ApplyDigAtWorld(worldPos, radius);

                    SyncBorderVoxels(chunk, x, y, z); // sync borders
                    RemeshBorderingChunks(x, y, z, localHit, radius); // also remesh neighbors
                }
            }
        }
    }
    public void SyncBorderVoxels(MarchingCubesGPU sourceChunk, int cx, int cy, int cz)
    {
        Vector3Int[] directions = {
        new Vector3Int(1, 0, 0),  // right (+X)
        new Vector3Int(0, 1, 0),  // up (+Y)
        new Vector3Int(0, 0, 1)   // forward (+Z)
    };

        foreach (Vector3Int dir in directions)
        {
            int nx = cx + dir.x;
            int ny = cy + dir.y;
            int nz = cz + dir.z;

            // Only sync if the neighbor exists
            if (nx < 0 || ny < 0 || nz < 0 || nx >= worldSizeX || ny >= worldSizeY || nz >= worldSizeZ)
                continue;

            var neighborChunk = chunks[nx, ny, nz];
            if (neighborChunk != null)
            {
                SyncSharedFace(sourceChunk, neighborChunk, dir);  // copy FROM sourceChunk TO neighborChunk
                neighborChunk.Remesh(); // after syncing, remesh neighbor
            }
        }

        // Also remesh the sourceChunk AFTER syncing
        sourceChunk.Remesh();
    }

    public void RemeshBorderingChunks(int cx, int cy, int cz, Vector3 localPos, float radius)
    {
        bool nearMinX = localPos.x - radius <= 1;
        bool nearMaxX = localPos.x + radius >= chunkSize - 1;
        bool nearMinY = localPos.y - radius <= 1;
        bool nearMaxY = localPos.y + radius >= chunkSize - 1;
        bool nearMinZ = localPos.z - radius <= 1;
        bool nearMaxZ = localPos.z + radius >= chunkSize - 1;

        void Remesh(int x, int y, int z)
        {
            if (x < 0 || y < 0 || z < 0 ||
                x >= worldSizeX || y >= worldSizeY || z >= worldSizeZ)
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
    void SyncSharedFace(MarchingCubesGPU from, MarchingCubesGPU to, Vector3Int direction)
    {
        int edge = MarchingCubesGPU.N;

        if (direction == Vector3Int.right)
            to.CopyBorderFrom(from, faceAxis: 0, sourceCoord: edge, targetCoord: 0); // X axis
        else if (direction == Vector3Int.up)
            to.CopyBorderFrom(from, faceAxis: 1, sourceCoord: edge, targetCoord: 0); // Y axis
        else if (direction == Vector3Int.forward)
            to.CopyBorderFrom(from, faceAxis: 2, sourceCoord: edge, targetCoord: 0); // Z axis
    }

}
