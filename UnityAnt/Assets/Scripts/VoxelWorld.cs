// VoxelWorld.cs
using UnityEngine;




public class VoxelWorld : MonoBehaviour
{
    public static VoxelWorld Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject); // Prevent duplicates
            return;
        }

        Instance = this;
    }

    public Vector3Int WorldSize =>
    new Vector3Int(
        chunksX * chunkPrefab.GetComponent<VoxelChunk>().width,
        chunksY * chunkPrefab.GetComponent<VoxelChunk>().height,
        chunksZ * chunkPrefab.GetComponent<VoxelChunk>().depth
    );
    public Vector3 GetCenterWorldPosition()
    {
        VoxelChunk sample = chunkPrefab.GetComponent<VoxelChunk>();

        int chunkWidth = sample.width;
        int chunkHeight = sample.height;
        int chunkDepth = sample.depth;

        float worldX = chunksX * chunkWidth;
        float worldZ = chunksZ * chunkDepth;

        // Y = surface level. If you're only generating 1 chunk vertically and it sits at -chunkHeight,
        // then the surface is around Y = 0
        float surfaceY = 2f; // Just above the surface

        return new Vector3(worldX / 2f, surfaceY, worldZ / 2f);
    }

    void OnDrawGizmos()
    {
        if (Application.isPlaying)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawSphere(GetCenterWorldPosition(), 1f);
        }
    }


    public GameObject chunkPrefab;
    public int chunksX = 2;
    public int chunksY = 1;
    public int chunksZ = 2;
    public float surface = 0.0f;

    private VoxelChunk[,,] chunks;

    void Start()
    {
        GenerateChunks();
    }

    void GenerateChunks()
    {
        chunks = new VoxelChunk[chunksX, chunksY, chunksZ];

        // Get actual chunk dimensions from the prefab
        VoxelChunk tempChunk = chunkPrefab.GetComponent<VoxelChunk>();
        int chunkWidth = tempChunk.width;
        int chunkHeight = tempChunk.height;
        int chunkDepth = tempChunk.depth;

        for (int x = 0; x < chunksX; x++)
        {
            for (int y = 0; y < chunksY; y++)
            {
                for (int z = 0; z < chunksZ; z++)
                {
                    Vector3 position = new Vector3(
                        x * chunkWidth,
                        -(y + 1) * chunkHeight,
                        z * chunkDepth
                    );

                    GameObject chunkObj = Instantiate(chunkPrefab, position, Quaternion.identity, transform);
                    var chunk = chunkObj.GetComponent<VoxelChunk>();
                    chunk.Init(surface);
                    chunks[x, y, z] = chunk;
                }
            }
        }
    }
    public Vector3Int GetChunkCoordFromWorld(Vector3 worldPosition)
    {
        VoxelChunk sampleChunk = chunkPrefab.GetComponent<VoxelChunk>();
        int chunkWidth = sampleChunk.width;
        int chunkHeight = sampleChunk.height;
        int chunkDepth = sampleChunk.depth;

        int x = Mathf.FloorToInt(worldPosition.x / chunkWidth);
        int y = 0; // 👈 force to 0 for single-layer world
        int z = Mathf.FloorToInt(worldPosition.z / chunkDepth);

        return new Vector3Int(x, y, z);
    }



    public void Dig(Vector3 worldPos, float radius)
    {
        foreach (var chunk in chunks)
        {
            chunk.Dig(worldPos, radius);
        }
    }

    public void TryDigAt(Vector3 worldPosition, float radius)
    {
        Vector3Int chunkCoord = GetChunkCoordFromWorld(worldPosition);
        Debug.Log("Trying to dig at chunk: " + chunkCoord);

        int x = chunkCoord.x;
        int y = chunkCoord.y;
        int z = chunkCoord.z;

        if (x >= 0 && x < chunksX &&
            y >= 0 && y < chunksY &&
            z >= 0 && z < chunksZ)
        {
            Debug.Log("Calling Dig on chunk " + chunkCoord);
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        int nx = x + dx;
                        int ny = y + dy;
                        int nz = z + dz;

                        if (nx >= 0 && nx < chunksX &&
                            ny >= 0 && ny < chunksY &&
                            nz >= 0 && nz < chunksZ)
                        {
                            chunks[nx, ny, nz].Dig(worldPosition, radius);
                        }
                    }
        }
        else
        {
            Debug.LogWarning("Dig out of bounds: " + chunkCoord);
        }
    }


}