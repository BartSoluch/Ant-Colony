using MarchingCubesGPUProject;
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

    private float lockedSurfaceHeight;
    private bool surfaceLocked = false;

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

        ApplyStickyGravitySDF();
    }

    void Roam()
    {
        Vector3 normal = SampleSurfaceNormal(transform.position);
        if (normal == Vector3.zero)
            return;

        smoothedNormal = Vector3.Slerp(smoothedNormal, normal, Time.deltaTime * 8f);
        Vector3 move = Vector3.ProjectOnPlane(currentDirection, smoothedNormal).normalized;

        transform.position += move * moveSpeed * Time.deltaTime;
        Vector3 projectedForward = Vector3.ProjectOnPlane(currentDirection, Vector3.up).normalized;
        if (projectedForward.sqrMagnitude > 0.01f)
        {
            Quaternion targetRot = Quaternion.LookRotation(projectedForward, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * 8f);
        }

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

    void ApplyStickyGravitySDF()
    {
        Vector3 pos = transform.position;
        Vector3 down = Vector3.down;

        float densityAtCurrent = SampleDensity(pos);
        float densityBelow = SampleDensity(pos + down * 0.5f);

        Vector3 normal = SampleSurfaceNormal(pos);

        if (densityAtCurrent > 0.1f)
        {
            // Inside solid: push outward
            transform.position -= normal * Time.deltaTime * 3f;
        }
        else if (densityBelow > 0.1f)
        {
            // Ground is close below: gently float above it
            Vector3 groundPoint = pos + down * 0.5f;
            Vector3 targetPos = new Vector3(pos.x, groundPoint.y + 1.5f, pos.z); // 1.5f hover

            // Heavy smoothing to avoid jitter
            transform.position = Vector3.Lerp(transform.position, targetPos, Time.deltaTime * 4f);
        }
        else
        {
            // No ground below: fall
            transform.position += Vector3.down * Time.deltaTime * 2f;
        }
    }

    Vector3 SampleSurfaceNormal(Vector3 pos)
    {
        ChunkManager chunkManager = FindFirstObjectByType<ChunkManager>();
        if (chunkManager == null)
            return Vector3.up;

        MarchingCubesGPU chunk = chunkManager.GetChunkAtWorldPosition(pos);
        if (chunk == null)
            return Vector3.up;

        float eps = 0.5f;
        float dx = chunk.SampleDensityAtWorldPosition(pos + Vector3.right * eps) - chunk.SampleDensityAtWorldPosition(pos - Vector3.right * eps);
        float dy = chunk.SampleDensityAtWorldPosition(pos + Vector3.up * eps) - chunk.SampleDensityAtWorldPosition(pos - Vector3.up * eps);
        float dz = chunk.SampleDensityAtWorldPosition(pos + Vector3.forward * eps) - chunk.SampleDensityAtWorldPosition(pos - Vector3.forward * eps);

        Vector3 gradient = new Vector3(dx, dy, dz);
        return gradient.sqrMagnitude > 0.0001f ? gradient.normalized : Vector3.zero;
    }

    float SampleDensity(Vector3 pos)
    {
        ChunkManager chunkManager = FindFirstObjectByType<ChunkManager>();
        if (chunkManager == null)
            return 0f;

        MarchingCubesGPU chunk = chunkManager.GetChunkAtWorldPosition(pos);
        if (chunk == null)
            return 0f;

        return chunk.SampleDensityAtWorldPosition(pos);
    }

    void PickNewDirection()
    {
        float angle = Random.Range(0f, 360f);
        currentDirection = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)).normalized;
        transform.rotation = Quaternion.LookRotation(currentDirection);
    }

    void TryDig()
    {
        if (Time.time - lastDigTime < digCooldown)
            return;
        lastDigTime = Time.time;

        ChunkManager chunkManager = FindFirstObjectByType<ChunkManager>();
        if (chunkManager == null)
        {
            Debug.LogError("ChunkManager not found. Aborting dig.");
            return;
        }

        Vector3 digPos = transform.position + transform.forward * 0.6f;
        chunkManager.DigAtWorldPosition(digPos, digRadius);
        PheromoneField.Instance.DepositDig(digPos, pheromoneDepositAmount);

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
        ChunkManager chunkManager = FindFirstObjectByType<ChunkManager>();
        if (chunkManager == null)
        {
            Debug.LogError("ChunkManager not found. Aborting chamber expansion dig.");
            currentState = State.Roaming;
            return;
        }

        Vector3[] directions = { transform.right, -transform.right, transform.forward, -transform.forward };
        Vector3 direction = directions[Random.Range(0, directions.Length)];

        Vector3 digPos = transform.position + direction * 1.5f;
        chunkManager.DigAtWorldPosition(digPos, digRadius * 1.5f);
        PheromoneField.Instance.DepositDig(digPos, pheromoneDepositAmount * 1.5f);

        currentState = State.Roaming;
    }

    Vector3Int GetBestDigTarget()
    {
        Vector3Int current = Vector3Int.FloorToInt(transform.position);
        float bestScore = 0f;
        Vector3Int bestTarget = Vector3Int.zero;

        Vector3Int[] offsets = {
            new(0, -1, 0), new(1, -1, 0), new(-1, -1, 0),
            new(0, -1, 1), new(0, -1, -1),
            new(1, 0, 0), new(-1, 0, 0),
            new(0, 0, 1), new(0, 0, -1),
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
}
