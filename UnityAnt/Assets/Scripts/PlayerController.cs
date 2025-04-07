using MarchingCubesGPUProject;
using UnityEngine;

public class PlayerController : MonoBehaviour
{
    public float moveSpeed = 5f;
    public float flySpeed = 10f;
    public float digRadius = 2f;
    public MarchingCubesGPU marchingCubesGPU;// Reference to the VoxelTerrainGenerator

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

    void DigAtMousePosition()
    {
        Ray ray = playerCamera.ScreenPointToRay(Input.mousePosition);

        if (marchingCubesGPU.RaycastVoxel(ray.origin, ray.direction, 100f, out Vector3 hitPos))
        {
            marchingCubesGPU.DigAt(hitPos, digRadius);
        }
        else
        {
            Debug.Log("No solid voxel hit.");
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
