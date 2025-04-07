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
                    Vector3 position = new Vector3(x, y, z) * chunkSize;
                    GameObject chunk = Instantiate(chunkPrefab, position, Quaternion.identity, transform);
                    MarchingCubesGPU mc = chunk.GetComponent<MarchingCubesGPU>();
                    chunks[x, y, z] = mc;
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
}
