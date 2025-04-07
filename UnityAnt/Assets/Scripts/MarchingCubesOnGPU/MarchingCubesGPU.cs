using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;
using ImprovedPerlinNoiseProject;
using UnityEngine.UIElements;

#pragma warning disable 162

namespace MarchingCubesGPUProject
{
    public class MarchingCubesGPU : MonoBehaviour
    {
        //The size of the voxel array for each dimension
        const int N = 64;
        const int P = 1; // padding

        //The size of the buffer that holds the verts.
        //This is the maximum number of verts that the 
        //marching cube can produce, 5 triangles for each voxel.
        const int SIZE = N * N * N * 3 * 5;

        public int m_seed = 0;

        public ComputeShader m_digShader; // Assign this in the Inspector

        public Material m_drawBuffer;

        public ComputeShader m_perlinNoise;

        public ComputeShader m_marchingCubes;

        public ComputeShader m_normals;

        ComputeBuffer m_noiseBuffer, m_meshBuffer;

        RenderTexture m_normalsBuffer;

        ComputeBuffer m_cubeEdgeFlags, m_triangleConnectionTable;

        GPUPerlinNoise perlin;

        [SerializeField] private Material voxelMaterial;

        public Vector3Int ChunkCoord; // Assigned by ChunkManager

        public Vector3 ChunkWorldPosition; // Assigned by ChunkManager

        private GameObject currentColliderMesh;

        ComputeBuffer m_vertexCountBuffer;
        int[] vertexCountArray = new int[1]; // just holds one int
        int m_actualVertexCount = 0;         // stores the live count for drawing

        void Start()
        {
            Debug.Log("Marching Cubes GPU: Start() called");

            if (N % 8 != 0)
                throw new System.ArgumentException("N must be divisible by 8");

            int densityWidth = N + 1 + P * 2;
            int voxelCount = densityWidth * densityWidth * densityWidth;
            m_noiseBuffer = new ComputeBuffer(voxelCount, sizeof(float));

            m_normalsBuffer = new RenderTexture(N, N, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            m_normalsBuffer.dimension = TextureDimension.Tex3D;
            m_normalsBuffer.enableRandomWrite = true;
            m_normalsBuffer.useMipMap = false;
            m_normalsBuffer.volumeDepth = N;
            m_normalsBuffer.Create();

            // Change to StructuredBuffer
            m_meshBuffer = new ComputeBuffer(SIZE, sizeof(float) * 7, ComputeBufferType.Default);  // No Append, use Default
            m_vertexCountBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.Raw);

            m_cubeEdgeFlags = new ComputeBuffer(256, sizeof(int));
            m_cubeEdgeFlags.SetData(MarchingCubesTables.CubeEdgeFlags);

            m_triangleConnectionTable = new ComputeBuffer(256 * 16, sizeof(int));
            m_triangleConnectionTable.SetData(MarchingCubesTables.TriangleConnectionTable);

            float[] flatDensity = new float[densityWidth * densityWidth * densityWidth];
            float frequency = 0.02f;
            float heightScale = N * 0.2f;

            Random.InitState(m_seed);
            float offsetX = Random.Range(0f, 1000f);
            float offsetZ = Random.Range(0f, 1000f);

            for (int z = 0; z <= N; z++)
            {
                for (int y = 0; y <= N; y++)
                {
                    for (int x = 0; x <= N; x++)
                    {
                        int i = (x + P) + (y + P) * densityWidth + (z + P) * densityWidth * densityWidth;
                        float perlin = Mathf.PerlinNoise(
                            (x + ChunkCoord.x * N) * frequency + offsetX,
                            (z + ChunkCoord.z * N) * frequency + offsetZ);
                        float height = perlin * heightScale + (N * 0.3f);
                        float worldY = y + ChunkCoord.y * N;
                        flatDensity[i] = height - worldY;
                    }
                }
            }

            m_noiseBuffer.SetData(flatDensity);
            m_vertexCountBuffer.SetData(new int[] { 0 });
            DispatchNormals();
            DispatchMesh();
            GameObject colliderMesh = ReadBackMesh();
            colliderMesh.GetComponent<Renderer>().enabled = false; // hide it


            Debug.Log("Marching Cubes GPU: Dispatched all shaders");

            // Get the vertices back
            Vert[] verts = new Vert[m_actualVertexCount];
            m_meshBuffer.GetData(verts, 0, 0, m_actualVertexCount);

            for (int i = 0; i < Mathf.Min(30, verts.Length); i += 3)
            {
                Debug.Log($"Triangle {i / 3}:");
                Debug.Log($"  A: {verts[i].position}");
                Debug.Log($"  B: {verts[i + 1].position}");
                Debug.Log($"  C: {verts[i + 2].position}");
            }
        }

        void Update() { }

        // Draws the mesh
        void OnRenderObject()
        {
            if (m_actualVertexCount > 0)
            {
                m_drawBuffer.SetBuffer("_Buffer", m_meshBuffer);
                m_drawBuffer.SetPass(0);
                Graphics.DrawProceduralNow(MeshTopology.Triangles, m_actualVertexCount);
            }
        }

        void OnDestroy()
        {
            m_noiseBuffer.Release();
            m_meshBuffer.Release();
            m_cubeEdgeFlags.Release();
            m_triangleConnectionTable.Release();
            m_normalsBuffer.Release();
            m_vertexCountBuffer?.Release();
        }

        struct Vert
        {
            public Vector4 position;
            public Vector3 normal;
        }

        /// <summary>
        /// Reads back the mesh data from the GPU and turns it into a standard unity mesh.
        /// </summary>
        /// <returns></returns>
        GameObject ReadBackMesh()
        {
            GenerateMeshData(out var positions, out var normals, out var index);

            GameObject physicsMeshObject = new GameObject("PhysicsMesh");
            physicsMeshObject.transform.SetParent(transform);
            physicsMeshObject.transform.localPosition = -ChunkWorldPosition;
            physicsMeshObject.AddComponent<MeshFilter>();
            physicsMeshObject.AddComponent<MeshRenderer>().enabled = true;
            physicsMeshObject.GetComponent<Renderer>().material.color = Color.red;
            physicsMeshObject.AddComponent<MeshCollider>();

            Mesh mesh = new Mesh();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = positions.ToArray();
            mesh.normals = normals.ToArray();
            mesh.bounds = new Bounds(new Vector3(0, N / 2, 0), new Vector3(N, N, N));
            mesh.SetTriangles(index.ToArray(), 0);

            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.Optimize();

            physicsMeshObject.GetComponent<MeshFilter>().mesh = mesh;

            MeshCollider collider = physicsMeshObject.GetComponent<MeshCollider>();
            collider.sharedMesh = null;
            collider.sharedMesh = mesh;

            physicsMeshObject.GetComponent<MeshRenderer>().enabled = true;
            physicsMeshObject.GetComponent<Renderer>().material = voxelMaterial;

            return physicsMeshObject;
        }


        public void DigAtLocal(Vector3 local, float radius)
        {
            Vector3Int idPos = Vector3Int.FloorToInt(local);
            int paddedWidth = N + 1 + P * 2;
            idPos = Vector3Int.Max(Vector3Int.zero, Vector3Int.Min(idPos, new Vector3Int(N - 1, N - 1, N - 1)));

            Debug.Log($"Digging at voxel index: {idPos} (local: {local})");

            int kernel = m_digShader.FindKernel("CSMain");
            m_digShader.SetInt("_Width", paddedWidth);
            m_digShader.SetInt("_Height", paddedWidth);
            m_digShader.SetInt("_Depth", paddedWidth);

            m_digShader.SetVector("_DigPosition", local); // ✅ full float position!
            m_digShader.SetFloat("_DigRadius", radius);
            m_digShader.SetBuffer(kernel, "_Noise", m_noiseBuffer);
            m_digShader.Dispatch(kernel, N / 8, N / 8, N / 8);

            if (m_noiseBuffer == null || m_digShader == null)
            {
                Debug.LogError("Dig shader or noise buffer is null!");
                return;
            }

            DispatchNormals();
            DispatchMesh();

            if (currentColliderMesh == null)
                currentColliderMesh = ReadBackMesh();
            else
                UpdateColliderMesh(currentColliderMesh);

            currentColliderMesh.GetComponent<Renderer>().enabled = false;
        }

        public float SampleDensity(Vector3Int voxelIndex)
        {
            int densityWidth = N + 1;
            if (voxelIndex.x < 0 || voxelIndex.y < 0 || voxelIndex.z < 0 ||
                voxelIndex.x >= N + 1 || voxelIndex.y >= N + 1 || voxelIndex.z >= N + 1)
                return float.MinValue;

            float[] result = new float[1];
            int i = voxelIndex.x + voxelIndex.y * densityWidth + voxelIndex.z * densityWidth * densityWidth;
            ComputeBuffer readback = new ComputeBuffer(1, sizeof(float));
            m_noiseBuffer.GetData(result, 0, i, 1);
            readback.Release();
            return result[0];
        }
        public bool RaycastVoxel(Vector3 rayOrigin, Vector3 rayDirection, float maxDistance, out Vector3 hitPos)
        {
            float stepSize = 0.5f;
            for (float t = 0; t < maxDistance; t += stepSize)
            {
                Vector3 samplePos = rayOrigin + rayDirection * t;
                // Use the voxel grid’s origin (_ChunkWorldPosition) here:
                Vector3 local = samplePos - ChunkWorldPosition;
                Vector3Int voxelIndex = Vector3Int.FloorToInt(local);

                float density = SampleDensity(voxelIndex);
                Debug.Log($"[RaycastVoxel] t={t:F2}, local={local}, index={voxelIndex}, density={density}");
                if (density > 0.0f)
                {
                    hitPos = samplePos;
                    Debug.Log($"[Hit] Voxel at {voxelIndex} with density {density}");
                    return true;
                }
            }
            hitPos = Vector3.zero;
            return false;
        }
        void UpdateColliderMesh(GameObject meshObj)
        {
            GenerateMeshData(out var positions, out var normals, out var index);

            Mesh mesh = new Mesh();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = positions.ToArray();
            mesh.normals = normals.ToArray();
            mesh.SetTriangles(index.ToArray(), 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            meshObj.GetComponent<MeshFilter>().mesh = mesh;

            var collider = meshObj.GetComponent<MeshCollider>();
            collider.sharedMesh = null;
            collider.sharedMesh = mesh;
        }

        private void GenerateMeshData(out List<Vector3> positions, out List<Vector3> normals, out List<int> index)
        {
            Vert[] verts = new Vert[m_actualVertexCount];
            m_meshBuffer.GetData(verts, 0, 0, m_actualVertexCount);

            positions = new List<Vector3>();
            normals = new List<Vector3>();
            index = new List<int>();

            for (int i = 0; i < verts.Length; i++)
            {
                positions.Add(verts[i].position);
                normals.Add(verts[i].normal);
            }

            for (int i = 0; i < verts.Length; i += 3)
            {
                index.Add(i + 2);
                index.Add(i + 1);
                index.Add(i);
            }
        }
        public void Remesh()
        {
            DispatchNormals();
            DispatchMesh();
        }

        void DispatchNormals()
        {
            int paddedWidth = N + 1 + P * 2;
            m_normals.SetInt("_Width", paddedWidth);
            m_normals.SetInt("_Height", paddedWidth);
            m_normals.SetBuffer(0, "_Noise", m_noiseBuffer);
            m_normals.SetTexture(0, "_Result", m_normalsBuffer);
            m_normals.Dispatch(0, N / 8, N / 8, N / 8);
        }

        void DispatchMesh()
        {
            m_vertexCountBuffer.SetData(new int[] { 0 });

            int kernel = m_marchingCubes.FindKernel("CSMain");

            int paddedWidth = N + 1 + P * 2;
            m_marchingCubes.SetInt("_Width", paddedWidth);
            m_marchingCubes.SetInt("_Height", paddedWidth);
            m_marchingCubes.SetInt("_Depth", paddedWidth);

            m_marchingCubes.SetInt("_Border", 1);
            m_marchingCubes.SetFloat("_Target", 0.0f);

            m_marchingCubes.SetBuffer(kernel, "_Voxels", m_noiseBuffer);
            m_marchingCubes.SetTexture(kernel, "_Normals", m_normalsBuffer);
            m_marchingCubes.SetBuffer(kernel, "_Buffer", m_meshBuffer);
            m_marchingCubes.SetBuffer(kernel, "_CubeEdgeFlags", m_cubeEdgeFlags);
            m_marchingCubes.SetBuffer(kernel, "_TriangleConnectionTable", m_triangleConnectionTable);
            m_marchingCubes.SetBuffer(kernel, "_VertexIndexBuffer", m_vertexCountBuffer);

            m_marchingCubes.SetVector("_ChunkWorldPosition", ChunkWorldPosition);

            m_marchingCubes.Dispatch(kernel, N / 8, N / 8, N / 8);

            int[] count = new int[1];
            m_vertexCountBuffer.GetData(count);
            m_actualVertexCount = count[0];

            Debug.Log($"[GPU Draw] Vertex Count: {m_actualVertexCount}");
        }

    }
}
