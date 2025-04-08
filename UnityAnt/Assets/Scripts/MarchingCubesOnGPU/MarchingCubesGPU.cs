using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;
using ImprovedPerlinNoiseProject;

#pragma warning disable 162

namespace MarchingCubesGPUProject
{
    public class MarchingCubesGPU : MonoBehaviour
    {
        public const int N = 64;
        const int P = 1;  // padding
        const int SIZE = N * N * N * 3 * 5;

        public int m_seed = 0;
        public ComputeShader m_digShader;
        public Material m_drawBuffer;
        public ComputeShader m_perlinNoise;
        public ComputeShader m_marchingCubes;
        public ComputeShader m_normals;

        ComputeBuffer m_noiseBuffer, m_meshBuffer;
        // The normals buffer is now provided externally.
        RenderTexture m_normalsBuffer;

        ComputeBuffer m_cubeEdgeFlags, m_triangleConnectionTable;
        [SerializeField] private Material voxelMaterial;
        public Vector3Int ChunkCoord;
        public Vector3 ChunkWorldPosition;

        ComputeBuffer m_vertexCountBuffer;
        int m_actualVertexCount = 0;

        public ChunkManager chunkManager;

        void Start()
        {
            Debug.Log("Marching Cubes GPU: Start() called");

            if (N % 8 != 0)
                throw new System.ArgumentException("N must be divisible by 8");

            int densityWidth = N + 1 + P * 2;
            int voxelCount = densityWidth * densityWidth * densityWidth;
            m_noiseBuffer = new ComputeBuffer(voxelCount, sizeof(float));

            // Do not create a normals RenderTexture here – it is assigned by the manager!
            m_meshBuffer = new ComputeBuffer(SIZE, sizeof(float) * 7, ComputeBufferType.Default);
            m_vertexCountBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.Raw);

            m_cubeEdgeFlags = new ComputeBuffer(256, sizeof(int));
            m_cubeEdgeFlags.SetData(MarchingCubesTables.CubeEdgeFlags);

            m_triangleConnectionTable = new ComputeBuffer(256 * 16, sizeof(int));
            m_triangleConnectionTable.SetData(MarchingCubesTables.TriangleConnectionTable);

            // Initialize the flat density array
            float[] flatDensity = new float[voxelCount];
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

            Debug.Log("Marching Cubes GPU: Dispatched all shaders");
        }

        // This method is called by the manager to assign the normals RenderTexture.
        public void SetNormalsBuffer(RenderTexture sharedBuffer)
        {
            m_normalsBuffer = sharedBuffer;
        }

        public void SetChunkManager(ChunkManager manager)
        {
            chunkManager = manager;
        }

        void Update() { }

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
            // Do not release m_normalsBuffer here because it is managed externally.
            m_vertexCountBuffer?.Release();
        }

        struct Vert
        {
            public Vector4 position;
            public Vector3 normal;
        }
        public void ApplyDigAtLocal(Vector3 local, float radius)
        {
            // Clamp the radius (if you wish)
            float maxDigRadius = 10.0f;
            radius = Mathf.Min(radius, maxDigRadius);

            // Find the kernel for the dig compute shader.
            int kernel = m_digShader.FindKernel("CSMain");

            // Set the dig parameters. Note that the compute shader expects local coordinates.
            m_digShader.SetFloat("_DigRadius", radius);
            m_digShader.SetVector("_DigPosition", local);

            // Pass our density/noise buffer.
            m_digShader.SetBuffer(kernel, "_Noise", m_noiseBuffer);

            // Calculate thread groups based on the padded width of the noise buffer.
            int paddedWidth = N + 1 + P * 2;
            int groups = Mathf.CeilToInt((float)paddedWidth / 8f);
            m_digShader.Dispatch(kernel, groups, groups, groups);

            // After modifying the density, update normals and remesh.
            Remesh();
        }

        public bool RaymarchDig(Vector3 rayOrigin, Vector3 rayDir, float maxDistance, out Vector3 hitPos)
        {
            ComputeBuffer outBuffer = new ComputeBuffer(1, sizeof(float));
            outBuffer.SetData(new float[] { -1f });

            int kernel = m_digShader.FindKernel("Raymarch");
            int padded = N + 1 + P * 2;

            m_digShader.SetInt("_Width", padded);
            m_digShader.SetInt("_Height", padded);
            m_digShader.SetInt("_Depth", padded);
            m_digShader.SetFloat("_MaxDistance", maxDistance);
            m_digShader.SetVector("_RayOrigin", rayOrigin - ChunkWorldPosition);
            m_digShader.SetVector("_RayDirection", rayDir);
            m_digShader.SetBuffer(kernel, "_Voxels", m_noiseBuffer);
            m_digShader.SetBuffer(kernel, "_VoxelOut", outBuffer);

            m_digShader.Dispatch(kernel, 1, 1, 1);

            float[] result = new float[1];
            outBuffer.GetData(result);
            outBuffer.Release();

            if (result[0] > 0)
            {
                hitPos = rayOrigin + rayDir * result[0];
                return true;
            }
            else
            {
                hitPos = Vector3.zero;
                return false;
            }
        }

        public void DigAtWorldPosition(Vector3 worldPos, float radius)
        {
            // This method is called externally to dig at a given world position.
            // It simply calls the per-chunk update.
            ApplyDigAtLocal(worldPos - ChunkWorldPosition, radius);
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

        public void CopyBorderFrom(MarchingCubesGPU other, int faceAxis, int sourceCoord, int targetCoord)
        {
            int kernel = m_digShader.FindKernel("CopyBorder");
            int paddedWidth = N + 1 + P * 2;

            m_digShader.SetInt("_PaddedWidth", paddedWidth);
            m_digShader.SetInt("_FaceAxis", faceAxis);
            m_digShader.SetInt("_SourceCoord", sourceCoord + P);
            m_digShader.SetInt("_TargetCoord", targetCoord + P);

            m_digShader.SetBuffer(kernel, "_SourceVoxels", other.m_noiseBuffer);
            m_digShader.SetBuffer(kernel, "_TargetVoxels", this.m_noiseBuffer);

            Vector3Int dispatchDims = faceAxis switch
            {
                0 => new Vector3Int(1, paddedWidth / 8, paddedWidth / 8),
                1 => new Vector3Int(paddedWidth / 8, 1, paddedWidth / 8),
                _ => new Vector3Int(paddedWidth / 8, paddedWidth / 8, 1),
            };

            m_digShader.Dispatch(kernel, dispatchDims.x, dispatchDims.y, dispatchDims.z);

            Debug.Log($"[CopyBorderFrom] From: {other.ChunkCoord} → {ChunkCoord}, axis: {faceAxis}, source: {sourceCoord}, target: {targetCoord}");
        }
    }
}
