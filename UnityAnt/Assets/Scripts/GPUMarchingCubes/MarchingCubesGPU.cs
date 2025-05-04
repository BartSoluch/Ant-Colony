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

        // New: Seed for noise generation
        public int m_seed = 0;

        // Existing fields...
        public ComputeShader m_digShader;
        public Material m_drawBuffer;
        public ComputeShader m_perlinNoise;
        public ComputeShader m_marchingCubes;
        public ComputeShader m_normals;

        ComputeBuffer m_noiseBuffer, m_meshBuffer;
        RenderTexture m_normalsBuffer;
        ComputeBuffer m_cubeEdgeFlags, m_triangleConnectionTable;
        [SerializeField] private Material voxelMaterial;
        public Vector3Int ChunkCoord;
        public Vector3 ChunkWorldPosition;
        public ChunkManager chunkManager;
        ComputeBuffer m_vertexCountBuffer;
        int m_actualVertexCount = 0;

        // NEW: References for collision mesh updates.
        public MeshFilter collisionMeshFilter;   // Assign in Inspector (for collisions)

        private float[] cpuDensity;

        private List<Vector3> debugVoxelPositions = new List<Vector3>();

        public int dirtyVoxels = 0; // How many voxels got touched
        public bool needsRemesh => dirtyVoxels > (0.1f * Mathf.Pow(N, 3)); // 10% threshold

        [HideInInspector] public float frequency = 0.005f;
        [HideInInspector] public float groundVariationMultiplier = 0.5f;
        [HideInInspector] public float baseGroundHeightMultiplier = 2.5f;

        //private float[] waterGradient; // same size as density field
        //private float[] co2Gradient;

        void Start()
        {
            Debug.Log("Marching Cubes GPU: Start() called");
            if (N % 8 != 0)
                throw new System.ArgumentException("N must be divisible by 8");

            int densityWidth = N + 1 + P * 2;
            int voxelCount = densityWidth * densityWidth * densityWidth;
            m_noiseBuffer = new ComputeBuffer(voxelCount, sizeof(float));

            m_meshBuffer = new ComputeBuffer(SIZE, sizeof(float) * 7, ComputeBufferType.Default);
            m_vertexCountBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.Raw);

            m_cubeEdgeFlags = new ComputeBuffer(256, sizeof(int));
            m_cubeEdgeFlags.SetData(MarchingCubesTables.CubeEdgeFlags);

            m_triangleConnectionTable = new ComputeBuffer(256 * 16, sizeof(int));
            m_triangleConnectionTable.SetData(MarchingCubesTables.TriangleConnectionTable);

            // Initialize the flat density array using Perlin noise (or your desired method)
            float[] flatDensity = new float[voxelCount];
            float frequency = 0.005f;
            float groundVariation = N * groundVariationMultiplier;
            float baseGroundHeight = N * baseGroundHeightMultiplier;

            // Get deterministic terrain offsets
            Random.InitState(m_seed);
            float offsetX = Random.Range(0f, 1000f);
            float offsetZ = Random.Range(0f, 1000f);

            // Get deterministic water and CO2 offsets without Random
            float waterOffsetX = Mathf.Abs(Mathf.Sin((m_seed + 100) * 12.9898f) * 43758.5453f) % 1000f;
            float waterOffsetZ = Mathf.Abs(Mathf.Sin((m_seed + 200) * 78.233f) * 43758.5453f) % 1000f;
            float co2OffsetX = Mathf.Abs(Mathf.Sin((m_seed + 300) * 45.164f) * 43758.5453f) % 1000f;
            float co2OffsetZ = Mathf.Abs(Mathf.Sin((m_seed + 400) * 31.416f) * 43758.5453f) % 1000f;

            //waterGradient = new float[voxelCount];
            //co2Gradient = new float[voxelCount];

            for (int z = 0; z < densityWidth; z++)
            {
                for (int y = 0; y < densityWidth; y++)
                {
                    for (int x = 0; x < densityWidth; x++)
                    {
                        int i = (x) + (y) * densityWidth + (z) * densityWidth * densityWidth;
                        float worldY = ChunkWorldPosition.y + (y - P);

                        float perlin = Mathf.PerlinNoise(
                            ((x - P) + ChunkCoord.x * N) * frequency + offsetX,
                            ((z - P) + ChunkCoord.z * N) * frequency + offsetZ
                        );

                        float surfaceHeight = baseGroundHeight + (perlin * groundVariation);
                        float density = surfaceHeight - worldY;

                        if (worldY < surfaceHeight)
                        {
                            density = Mathf.Min(density, 1.0f);
                        }

                        flatDensity[i] = density;

                        //waterGradient[i] = Mathf.Clamp01(1.0f - (worldY / (N * 5f)));
                        //co2Gradient[i] = Mathf.Clamp01(worldY / (N * 5f));
                    }
                }
            }

            m_noiseBuffer.SetData(flatDensity);
            cpuDensity = flatDensity;
            m_vertexCountBuffer.SetData(new int[] { 0 });
            DispatchNormals();
            DispatchMesh();

            Debug.Log("Marching Cubes GPU: Dispatched all shaders");
        }
        /*
        public float SampleWaterAtWorldPosition(Vector3 worldPos)
        {
            Vector3 localPos = worldPos - ChunkWorldPosition;
            int padded = N + 1 + P * 2;
            int x = Mathf.FloorToInt(localPos.x) + P;
            int y = Mathf.FloorToInt(localPos.y) + P;
            int z = Mathf.FloorToInt(localPos.z) + P;

            if (x < 0 || y < 0 || z < 0 || x >= padded || y >= padded || z >= padded)
                return 0f;

            int index = x + y * padded + z * padded * padded;

            if (waterGradient == null || index < 0 || index >= waterGradient.Length)
                return 0f;

            return waterGradient[index];
        }

        public float SampleCO2AtWorldPosition(Vector3 worldPos)
        {
            Vector3 localPos = worldPos - ChunkWorldPosition;
            int padded = N + 1 + P * 2;
            int x = Mathf.FloorToInt(localPos.x) + P;
            int y = Mathf.FloorToInt(localPos.y) + P;
            int z = Mathf.FloorToInt(localPos.z) + P;

            if (x < 0 || y < 0 || z < 0 || x >= padded || y >= padded || z >= padded)
                return 0f;

            int index = x + y * padded + z * padded * padded;

            if (co2Gradient == null || index < 0 || index >= co2Gradient.Length)
                return 0f;

            return co2Gradient[index];
        }
        */

        void Awake()
        {
            // Attempt to auto-assign if they're missing
            if (!collisionMeshFilter)
                collisionMeshFilter = GetComponent<MeshFilter>()
                                      ?? gameObject.AddComponent<MeshFilter>();
        }
        void DebugDrawVoxel(Vector3 position)
        {
            debugVoxelPositions.Add(position);
        }
        void OnDrawGizmos()
        {
            if (debugVoxelPositions == null)
                return;

            Gizmos.color = Color.red;
            foreach (var pos in debugVoxelPositions)
            {
                Gizmos.DrawCube(pos, Vector3.one * 0.5f);
            }
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
            float maxDigRadius = 10.0f;
            radius = Mathf.Min(radius, maxDigRadius);

            int kernel = m_digShader.FindKernel("CSMain");
            m_digShader.SetFloat("_DigRadius", radius);
            m_digShader.SetVector("_DigPosition", local);
            m_digShader.SetBuffer(kernel, "_Noise", m_noiseBuffer);

            int paddedWidth = N + 1 + P * 2;
            int groups = Mathf.CeilToInt((float)paddedWidth / 8f);
            m_digShader.Dispatch(kernel, groups, groups, groups);

            // After modifying the density, update normals, remesh, and update collider
            Remesh();
        }
        public void ApplyDigAtWorld(Vector3 worldHitPosition, float radius)
        {
            Vector3 localPos = worldHitPosition - ChunkWorldPosition;

            float maxDigRadius = 10.0f;
            radius = Mathf.Min(radius, maxDigRadius);

            int kernel = m_digShader.FindKernel("CSMain");
            int paddedWidth = N + 1 + P * 2;

            m_digShader.SetInt("_Width", paddedWidth);
            m_digShader.SetInt("_Height", paddedWidth);
            m_digShader.SetInt("_Depth", paddedWidth);

            m_digShader.SetFloat("_DigRadius", radius);
            m_digShader.SetVector("_DigPosition", localPos);
            m_digShader.SetBuffer(kernel, "_Noise", m_noiseBuffer);

            int groups = Mathf.CeilToInt((float)paddedWidth / 8f);
            m_digShader.Dispatch(kernel, groups, groups, groups);

            m_noiseBuffer.GetData(cpuDensity);

            // Instead of full remesh: remesh a **small local mesh** if possible
            RemeshSmallArea(localPos, radius);

            dirtyVoxels += Mathf.FloorToInt(Mathf.PI * Mathf.Pow(radius, 3)); // Still track dirtyness
        }
        void RemeshSmallArea(Vector3 localCenter, float radius)
        {
            // (Optional) You can implement *partial remesh* in your compute shader for extra performance.
            // But for now: just normal Remesh for now.
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

        public void Remesh()
        {
            DispatchNormals();
            DispatchMesh();
            UpdateCollisionMesh();  // NEW: Update collider after remeshing
        }

        // NEW: Create a collision mesh from the GPU-generated mesh data
        void UpdateCollisionMesh()
        {
            //Debug.Log($"Updating collision mesh with vertexCount = {m_actualVertexCount}");
            if (m_actualVertexCount <= 0)
                return;

            // Our vertex layout: Vector4 (position) and Vector3 (normal) = 7 floats per vertex.
            float[] meshData = new float[m_actualVertexCount * 7];
            m_meshBuffer.GetData(meshData, 0, 0, meshData.Length);

            Vector3[] vertices = new Vector3[m_actualVertexCount];
            int[] triangles = new int[m_actualVertexCount];

            for (int i = 0; i < m_actualVertexCount; i++)
            {
                vertices[i] = new Vector3(
                    meshData[i * 7],
                    meshData[i * 7 + 1],
                    meshData[i * 7 + 2]
                );
                // Assuming that the vertices form triangles consecutively (every 3 vertices is a triangle).
                triangles[i] = i;
            }

            Mesh collisionMesh = new Mesh();
            collisionMesh.vertices = vertices;
            collisionMesh.triangles = triangles;
            collisionMesh.RecalculateNormals();

            if (collisionMeshFilter != null)
                collisionMeshFilter.mesh = collisionMesh;

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

            //Debug.Log($"[GPU Draw] Vertex Count: {m_actualVertexCount}");
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

            //Debug.Log($"[CopyBorderFrom] From: {other.ChunkCoord} → {ChunkCoord}, axis: {faceAxis}, source: {sourceCoord}, target: {targetCoord}");
        }
        public float SampleDensityAtWorldPosition(Vector3 worldPos)
        {
            Vector3 localPos = worldPos - ChunkWorldPosition;
            int padded = N + 1 + P * 2;
            int x = Mathf.FloorToInt(localPos.x) + P;
            int y = Mathf.FloorToInt(localPos.y) + P;
            int z = Mathf.FloorToInt(localPos.z) + P;

            if (x < 0 || y < 0 || z < 0 || x >= padded || y >= padded || z >= padded)
                return -1f;

            int index = x + y * padded + z * padded * padded;

            if (cpuDensity == null || index < 0 || index >= cpuDensity.Length)
                return -1f;

            float d = cpuDensity[index];

            //Debug.Log($"[Density] {ChunkCoord} sample at {worldPos} = {d}");

            return d;
        }
    }
}
