using UnityEngine;

public class MarchingCubesGPUManager : MonoBehaviour
{
    public int gridSize = 32;  // Size of the voxel grid (e.g., 32x32x32)
    public float threshold = 0.0f;  // Surface value for Marching Cubes (typically 0.0f)
    public Material meshMaterial;  // Material to render the generated mesh

    private MeshFilter meshFilter;
    private ComputeShader marchingCubesShader;
    private ComputeBuffer voxelDataBuffer;
    private ComputeBuffer meshBuffer;
    private Mesh generatedMesh;

    void Start()
    {
        // Initialize the MeshFilter and MeshRenderer
        meshFilter = GetComponent<MeshFilter>();

        // Load the marching cubes compute shader from the resources
        marchingCubesShader = Resources.Load<ComputeShader>("MarchingCubes");

        // Initialize buffers
        InitializeBuffers();

        // Generate the voxel data
        GenerateVoxelData();

        // Dispatch the marching cubes compute shader
        GenerateMeshFromVoxels();

        // Create and assign the generated mesh to the MeshFilter
        meshFilter.mesh = generatedMesh;

        // Assign the material to the MeshRenderer
        if (meshMaterial != null)
        {
            GetComponent<MeshRenderer>().material = meshMaterial;
        }
    }

    // Initialize necessary compute buffers
    void InitializeBuffers()
    {
        // Buffer for voxel data (each voxel is a float representing its density)
        voxelDataBuffer = new ComputeBuffer(gridSize * gridSize * gridSize, sizeof(float));

        // Buffer to store mesh vertices and triangle indices
        meshBuffer = new ComputeBuffer(gridSize * gridSize * 3 * 5, sizeof(float) * 7); // 7: (x, y, z, r, g, b, a)

        // Initialize the mesh
        generatedMesh = new Mesh();
    }

    // Generate voxel data (you can replace this with more advanced noise or voxel generation)
    void GenerateVoxelData()
    {
        float[] voxelData = new float[gridSize * gridSize * gridSize];

        for (int z = 0; z < gridSize; z++)
        {
            for (int y = 0; y < gridSize; y++)
            {
                for (int x = 0; x < gridSize; x++)
                {
                    // Example: Simple sine wave voxel data (replace with any noise or procedural generation)
                    voxelData[x + y * gridSize + z * gridSize * gridSize] = Mathf.Sin(x * 0.1f) * Mathf.Cos(y * 0.1f) * Mathf.Sin(z * 0.1f);
                }
            }
        }

        // Set the voxel data into the compute buffer
        voxelDataBuffer.SetData(voxelData);
    }

    // Dispatch the marching cubes compute shader to generate the mesh
    void GenerateMeshFromVoxels()
    {
        // Set shader parameters
        marchingCubesShader.SetInt("_GridSize", gridSize);
        marchingCubesShader.SetFloat("_Threshold", threshold);
        marchingCubesShader.SetBuffer(0, "_VoxelData", voxelDataBuffer);
        marchingCubesShader.SetBuffer(0, "_MeshBuffer", meshBuffer);

        // Dispatch the shader
        int threadGroups = Mathf.CeilToInt(gridSize / 8.0f);  // 8 threads per group for each dimension
        marchingCubesShader.Dispatch(0, threadGroups, threadGroups, threadGroups);

        // Read the results from the buffer
        float[] meshData = new float[gridSize * gridSize * 3 * 5 * 7];
        meshBuffer.GetData(meshData);

        // Populate the generated mesh
        Vector3[] vertices = new Vector3[meshData.Length / 7];
        int[] triangles = new int[meshData.Length / 7];

        for (int i = 0; i < meshData.Length / 7; i++)
        {
            vertices[i] = new Vector3(meshData[i * 7], meshData[i * 7 + 1], meshData[i * 7 + 2]);
            triangles[i] = i;
        }

        generatedMesh.vertices = vertices;
        generatedMesh.triangles = triangles;
        generatedMesh.RecalculateNormals();
    }

    // Release resources when the object is destroyed
    void OnDestroy()
    {
        voxelDataBuffer.Release();
        meshBuffer.Release();
    }
}
