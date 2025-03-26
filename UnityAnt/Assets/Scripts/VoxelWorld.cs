// VoxelWorld.cs
using UnityEngine;

public class VoxelWorld : MonoBehaviour
{
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

    public void Dig(Vector3 worldPos, float radius)
    {
        foreach (var chunk in chunks)
        {
            chunk.Dig(worldPos, radius);
        }
    }
}