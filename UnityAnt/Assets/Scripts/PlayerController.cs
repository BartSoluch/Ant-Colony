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

        // Cast a ray from the camera through the mouse position.
        Ray ray = playerCamera.ScreenPointToRay(Input.mousePosition);
        float maxDistance = 100f;

        // (Use a 3D DDA approach to find the first chunk that returns a voxel hit.)
        int cs = ChunkManager.chunkSize;
        Vector3 rayOrigin = ray.origin;
        Vector3 rayDir = ray.direction.normalized;
        Vector3 currentPos = rayOrigin;
        Vector3Int chunkCoord = new Vector3Int(
            Mathf.FloorToInt(currentPos.x / cs),
            Mathf.FloorToInt(currentPos.y / cs),
            Mathf.FloorToInt(currentPos.z / cs)
        );
        Vector3Int step = new Vector3Int(
            rayDir.x > 0 ? 1 : -1,
            rayDir.y > 0 ? 1 : -1,
            rayDir.z > 0 ? 1 : -1
        );
        Vector3 nextBoundary = new Vector3(
            (chunkCoord.x + (step.x > 0 ? 1 : 0)) * cs,
            (chunkCoord.y + (step.y > 0 ? 1 : 0)) * cs,
            (chunkCoord.z + (step.z > 0 ? 1 : 0)) * cs
        );
        Vector3 tMax = new Vector3(
            (nextBoundary.x - rayOrigin.x) / rayDir.x,
            (nextBoundary.y - rayOrigin.y) / rayDir.y,
            (nextBoundary.z - rayOrigin.z) / rayDir.z
        );
        Vector3 tDelta = new Vector3(
            cs / Mathf.Abs(rayDir.x),
            cs / Mathf.Abs(rayDir.y),
            cs / Mathf.Abs(rayDir.z)
        );

        float t = 0f;
        for (int i = 0; i < 256 && t < maxDistance; i++)
        {
            MarchingCubesGPU chunk = chunkManager.GetChunk(chunkCoord.x, chunkCoord.y, chunkCoord.z);
            if (chunk != null && chunk.RaymarchDig(rayOrigin, rayDir, maxDistance, out Vector3 hitPoint))
            {
                // Found a voxel hit in this chunk.
                chunkManager.DigAtWorldPosition(hitPoint, digRadius);
                Debug.Log($"[GPU Dig] Hit at: {hitPoint}");
                return;
            }
            // Step to the next chunk along the ray (using 3D DDA)
            if (tMax.x < tMax.y)
            {
                if (tMax.x < tMax.z)
                {
                    chunkCoord.x += step.x;
                    t = tMax.x;
                    tMax.x += tDelta.x;
                }
                else
                {
                    chunkCoord.z += step.z;
                    t = tMax.z;
                    tMax.z += tDelta.z;
                }
            }
            else
            {
                if (tMax.y < tMax.z)
                {
                    chunkCoord.y += step.y;
                    t = tMax.y;
                    tMax.y += tDelta.y;
                }
                else
                {
                    chunkCoord.z += step.z;
                    t = tMax.z;
                    tMax.z += tDelta.z;
                }
            }
        }
        Debug.Log("[GPU Dig] No voxel hit");
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
}
