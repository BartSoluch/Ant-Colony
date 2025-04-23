using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class AntAgent : MonoBehaviour
{
    public enum Role { Digger, Scout }
    public Role role = Role.Digger;

    public enum State { Roaming, Digging, Expanding }
    public State currentState = State.Roaming;

    [Header("Dig Settings")]
    public float digRadius = 1.2f;
    public float digCooldown = 2f;

    [Header("Ant Settings")]
    public float moveSpeed = 1.5f;
    public float pheromoneDepositAmount = 1f;
    public float directionUpdateCooldown = 2f;

    [Header("Climb & Stick Settings")]
    public float stickSpeed = 10f;
    public float rotationSpeed = 8f;
    public float fallSpeed = 2f;
    public float stickDistance = 1.2f;
    public float groundOffset = 0.5f;
    private float sphereRadius;

    private float lastDigTime;
    private float lastDirectionUpdateTime;
    private Vector3 currentDirection;
    private Vector3 smoothedNormal = Vector3.up;

    private Animator animator;
    Rigidbody rb;
    SphereCollider sphereCol;

    void Awake()
    {
        sphereCol = GetComponent<SphereCollider>();
        rb = GetComponent<Rigidbody>();
        rb.useGravity = false;
        rb.isKinematic = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        sphereRadius = sphereCol.radius * transform.localScale.y;
    }

    void Start()
    {
        PickNewDirection();
        animator = GetComponentInChildren<Animator>();
    }

    void Update()
    {
        if (role == Role.Scout)
            RoamScout();
        else
        {
            switch (currentState)
            {
                case State.Roaming: Roam(); break;
                case State.Digging: TryDig(); break;
                case State.Expanding: ExpandChamber(); break;
            }
        }

        if (animator != null)
        {
            float animSpeed = (currentState == State.Roaming) ? 1f : 0f;
            animator.SetFloat("Speed", animSpeed);
        }

        // Always apply stickiness last so no drift occurs
        ApplyStickyGravity();
    }

    void Roam()
    {
        if (!FindClimbableSurface(out Vector3 surfacePoint, out Vector3 surfaceNormal))
            return;

        smoothedNormal = Vector3.Slerp(smoothedNormal, surfaceNormal, Time.deltaTime * rotationSpeed);
        Vector3 move = Vector3.ProjectOnPlane(currentDirection, smoothedNormal).normalized;
        transform.position += move * moveSpeed * Time.deltaTime;

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

    void RoamScout()
    {
        if (!FindClimbableSurface(out Vector3 surfacePoint, out Vector3 surfaceNormal))
            return;

        smoothedNormal = Vector3.Slerp(smoothedNormal, surfaceNormal, Time.deltaTime * rotationSpeed);
        Vector3 move = Vector3.ProjectOnPlane(currentDirection, smoothedNormal).normalized;
        transform.position += move * moveSpeed * Time.deltaTime;

        PheromoneField.Instance.DepositTrail(transform.position, pheromoneDepositAmount * 0.25f);
        if (Time.time - lastDirectionUpdateTime > directionUpdateCooldown)
        {
            PickNewDirection();
            lastDirectionUpdateTime = Time.time;
        }
    }

    bool FindClimbableSurface(out Vector3 hitPoint, out Vector3 hitNormal)
    {
        Vector3 origin = transform.position;
        int layerMask = ~LayerMask.GetMask("Ant");
        Vector3[] dirs = {
        transform.up, -transform.up,
        transform.right, -transform.right,
        transform.forward, -transform.forward
        };

        foreach (var dir in dirs)
        {
            if (Physics.SphereCast(origin, sphereRadius, dir, out RaycastHit hit, stickDistance, layerMask))
            {
                hitPoint = hit.point;
                hitNormal = hit.normal;
                return true;
            }
        }

        if (Physics.Raycast(origin + Vector3.up * 0.5f, Vector3.down, out RaycastHit fr, stickDistance * 2f, layerMask))
        {
            hitPoint = fr.point;
            hitNormal = fr.normal;
            return true;
        }

        hitPoint = origin;
        hitNormal = Vector3.up;
        return false;
    }

    void ApplyStickyGravity()
    {
        if (FindClimbableSurface(out Vector3 surfacePoint, out Vector3 surfaceNormal))
        {
            smoothedNormal = Vector3.Slerp(smoothedNormal, surfaceNormal, Time.deltaTime * rotationSpeed);
            currentDirection = Vector3.ProjectOnPlane(currentDirection, smoothedNormal).normalized;
            Quaternion targetRot = Quaternion.LookRotation(currentDirection, smoothedNormal);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * rotationSpeed);

            float desiredDistance = sphereCol.radius * transform.localScale.y + groundOffset;
            Vector3 targetPos = surfacePoint + smoothedNormal * desiredDistance;
            transform.position = Vector3.Lerp(transform.position, targetPos, Time.deltaTime * stickSpeed);
        }
        else
        {
            transform.position += Vector3.down * fallSpeed * Time.deltaTime;
            smoothedNormal = Vector3.up;
        }
    }

    void TryDig()
    {
        if (Time.time - lastDigTime < digCooldown) return;
        lastDigTime = Time.time;
        int terrainMask = LayerMask.GetMask("Terrain");
        Vector3 origin = transform.position + transform.up * 0.1f;
        Vector3 dir = GetBestDigDirection();

        if (Physics.Raycast(origin, dir, out RaycastHit hit, stickDistance, terrainMask))
        {
            VoxelWorld.Instance.TryDigAt(hit.point, digRadius);
            PheromoneField.Instance.DepositDig(hit.point, pheromoneDepositAmount);
        }
        else
        {
            Vector3 fallback = transform.position + transform.forward * 0.6f;
            Debug.LogWarning($"⚠️ Ant {name} failed to dig. Fallback at {fallback}");
            VoxelWorld.Instance.TryDigAt(fallback, digRadius * 0.8f);
            PheromoneField.Instance.DepositDig(fallback, pheromoneDepositAmount * 0.5f);
        }

        float digPhero = PheromoneField.Instance.GetDig(Vector3Int.FloorToInt(transform.position));
        if (digPhero > 1f)
        {
            currentState = State.Expanding;
            return;
        }
        currentState = State.Roaming;
    }

    void ExpandChamber()
    {
        Vector3 origin = transform.position + transform.up * 0.1f;
        Vector3[] dirs = { transform.right, -transform.right, transform.forward, -transform.forward };
        Vector3 dir = dirs[Random.Range(0, dirs.Length)];
        if (Physics.Raycast(origin, dir, out RaycastHit hit, 2f, LayerMask.GetMask("Terrain")))
        {
            VoxelWorld.Instance.TryDigAt(hit.point, digRadius * 1.5f);
            PheromoneField.Instance.DepositDig(hit.point, pheromoneDepositAmount * 1.5f);
        }
        currentState = State.Roaming;
    }

    void PickNewDirection()
    {
        float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
        Vector3 ld = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        currentDirection = Vector3.ProjectOnPlane(ld, smoothedNormal).normalized;
        transform.rotation = Quaternion.LookRotation(currentDirection, smoothedNormal);
    }

    Vector3Int GetBestDigTarget()
    {
        Vector3Int current = Vector3Int.FloorToInt(transform.position);
        float bestScore = 0f;
        Vector3Int bestTarget = Vector3Int.zero;
        Vector3Int[] offsets = {
            new Vector3Int(0, -1, 0), new Vector3Int(1, -1, 0), new Vector3Int(-1, -1, 0),
            new Vector3Int(0, -1, 1), new Vector3Int(0, -1, -1),
            new Vector3Int(1, 0, 0), new Vector3Int(-1, 0, 0),
            new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1),
        };

        foreach (var offset in offsets)
        {
            Vector3Int check = current + offset;
            float digPhero = PheromoneField.Instance.GetDig(check);
            float trailPhero = PheromoneField.Instance.GetTrail(check);
            float score = digPhero + 0.1f - trailPhero * 0.5f;
            if (offset.y < 0) score += 0.2f;
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
            for (int y = -1; y <= 0; y++)
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
