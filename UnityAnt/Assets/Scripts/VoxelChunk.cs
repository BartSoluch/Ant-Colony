using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using MarchingCubesProject;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public class VoxelChunk : MonoBehaviour
{
    public int width = 16, height = 16, depth = 16;
    public float surface = 0.0f;

    private Dictionary<Vector3Int, float> voxelData = new();
    private MeshFilter meshFilter;
    private MeshCollider meshCollider;
    private MarchingCubes marchingCubes;

    [SerializeField] private Material terrainMaterial;
    [SerializeField] private Material tunnelMaterial;

    public void Init(float surface)
    {
        this.surface = surface;
        marchingCubes = new MarchingCubes(surface);

        meshFilter = GetComponent<MeshFilter>();
        meshCollider = GetComponent<MeshCollider>();

        // Assign both materials to the renderer
        MeshRenderer renderer = GetComponent<MeshRenderer>();
        renderer.materials = new Material[] { terrainMaterial, tunnelMaterial };

        GenerateInitialData();
        _ = GenerateMeshAsync();
    }

    void GenerateInitialData()
    {
        voxelData.Clear();
        for (int x = -1; x <= width + 1; x++)
        {
            for (int y = -1; y <= height + 1; y++)
            {
                for (int z = -1; z <= depth + 1; z++)
                {
                    Vector3Int pos = new(x, y, z);
                    bool isEdge = x == -1 || x == width + 1 || y == -1 || y == height + 1 || z == -1 || z == depth + 1;
                    voxelData[pos] = isEdge ? -1f : 1f;
                }
            }
        }
    }

    public void Dig(Vector3 worldPos, float radius)
    {
        Vector3 localPos = transform.InverseTransformPoint(worldPos);
        Vector3Int min = Vector3Int.FloorToInt(localPos - Vector3.one * radius);
        Vector3Int max = Vector3Int.CeilToInt(localPos + Vector3.one * radius);

        bool changed = false;
        for (int x = min.x; x <= max.x; x++)
        {
            for (int y = min.y; y <= max.y; y++)
            {
                for (int z = min.z; z <= max.z; z++)
                {
                    Vector3Int pos = new(x, y, z);
                    if (!voxelData.ContainsKey(pos)) continue;

                    float distance = Vector3.Distance(pos, localPos);
                    if (distance < radius)
                    {
                        float fade = Mathf.InverseLerp(radius, radius * 0.5f, distance);
                        float newValue = Mathf.Lerp(voxelData[pos], -1f, fade);
                        if (!Mathf.Approximately(voxelData[pos], newValue))
                        {
                            voxelData[pos] = newValue;
                            changed = true;
                        }
                    }
                }
            }
        }

        if (changed)
        {
            _ = GenerateMeshAsync();
        }
    }

    async Task GenerateMeshAsync()
    {
        float[,,] density = new float[width + 3, height + 3, depth + 3];

        foreach (var kvp in voxelData)
        {
            Vector3Int pos = kvp.Key;
            int x = pos.x + 1;
            int y = pos.y + 1;
            int z = pos.z + 1;

            if (x >= 0 && x < width + 3 && y >= 0 && y < height + 3 && z >= 0 && z < depth + 3)
                density[x, y, z] = kvp.Value;
        }

        List<Vector3> verts = new();
        marchingCubes.terrainIndices.Clear();
        marchingCubes.tunnelIndices.Clear();

        await Task.Run(() =>
        {
            marchingCubes.Generate(density, verts, null);
        });

        // Safety check
        if (verts.Count < 3 ||
            (marchingCubes.terrainIndices.Count < 3 && marchingCubes.tunnelIndices.Count < 3))
        {
            meshFilter.mesh = null;
            meshCollider.sharedMesh = null;
            return;
        }

        Mesh mesh = new()
        {
            indexFormat = UnityEngine.Rendering.IndexFormat.UInt32
        };

        mesh.SetVertices(verts);
        mesh.subMeshCount = 2;
        mesh.SetTriangles(marchingCubes.terrainIndices, 0);
        mesh.SetTriangles(marchingCubes.tunnelIndices, 1);
        mesh.RecalculateNormals();

        meshFilter.mesh = mesh;
        meshCollider.sharedMesh = mesh;
    }

    void Update()
    {
        if (meshFilter != null && meshFilter.sharedMesh != null)
        {
            Material[] mats = GetComponent<MeshRenderer>().materials;
            foreach (var mat in mats)
            {
                mat.SetVector("_RevealCenter", Camera.main.transform.position);
            }
        }
    }
}
