using MarchingCubesGPUProject;
using UnityEngine;

public class PlayerController : MonoBehaviour
{
    public float moveSpeed = 5f;
    public float flySpeed = 10f;
    public float digRadius = 2f;
    public MarchingCubesGPU marchingCubesGPU;// Reference to the VoxelTerrainGenerator
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
        if (marchingCubesGPU == null)
        {
            marchingCubesGPU = FindFirstObjectByType<MarchingCubesGPU>();
        }
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

        Ray ray = playerCamera.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, 100f))
        {
            Vector3 safeHitPoint = hit.point - ray.direction * 0.01f;

            MarchingCubesGPU chunk = chunkManager.GetChunkAtWorldPosition(safeHitPoint);
            if (chunk != null)
            {
                GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                marker.transform.position = safeHitPoint;
                marker.transform.localScale = Vector3.one * 0.5f;
                marker.GetComponent<Renderer>().material.color = Color.red;
                Destroy(marker, 3f);

                chunkManager.DigAtWorldPosition(safeHitPoint, digRadius);
                Debug.Log($"Digging at: {safeHitPoint} in chunk: {chunk.ChunkCoord}");
            }
        }
        else
        {
            Debug.Log("Nothing hit by Physics.Raycast.");
        }
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

}
