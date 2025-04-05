using UnityEngine;

public class AntAgent : MonoBehaviour
{
    public enum State { Roaming, Digging }
    public State currentState = State.Roaming;

    [Header("Dig Settings")]
    public float digRadius = 1.2f;
    public float digCooldown = 2f;

    [Header("Ant Settings")]
    public float moveSpeed = 1.5f;
    public float pheromoneDepositAmount = 1f;
    public float directionUpdateCooldown = 2f;

    private float lastDigTime;
    private float lastDirectionUpdateTime;
    private Vector3 currentDirection;
    private Vector3 smoothedNormal = Vector3.up;

    private Animator animator;

    void Start()
    {
        PickNewDirection();
        animator = GetComponentInChildren<Animator>();
    }

    void Update()
    {
        switch (currentState)
        {
            case State.Roaming:
                Roam();
                break;

            case State.Digging:
                TryDig();
                break;
        }

        if (animator != null)
        {
            float animSpeed = currentState == State.Roaming ? 1f : 0f;
            animator.SetFloat("Speed", animSpeed);
        }

        ApplyStickyGravity();

    }
    void Roam()
    {
        if (!FindClimbableSurface(out Vector3 surfacePoint, out Vector3 surfaceNormal))
        {
            // No terrain detected — don't move this frame
            return;
        }

        // Smooth the surface normal for stable rotation
        smoothedNormal = Vector3.Slerp(smoothedNormal, surfaceNormal, Time.deltaTime * 8f);

        // Use the current direction projected onto the surface for proper climbing
        Vector3 move = Vector3.ProjectOnPlane(currentDirection, smoothedNormal).normalized;

        // Move the ant forward
        transform.position += move * moveSpeed * Time.deltaTime;

        // Keep ant snug on the surface
        transform.position = Vector3.Lerp(transform.position, surfacePoint + surfaceNormal * 0.02f, Time.deltaTime * 10f);

        // Rotate to align with movement and surface
        Quaternion targetRot = Quaternion.LookRotation(move, smoothedNormal);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * 8f);

        // Occasionally pick a new random direction
        if (Time.time - lastDirectionUpdateTime > directionUpdateCooldown)
        {
            PickNewDirection();
            lastDirectionUpdateTime = Time.time;
        }

        // Look for nearby pheromone dig target
        Vector3Int bestDigTarget = GetBestDigTarget();
        if (bestDigTarget != Vector3Int.zero)
        {
            currentDirection = ((Vector3)(bestDigTarget - Vector3Int.FloorToInt(transform.position))).normalized;
            currentState = State.Digging;
        }
    }

    bool FindClimbableSurface(out Vector3 hitPoint, out Vector3 hitNormal)
    {
        Vector3 origin = transform.position;
        float radius = 0.4f;
        float distance = 1.2f;

        int layerMask = ~LayerMask.GetMask("Ant");

        Vector3[] directions = {
        -transform.up, transform.forward, -transform.forward,
        transform.right, -transform.right, transform.up
    };

        foreach (Vector3 dir in directions)
        {
            if (Physics.SphereCast(origin, radius, dir, out RaycastHit hit, distance, layerMask))
            {
                hitPoint = hit.point;
                hitNormal = hit.normal;
                return true;
            }
        }

        // Fallback: try a downward ray
        if (Physics.Raycast(origin + Vector3.up * 0.5f, Vector3.down, out RaycastHit fallbackHit, 2f, layerMask))
        {
            hitPoint = fallbackHit.point;
            hitNormal = fallbackHit.normal;
            return true;
        }

        // Still nothing — ant is floating
        hitPoint = origin;
        hitNormal = Vector3.up;
        return false;
    }

    void ApplyStickyGravity()
    {
        Vector3 origin = transform.position;
        Vector3 down = -transform.up;
        float stickDistance = 1f;
        float fallSpeed = 2f;

        int layerMask = ~LayerMask.GetMask("Ant"); // ignore other ants

        // Try to find terrain underneath (relative "down")
        if (Physics.Raycast(origin, down, out RaycastHit hit, stickDistance, layerMask))
        {
            // Gently pull toward surface to keep grounded
            Vector3 targetPos = hit.point + hit.normal * 0.05f;
            transform.position = Vector3.Lerp(transform.position, targetPos, Time.deltaTime * 10f);
        }
        else
        {
            // No terrain: simulate falling down
            transform.position += Vector3.down * fallSpeed * Time.deltaTime;
        }
    }


    void TryDig()
    {
        if (Time.time - lastDigTime < digCooldown)
            return;

        lastDigTime = Time.time;

        // Start position for the ray
        int terrainMask = LayerMask.GetMask("Terrain");  // Ensure Terrain is assigned to solid voxels

        Vector3 origin = transform.position + transform.up * 0.1f;
        Vector3 direction = transform.forward;

        if (Physics.Raycast(origin, direction, out RaycastHit hit, 3f, terrainMask))
        {
            Vector3 digPos = hit.point;
            Debug.Log($"🛠️ {name} digging at {digPos} (Hit: {hit.collider.name})");

            VoxelWorld.Instance.TryDigAt(digPos, digRadius);
            PheromoneField.Instance.Deposit(digPos, pheromoneDepositAmount);

        }
        else
        {
            Vector3 fallbackPos = transform.position + transform.forward * 0.6f;
            Debug.LogWarning($"⚠️ Ant {name} tried to dig but hit nothing. Using fallback at {fallbackPos}");

            VoxelWorld.Instance.TryDigAt(fallbackPos, digRadius * 0.8f);
            PheromoneField.Instance.Deposit(fallbackPos, pheromoneDepositAmount * 0.5f);
        }
        currentState = State.Roaming;
    }


    void PickNewDirection()
    {
        float angle = Random.Range(0f, 360f);
        currentDirection = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)).normalized;
        transform.rotation = Quaternion.LookRotation(currentDirection);
    }

    Vector3Int GetBestDigTarget()
    {
        Vector3Int current = Vector3Int.FloorToInt(transform.position);
        float bestPheromone = 0f;
        Vector3Int bestOffset = Vector3Int.zero;

        for (int x = -1; x <= 1; x++)
            for (int y = -1; y <= 0; y++)
                for (int z = -1; z <= 1; z++)
                {
                    Vector3Int offset = new Vector3Int(x, y, z);
                    Vector3Int check = current + offset;

                    float phero = PheromoneField.Instance.Get(check);
                    if (phero > bestPheromone)
                    {
                        bestPheromone = phero;
                        bestOffset = offset;
                    }
                }

        return bestPheromone > 0f ? current + bestOffset : Vector3Int.zero;
    }
}
