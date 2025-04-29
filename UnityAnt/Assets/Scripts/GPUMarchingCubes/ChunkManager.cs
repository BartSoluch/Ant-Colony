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

    [Header("Terrain Generation Settings")]
    public int seed = 0;
    public float frequency = 0.005f;
    public float groundVariationMultiplier = 0.5f;
    public float baseGroundHeightMultiplier = 2.5f;

    private MarchingCubesGPU[,,] chunks;
    // A dictionary to hold a normals RenderTexture for each chunk.
    private Dictionary<Vector3Int, RenderTexture> normalsPool = new Dictionary<Vector3Int, RenderTexture>();
    void Start()
    {
        chunks = new MarchingCubesGPU[worldSizeX, worldSizeY, worldSizeZ];

        // Step 1: Spawn all chunks
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
                    mc.m_seed = seed;

                    RenderTexture normalsBuffer = CreateNormalsBuffer();
                    normalsPool[coord] = normalsBuffer;
                    mc.SetNormalsBuffer(normalsBuffer);
                    mc.SetChunkManager(this);
                    mc.groundVariationMultiplier = groundVariationMultiplier;
                    mc.baseGroundHeightMultiplier = baseGroundHeightMultiplier;

                    //Debug.Log($"Chunk {coord} → worldPos = {chunk.transform.position}");
                }
            }
        }

        // 🛠 Step 2: Defer border syncing to next frame
        Invoke(nameof(LateSyncChunks), 0f);
    }

    void LateSyncChunks()
    {
        //Debug.Log("[ChunkManager] Performing late chunk border syncing...");

        for (int x = 0; x < worldSizeX; x++)
        {
            for (int y = 0; y < worldSizeY; y++)
            {
                for (int z = 0; z < worldSizeZ; z++)
                {
                    MarchingCubesGPU chunk = chunks[x, y, z];
                    if (chunk == null) continue;

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

        //Debug.Log($"[ChunkManager] Lookup chunk at ({x},{y},{z})");

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

        Vector3 minWorld = worldPos - Vector3.one * radius;
        Vector3 maxWorld = worldPos + Vector3.one * radius;

        int minX = Mathf.FloorToInt(minWorld.x / cs);
        int maxX = Mathf.FloorToInt(maxWorld.x / cs);
        int minY = Mathf.FloorToInt(minWorld.y / cs);
        int maxY = Mathf.FloorToInt(maxWorld.y / cs);
        int minZ = Mathf.FloorToInt(minWorld.z / cs);
        int maxZ = Mathf.FloorToInt(maxWorld.z / cs);

        HashSet<Vector3Int> affectedChunks = new HashSet<Vector3Int>();

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

                    chunk.ApplyDigAtWorld(worldPos, radius);

                    // Mark affected chunks
                    affectedChunks.Add(new Vector3Int(x, y, z));

                    // Also mark neighbors that need syncing
                    affectedChunks.Add(new Vector3Int(x + 1, y, z));
                    affectedChunks.Add(new Vector3Int(x - 1, y, z));
                    affectedChunks.Add(new Vector3Int(x, y + 1, z));
                    affectedChunks.Add(new Vector3Int(x, y - 1, z));
                    affectedChunks.Add(new Vector3Int(x, y, z + 1));
                    affectedChunks.Add(new Vector3Int(x, y, z - 1));
                }
            }
        }

        // Step 2: Now sync all borders
        foreach (var coord in affectedChunks)
        {
            MarchingCubesGPU chunk = GetChunk(coord.x, coord.y, coord.z);
            if (chunk != null)
            {
                SyncBorderVoxels(chunk, coord.x, coord.y, coord.z);
            }
        }

        // Step 3: Force GPU sync (small dummy buffer if needed)

        // Step 4: Remesh all affected chunks
        foreach (var coord in affectedChunks)
        {
            MarchingCubesGPU chunk = GetChunk(coord.x, coord.y, coord.z);
            if (chunk != null)
            {
                if (chunk.dirtyVoxels > 0.5f * Mathf.Pow(MarchingCubesGPU.N, 3)) // 50% modified
                {
                    chunk.Remesh(); // Full Remesh as maintenance
                    chunk.dirtyVoxels = 0;
                }
            }
        }
    }

    public void SyncBorderVoxels(MarchingCubesGPU sourceChunk, int cx, int cy, int cz)
    {
        Vector3Int[] directions = {
        new Vector3Int(1, 0, 0),  // +X (right)
        new Vector3Int(0, 1, 0),  // +Y (up)
        new Vector3Int(0, 0, 1)   // +Z (forward)
    };

        foreach (Vector3Int dir in directions)
        {
            int nx = cx + dir.x;
            int ny = cy + dir.y;
            int nz = cz + dir.z;

            if (nx < 0 || ny < 0 || nz < 0 || nx >= worldSizeX || ny >= worldSizeY || nz >= worldSizeZ)
                continue;

            var neighborChunk = chunks[nx, ny, nz];
            if (neighborChunk != null)
            {
                // Push modified chunk's edge into neighbor
                SyncSharedFace(sourceChunk, neighborChunk, dir);

                // Pull neighbor's edge back into modified chunk
                SyncSharedFace(neighborChunk, sourceChunk, -dir);
            }
        }
    }

    void SyncSharedFace(MarchingCubesGPU from, MarchingCubesGPU to, Vector3Int direction)
    {
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
    }

}
