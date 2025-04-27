using UnityEngine;

public class AntAgent : MonoBehaviour
{
    public enum State { Roaming, Digging, Expanding }

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
            case State.Roaming: Roam(); break;
            case State.Digging: TryDig(); break;
            case State.Expanding: ExpandChamber(); break;
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
            return;
        }

        smoothedNormal = Vector3.Slerp(smoothedNormal, surfaceNormal, Time.deltaTime * 8f);
        Vector3 move = Vector3.ProjectOnPlane(currentDirection, smoothedNormal).normalized;

        transform.position += move * moveSpeed * Time.deltaTime;
        transform.position = Vector3.Lerp(transform.position, surfacePoint + surfaceNormal * 0.02f, Time.deltaTime * 10f);
        Quaternion targetRot = Quaternion.LookRotation(move, smoothedNormal);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * 8f);

        // 💧 Leave trail pheromone as you walk
        PheromoneField.Instance.DepositTrail(transform.position, pheromoneDepositAmount * 0.25f);

        if (Time.time - lastDirectionUpdateTime > directionUpdateCooldown)
        {
            PickNewDirection();
            lastDirectionUpdateTime = Time.time;
        }

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

        int terrainMask = LayerMask.GetMask("Terrain");  // Ensure the Terrain layer is correct.
        Vector3 digDirection = GetBestDigDirection();
        Vector3 origin = transform.position + transform.up * 0.1f;

        // Get the ChunkManager instance (ensure your ChunkManager is active in the scene)
        ChunkManager chunkManager = FindFirstObjectByType<ChunkManager>(); // Or FindObjectOfType<ChunkManager>()
        if (chunkManager == null)
        {
            Debug.LogError("ChunkManager not found. Aborting dig.");
            return;
        }

        if (Physics.Raycast(origin, digDirection, out RaycastHit hit, 3f, terrainMask))
        {
            Vector3 digPos = hit.point;
            // Use the GPU-based digging via ChunkManager
            chunkManager.DigAtWorldPosition(digPos, digRadius);
            PheromoneField.Instance.DepositDig(digPos, pheromoneDepositAmount);
        }
        else
        {
            Vector3 fallbackPos = transform.position + transform.forward * 0.6f;
            Debug.LogWarning($"⚠️ Ant {name} tried to dig but hit nothing. Using fallback at {fallbackPos}");
            chunkManager.DigAtWorldPosition(fallbackPos, digRadius * 0.8f);
            PheromoneField.Instance.DepositDig(fallbackPos, pheromoneDepositAmount * 0.5f);
        }

        float digPhero = PheromoneField.Instance.GetDig(Vector3Int.FloorToInt(transform.position));
        if (digPhero > 2f && transform.position.y < 0f)
        {
            currentState = State.Expanding;
            return;
        }
        currentState = State.Roaming;
    }

    void ExpandChamber()
    {
        Vector3 origin = transform.position + transform.up * 0.1f;

        // Try to dig in a random horizontal direction
        Vector3[] directions = {
        transform.right, -transform.right,
        transform.forward, -transform.forward
    };

        // Acquire the ChunkManager instance
        ChunkManager chunkManager = FindFirstObjectByType<ChunkManager>(); // Or use FindObjectOfType<ChunkManager>()
        if (chunkManager == null)
        {
            Debug.LogError("ChunkManager not found. Aborting chamber expansion dig.");
            currentState = State.Roaming;
            return;
        }

        Vector3 direction = directions[Random.Range(0, directions.Length)];
        int terrainMask = LayerMask.GetMask("Terrain");

        if (Physics.Raycast(origin, direction, out RaycastHit hit, 2f, terrainMask))
        {
            Vector3 digPos = hit.point;
            // Use the GPU dig method via ChunkManager
            chunkManager.DigAtWorldPosition(digPos, digRadius * 1.5f);
            PheromoneField.Instance.DepositDig(digPos, pheromoneDepositAmount * 1.5f);
            currentState = State.Roaming;
        }
        else
        {
            // Fallback if we couldn't expand
            currentState = State.Roaming;
        }
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
        float bestScore = 0f;
        Vector3Int bestTarget = Vector3Int.zero;

        // Leafcutter ant-style offsets: downward + horizontal tunneling
        Vector3Int[] offsets = {
        new(0, -1, 0), new(1, -1, 0), new(-1, -1, 0),
        new(0, -1, 1), new(0, -1, -1),
        new(1, 0, 0), new(-1, 0, 0),
        new(0, 0, 1), new(0, 0, -1),
    };

        foreach (Vector3Int offset in offsets)
        {
            Vector3Int check = current + offset;

            float digPhero = PheromoneField.Instance.GetDig(check);
            float trailPhero = PheromoneField.Instance.GetTrail(check);

            // Prefer places with dig pheromone AND low trail (i.e. less crowded)
            float score = digPhero + 0.1f - trailPhero * 0.5f;

            // Bonus score if going downward
            if (offset.y < 0) score += 0.2f;

            // Bonus for mid-depths to form chambers
            if (check.y < 2 && check.y > -10) score += 0.3f;

            if (score > bestScore)
            {
                bestScore = score;
                bestTarget = check;
            }
        }

        return bestScore > 0.15f ? bestTarget : Vector3Int.zero;
    }

    Vector3 GetBestDigDirection()
    {
        Vector3Int current = Vector3Int.FloorToInt(transform.position);
        Vector3 bestDirection = Vector3.zero;
        float bestValue = 0f;

        for (int x = -1; x <= 1; x++)
            for (int y = -1; y <= 0; y++) // Prefer same level or downward
                for (int z = -1; z <= 1; z++)
                {
                    Vector3Int offset = new Vector3Int(x, y, z);
                    Vector3Int neighbor = current + offset;
                    float pheromone = PheromoneField.Instance.GetDig(neighbor);

                    if (pheromone > bestValue)
                    {
                        bestValue = pheromone;
                        bestDirection = offset;
                    }
                }

        return bestDirection != Vector3.zero ? bestDirection.normalized : transform.forward;
    }

}
