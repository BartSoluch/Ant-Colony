using UnityEngine;
using System.Collections.Generic;
using MarchingCubesProject;

public class VoxelTerrainGenerator : MonoBehaviour
{
    public int width = 16;
    public int height = 16;
    public int depth = 16;
    public float surface = 0.0f;
    public Material terrainMaterial;  // Reference to the material (for the shader)

    private Dictionary<Vector3Int, float> voxelData = new Dictionary<Vector3Int, float>(); // Sparse storage
    private MeshFilter meshFilter;
    private MeshCollider meshCollider;

    void Start()
    {
        // Ensure components exist
        if (GetComponent<MeshFilter>() == null)
            gameObject.AddComponent<MeshFilter>();
        if (GetComponent<MeshRenderer>() == null)
            gameObject.AddComponent<MeshRenderer>();
        if (GetComponent<MeshCollider>() == null)
            gameObject.AddComponent<MeshCollider>();

        meshFilter = GetComponent<MeshFilter>();
        meshCollider = GetComponent<MeshCollider>();

        // Generate initial voxel data
        GenerateVoxelData();
        GenerateMesh();
    }

    void GenerateVoxelData()
    {
        int minHeight = -height / 2; // Start the terrain generation below y = 0
        int maxHeight = height / 2;

        // Using a dictionary to store only solid voxels
        for (int x = 0; x <= width; x++)
        {
            for (int y = minHeight; y <= maxHeight; y++)  // Now the terrain spans below y = 0
            {
                for (int z = 0; z <= depth; z++)
                {
                    Vector3Int voxelPos = new Vector3Int(x, y, z);
                    if (x == 0 || x == width || y == minHeight || y == maxHeight || z == 0 || z == depth)
                        voxelData[voxelPos] = -1f;  // Set to air (-1) for boundaries
                    else
                        voxelData[voxelPos] = 1f;   // Set to solid (1) for inside
                }
            }
        }
    }

    void GenerateMesh()
    {
        if (voxelData.Count == 0)
        {
            Debug.LogError("Voxel data is empty. Cannot generate mesh.");
            return;
        }

        // Convert voxel data from dictionary to a 3D array for Marching Cubes
        float[,,] voxelDataArray = ConvertDictionaryToArray(voxelData);

        var mc = new MarchingCubes(surface);
        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();

        // Generate the mesh for the currently active voxels (this is where you use the Marching Cubes algorithm)
        mc.Generate(voxelDataArray, vertices, triangles);

        // Create the mesh and apply it
        Mesh mesh = new Mesh();
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();

        meshFilter.mesh = mesh;
        meshCollider.sharedMesh = mesh;

        Debug.Log($"Mesh generated: {vertices.Count} vertices, {triangles.Count / 3} triangles");

        // Update the transparency in the material
        if (terrainMaterial != null)
        {
            float digFactor = CalculateDigFactor();
            terrainMaterial.SetFloat("_Transparency", digFactor);
        }
    }

    float CalculateDigFactor()
    {
        // Example method to calculate transparency, adjust as needed.
        return 0.5f;  // Placeholder transparency value; adjust as needed
    }

    public void Dig(Vector3 position, float radius)
    {
        bool changed = false; // Flag to track if any voxel is changed
        int totalVoxels = 0;
        int dugVoxels = 0;

        // Loop through all the voxels in the grid (using the dictionary)
        List<Vector3Int> voxelsToRemove = new List<Vector3Int>();

        foreach (var voxel in voxelData)
        {
            Vector3 voxelPosition = voxel.Key;
            totalVoxels++;

            // Corrected distance check (ensure you check the correct side of the terrain)
            float distance = Vector3.Distance(voxelPosition, position);
            if (distance < radius)
            {
                // Only change the voxel if it's solid (not already air)
                if (voxel.Value != -1f) // Air
                {
                    voxelsToRemove.Add(voxel.Key); // Mark voxel to be "dug"
                    dugVoxels++;
                    changed = true;
                }
            }
        }

        // Remove the dug voxels
        foreach (var voxelToRemove in voxelsToRemove)
        {
            voxelData[voxelToRemove] = -1f;  // Set the voxel to air
        }

        // If the voxel data changed, regenerate the mesh
        if (changed)
        {
            Debug.Log("Voxel data changed, regenerating mesh.");
            GenerateMesh();

            // Calculate the dig factor (percentage of dug voxels)
            float digFactor = (float)dugVoxels / totalVoxels;
            UpdateMaterial(digFactor);
        }
        else
        {
            Debug.Log("No change in voxel data, skipping mesh regeneration.");
        }
    }

    private void UpdateMaterial(float digFactor)
    {
        // Update the shader with the new dig factor
        if (terrainMaterial != null)
        {
            terrainMaterial.SetFloat("_DigFactor", digFactor);
        }
    }

    // Helper function to convert dictionary to 3D array for Marching Cubes
    private float[,,] ConvertDictionaryToArray(Dictionary<Vector3Int, float> voxelData)
    {
        float[,,] voxelDataArray = new float[width + 1, height + 1, depth + 1];

        // Loop through the dictionary and assign the values to the 3D array
        foreach (var kvp in voxelData)
        {
            Vector3Int pos = kvp.Key;
            float value = kvp.Value;

            // Ensure we're within the array bounds
            if (pos.x >= 0 && pos.x <= width && pos.y >= 0 && pos.y <= height && pos.z >= 0 && pos.z <= depth)
            {
                voxelDataArray[pos.x, pos.y, pos.z] = value;
            }
        }

        return voxelDataArray;
    }
}
