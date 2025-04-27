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

    public int yOffset = 0;

    private MarchingCubesGPU[,,] chunks;
    // A dictionary to hold a normals RenderTexture for each chunk.
    private Dictionary<Vector3Int, RenderTexture> normalsPool = new Dictionary<Vector3Int, RenderTexture>();

    void Start()
    {
        yOffset = worldSizeY / 2; // Center world vertically (allows y=-1, y=0, y=1, etc.)

        chunks = new MarchingCubesGPU[worldSizeX, worldSizeY, worldSizeZ];

        for (int x = 0; x < worldSizeX; x++)
        {
            for (int y = 0; y < worldSizeY; y++)
            {
                for (int z = 0; z < worldSizeZ; z++)
                {
                    Vector3 position = new Vector3(x, y - yOffset, z) * chunkSize;
                    GameObject chunk = Instantiate(chunkPrefab, position, Quaternion.identity, transform);

                    MarchingCubesGPU mc = chunk.GetComponent<MarchingCubesGPU>();
                    chunks[x, y, z] = mc;
                    Vector3Int coord = new Vector3Int(x, y - yOffset, z);
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
        int y = Mathf.FloorToInt(worldPos.y / chunkSize) + yOffset; // Add yOffset
        int z = Mathf.FloorToInt(worldPos.z / chunkSize);

        Debug.Log($"[ChunkManager] Lookup chunk at ({x},{y - yOffset},{z})");

        if (x < 0 || y < 0 || z < 0 || x >= worldSizeX || y >= worldSizeY || z >= worldSizeZ)
            return null;

        return chunks[x, y, z];
    }


    public MarchingCubesGPU GetChunk(int x, int y, int z)
    {
        y += yOffset; // Apply offset here too

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
            for (int y = minY; y <= maxY; y++)
            {
                for (int z = minZ; z <= maxZ; z++)
                {
                    // Check if these chunk coordinates are within our world bounds.
                    if (x < 0 || y < 0 || z < 0 || x >= worldSizeX || y >= worldSizeY || z >= worldSizeZ)
                        continue;
                    MarchingCubesGPU chunk = GetChunk(x, y, z);
                    if (chunk == null)
                        continue;
                    // Compute the dig position in the chunk’s local space.
                    Vector3 localHit = worldPos - chunk.ChunkWorldPosition;
                    // Have the chunk update its own density buffer.
                    chunk.ApplyDigAtWorld(worldPos, radius);
                    // Update any shared border voxels (if needed).
                    SyncBorderVoxels(chunk, x, y, z);
                    // Optionally remesh neighboring chunks that might be affected.
                    RemeshBorderingChunks(x, y, z, localHit, radius);
                }
            }
        }
    }

    public void SyncBorderVoxels(MarchingCubesGPU sourceChunk, int cx, int cy, int cz)
    {
        Vector3Int[] directions = {
            new Vector3Int(-1, 0, 0), new Vector3Int(1, 0, 0),
            new Vector3Int(0, -1, 0), new Vector3Int(0, 1, 0),
            new Vector3Int(0, 0, -1), new Vector3Int(0, 0, 1)
        };

        foreach (Vector3Int dir in directions)
        {
            int nx = cx + dir.x;
            int ny = cy - dir.y;
            int nz = cz + dir.z;
            if (nx < 0 || ny < 0 || nz < 0 || nx >= worldSizeX || ny >= worldSizeY || nz >= worldSizeZ)
                continue;
            var neighborChunk = chunks[nx, ny, nz];
            SyncSharedFace(sourceChunk, neighborChunk, dir);
            Debug.Log($"[SyncBorder] {sourceChunk.ChunkCoord} syncing to neighbor {neighborChunk.ChunkCoord}");
        }
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
        // Use the correct interior edge; for our density field this should be N
        int edge = MarchingCubesGPU.N;

        if (direction == Vector3Int.right)
            to.CopyBorderFrom(from, faceAxis: 0, sourceCoord: edge, targetCoord: 0);
        else if (direction == Vector3Int.left)
            to.CopyBorderFrom(from, faceAxis: 0, sourceCoord: 0, targetCoord: edge);
        else if (direction == Vector3Int.up)
            to.CopyBorderFrom(from, faceAxis: 1, sourceCoord: edge, targetCoord: 0);
        else if (direction == Vector3Int.down)
            to.CopyBorderFrom(from, faceAxis: 1, sourceCoord: 0, targetCoord: edge);
        else if (direction == Vector3Int.forward)
            to.CopyBorderFrom(from, faceAxis: 2, sourceCoord: edge, targetCoord: 0);
        else if (direction == Vector3Int.back)
            to.CopyBorderFrom(from, faceAxis: 2, sourceCoord: 0, targetCoord: edge);

        to.Remesh();
    }

}
