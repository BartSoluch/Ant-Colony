using UnityEngine;

public class PlayerController : MonoBehaviour
{
    public float moveSpeed = 5f;
    public float flySpeed = 10f;
    public float digRadius = 2f;
    public VoxelWorld voxelWorld;  // Reference to the VoxelTerrainGenerator

    private Rigidbody rb;
    private Camera playerCamera;
    private float rotationX = 0f;  // Variable to track vertical camera rotation

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        playerCamera = Camera.main;

        // Automatically assign the VoxelTerrainGenerator if not set in the Inspector
        if (voxelWorld == null)
        {
            voxelWorld = FindFirstObjectByType<VoxelWorld>();

            // If we couldn't find it, log an error
            if (voxelWorld == null)
            {
                Debug.LogError("VoxelWorld not found! Please make sure it is in the scene.");
            }
        }
    }

    void Update()
    {
        // Fly movement (WASD for forward/backward/left/right, QE for up/down)
        float moveX = Input.GetAxis("Horizontal");
        float moveY = Input.GetAxis("Vertical");
        float moveZ = 0f;

        if (Input.GetKey(KeyCode.Q)) moveZ = -1f;  // Move down (Q)
        if (Input.GetKey(KeyCode.E)) moveZ = 1f;   // Move up (E)

        Vector3 moveDirection = transform.right * moveX + transform.up * moveZ + transform.forward * moveY;
        rb.linearVelocity = moveDirection * flySpeed;  // Use linearVelocity as per Unity 6

        // Digging (Left-click to dig at the mouse position)
        if (Input.GetMouseButtonDown(0))
        {
            DigAtMousePosition();
        }

        // Camera movement and rotation
        CameraMovement();
    }

    void DigAtMousePosition()
    {
        // Check if voxelTerrainGenerator is null
        if (voxelWorld == null)
        {
            Debug.LogError("VoxelTerrainGenerator is not assigned!");
            return;
        }

        // Raycasting to get the mouse position
        Ray ray = playerCamera.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit))
        {
            // Get the position where the mouse is pointing at
            Vector3 hitPosition = hit.point;

            // Log the hit position
            Debug.Log($"Mouse clicked at position: {hitPosition}");

            // Call the Dig method to dig at the hit position
            voxelWorld.Dig(hitPosition, digRadius);
        }
        else
        {
            Debug.Log("Raycast did not hit anything.");
        }
    }

    void CameraMovement()
    {
        // Camera movement using WASD or arrow keys
        float moveX = Input.GetAxis("Horizontal");
        float moveY = Input.GetAxis("Vertical");
        float moveZ = 0f;

        if (Input.GetKey(KeyCode.Q)) moveZ = -1f;  // Move down (Q)
        if (Input.GetKey(KeyCode.E)) moveZ = 1f;   // Move up (E)

        Vector3 moveDirection = new Vector3(moveX, moveZ, moveY) * moveSpeed * Time.deltaTime;
        transform.Translate(moveDirection);

        // Camera rotation using mouse (left-right, up-down)
        float mouseX = Input.GetAxis("Mouse X") * 2f;
        float mouseY = -Input.GetAxis("Mouse Y") * 2f;

        // Rotate player for horizontal (left-right) look
        transform.Rotate(Vector3.up, mouseX);

        // Rotate camera for vertical (up-down) look
        rotationX += mouseY;
        rotationX = Mathf.Clamp(rotationX, -80f, 80f); // Limit vertical rotation to prevent full rotation
        playerCamera.transform.localRotation = Quaternion.Euler(rotationX, 0f, 0f);
    }
}
