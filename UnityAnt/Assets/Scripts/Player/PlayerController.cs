using MarchingCubesGPUProject;
using UnityEngine;

public class PlayerController : MonoBehaviour
{
    public float moveSpeed = 5f;
    public float flySpeed = 10f;
    public float digRadius = 2f;
    public ChunkManager chunkManager;
    private Rigidbody rb;
    private Camera playerCamera;
    private float rotationX = 0f;  // Variable to track vertical camera rotation

    private bool cursorUnlocked = false;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        playerCamera = Camera.main;

        // Automatically assign the VoxelTerrainGenerator if not set in the Inspector
        if (chunkManager == null)
        {
            chunkManager = FindFirstObjectByType<ChunkManager>();
        }

        TeleportAboveWorldCenter();
    }

    void Update()
    {
        // Toggle cursor lock/unlock with Escape
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            cursorUnlocked = !cursorUnlocked;
            Cursor.lockState = cursorUnlocked ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = cursorUnlocked;
        }

        if (!cursorUnlocked)
        {
            HandleMovement();
            CameraMovement();

            if (Input.GetMouseButtonDown(0))
            {
                DigAtMousePosition();
            }
        }
    }
    void HandleMovement()
    {
        float moveX = Input.GetAxis("Horizontal");
        float moveY = Input.GetAxis("Vertical");
        float moveZ = 0f;

        if (Input.GetKey(KeyCode.Q)) moveZ = -1f;
        if (Input.GetKey(KeyCode.E)) moveZ = 1f;

        Vector3 moveDirection = transform.right * moveX + transform.up * moveZ + transform.forward * moveY;
        rb.linearVelocity = moveDirection * flySpeed;
    }

    private float lastDigTime;
    private float digCooldown = 0.2f;

    void DigAtMousePosition()
    {
        if (Time.time - lastDigTime < digCooldown) return;
        lastDigTime = Time.time;

        Ray ray = playerCamera.ScreenPointToRay(Input.mousePosition);
        Vector3 origin = ray.origin;
        Vector3 dir = ray.direction.normalized;

        float maxDistance = 100f;
        float traveled = 0f;

        Vector3 pos = origin;
        Vector3Int voxel = new Vector3Int(
            Mathf.FloorToInt(pos.x),
            Mathf.FloorToInt(pos.y),
            Mathf.FloorToInt(pos.z)
        );

        Vector3Int step = new Vector3Int(
            dir.x > 0 ? 1 : -1,
            dir.y > 0 ? 1 : -1,
            dir.z > 0 ? 1 : -1
        );

        Vector3 nextVoxelBoundary = new Vector3(
            voxel.x + (step.x > 0 ? 1 : 0),
            voxel.y + (step.y > 0 ? 1 : 0),
            voxel.z + (step.z > 0 ? 1 : 0)
        );

        Vector3 tMax = new Vector3(
            (nextVoxelBoundary.x - pos.x) / dir.x,
            (nextVoxelBoundary.y - pos.y) / dir.y,
            (nextVoxelBoundary.z - pos.z) / dir.z
        );

        Vector3 tDelta = new Vector3(
            Mathf.Abs(1f / dir.x),
            Mathf.Abs(1f / dir.y),
            Mathf.Abs(1f / dir.z)
        );

        for (int i = 0; i < 512 && traveled < maxDistance; i++)
        {
            Vector3 worldVoxelPos = new Vector3(voxel.x, voxel.y, voxel.z);

            // 🚀 Always find correct chunk for this voxel
            MarchingCubesGPU chunk = chunkManager.GetChunkAtWorldPosition(worldVoxelPos);

            if (chunk != null)
            {
                float density = chunk.SampleDensityAtWorldPosition(worldVoxelPos);
                if (density > 0.0f)
                {
                    chunkManager.DigAtWorldPosition(worldVoxelPos, digRadius);
                    Debug.Log($"[DDA Dig] Hit solid voxel at {worldVoxelPos}");
                    return;
                }
            }

            // Advance to next voxel
            if (tMax.x < tMax.y)
            {
                if (tMax.x < tMax.z)
                {
                    voxel.x += step.x;
                    traveled = tMax.x;
                    tMax.x += tDelta.x;
                }
                else
                {
                    voxel.z += step.z;
                    traveled = tMax.z;
                    tMax.z += tDelta.z;
                }
            }
            else
            {
                if (tMax.y < tMax.z)
                {
                    voxel.y += step.y;
                    traveled = tMax.y;
                    tMax.y += tDelta.y;
                }
                else
                {
                    voxel.z += step.z;
                    traveled = tMax.z;
                    tMax.z += tDelta.z;
                }
            }
        }

        Debug.Log("[DDA Dig] No voxel found after DDA.");
    }

    void CameraMovement()
    {
        float mouseX = Input.GetAxis("Mouse X") * 2f;
        float mouseY = -Input.GetAxis("Mouse Y") * 2f;

        transform.Rotate(Vector3.up, mouseX);

        rotationX += mouseY;
        rotationX = Mathf.Clamp(rotationX, -80f, 80f);
        playerCamera.transform.localRotation = Quaternion.Euler(rotationX, 0f, 0f);
    }

    private ChunkManager GetChunkManager()
    {
        return chunkManager;
    }
    void TeleportAboveWorldCenter()
    {
        if (chunkManager == null)
            return;

        // Calculate the center of the world
        float centerX = (chunkManager.worldSizeX * ChunkManager.chunkSize) * 0.5f;
        float centerZ = (chunkManager.worldSizeZ * ChunkManager.chunkSize) * 0.5f;

        // Calculate the top of the world (height)
        float topY = chunkManager.worldSizeY * ChunkManager.chunkSize;

        // Set player position slightly above the world
        Vector3 spawnPos = new Vector3(centerX, topY + 10f, centerZ);
        transform.position = spawnPos;

        // Also reset the Rigidbody velocity (so you don't fall weirdly)
        if (rb != null)
            rb.linearVelocity = Vector3.zero;

        Debug.Log($"[PlayerController] Teleported player to {spawnPos}");
    }

}
