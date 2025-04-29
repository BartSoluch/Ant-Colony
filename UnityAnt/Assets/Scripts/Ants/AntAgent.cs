using MarchingCubesGPUProject;
using UnityEngine;

public class AntAgent : MonoBehaviour
{
    public enum State { Roaming, Digging, Expanding }

    public State currentState = State.Roaming;

    private Renderer outlineRenderer;

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

    [Header("Lifecycle")]
    public float maxAge = 300f; // in seconds
    public float energy = 100f;
    public float energyDecayRate = 1f;
    public float age = 0f;
    public bool isDead = false;


    private Animator animator;

    public enum Role { Worker, Scout, Queen }
    public Role currentRole = Role.Worker;

    void Start()
    {
        PickNewDirection();
        animator = GetComponentInChildren<Animator>();

        Transform outlineMesh = transform.Find("Outline/__ant_4"); // Update if your structure changes
        if (outlineMesh != null)
        {
            outlineRenderer = outlineMesh.GetComponent<Renderer>();
        }

        ApplyOutlineColor(); // Apply color based on currentRole
    }

    void Update()
    {
        if (isDead) return;

        age += Time.deltaTime;
        energy -= energyDecayRate * Time.deltaTime;

        if (age >= maxAge || energy <= 0f)
        {
            Die();
            return;
        }

        switch (currentRole)
        {
            case Role.Queen:
                maxAge = 20 * 60f; // ~20 minutes in simulation
                energyDecayRate = 0.25f;
                HandleQueenBehavior();
                break;

            case Role.Worker:
                maxAge = Random.Range(5f * 60f, 8f * 60f); // 5–8 minutes
                energyDecayRate = 1.0f;
                HandleWorkerBehavior();
                break;

            case Role.Scout:
                maxAge = Random.Range(2f * 60f, 4f * 60f); // 2–4 minutes
                energyDecayRate = 1.5f;
                HandleScoutBehavior();
                break;
        }

        ApplyStickyGravitySDF();
    }
    void Die()
    {
        isDead = true;
        GameManager.Instance.NotifyAntDeath(this);
        Destroy(gameObject); // or play animation first, then destroy
    }

    void HandleWorkerBehavior()
    {
        switch (currentState)
        {
            case State.Roaming: Roam(); break;
            case State.Digging: TryDig(); break;
            case State.Expanding: ExpandChamber(); break;
        }
    }
    void HandleScoutBehavior()
    {
        Vector3Int current = Vector3Int.FloorToInt(transform.position);
        Vector3Int best = GetBestTrailTarget(current);

        if (best != Vector3Int.zero)
        {
            currentDirection = ((Vector3)(best - current)).normalized;
        }
        if (Time.time - lastDirectionUpdateTime > directionUpdateCooldown)
        {
            PickNewDirection(); // fallback randomness
            lastDirectionUpdateTime = Time.time;
        }

        Roam(); // Uses currentDirection
    }

    [Header("Queen Settings")]
    public float spawnInterval = 20f;
    private float lastSpawnTime;

    void HandleQueenBehavior()
    {
        // Remain idle
        if (Time.time - lastSpawnTime > spawnInterval)
        {
            TryLayEgg();
            lastSpawnTime = Time.time;
        }
    }
    void TryLayEgg()
    {
        Vector3 spawnPos = transform.position + Random.insideUnitSphere * 2f;
        spawnPos.y = transform.position.y; // Keep it on the same Y level

        if (GameManager.Instance.CanSpawnMoreAnts())
        {
            GameManager.Instance.SpawnAntAt(spawnPos);
        }
    }

    Vector3Int GetBestTrailTarget(Vector3Int current)
    {
        Vector3Int[] offsets = {
        new(1, 0, 0), new(-1, 0, 0),
        new(0, 0, 1), new(0, 0, -1),
        new(1, 0, 1), new(-1, 0, -1),
        new(1, 0, -1), new(-1, 0, 1)
    };

        float bestPhero = 0f;
        Vector3Int bestPos = Vector3Int.zero;

        foreach (var offset in offsets)
        {
            Vector3Int check = current + offset;
            float trail = PheromoneField.Instance.GetTrail(check);
            float dig = PheromoneField.Instance.GetDig(check); // Optional: weight if it’s a dug tunnel

            float score = trail + dig * 0.5f;

            if (score > bestPhero)
            {
                bestPhero = score;
                bestPos = check;
            }
        }

        return bestPhero > 0.1f ? bestPos : Vector3Int.zero;
    }

    void Roam()
    {
        Vector3 normal = SampleSurfaceNormal(transform.position);
        if (normal == Vector3.zero)
            return;

        smoothedNormal = Vector3.Slerp(smoothedNormal, normal, Time.deltaTime * 8f);
        Vector3 move = Vector3.ProjectOnPlane(currentDirection, smoothedNormal).normalized;

        transform.position += move * moveSpeed * Time.deltaTime;
        Vector3 projectedForward = Vector3.ProjectOnPlane(currentDirection, -smoothedNormal).normalized;
        if (projectedForward.sqrMagnitude > 0.01f)
        {
            Quaternion targetRot = Quaternion.LookRotation(projectedForward, -smoothedNormal);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * 8f);
        }

        PheromoneField.Instance.DepositTrail(transform.position, pheromoneDepositAmount * 0.25f);

        if (Time.time - lastDirectionUpdateTime > directionUpdateCooldown)
        {
            PickNewDirection();
            lastDirectionUpdateTime = Time.time;
        }

        if (currentRole == Role.Worker)
        {
            Vector3Int bestDigTarget = GetBestDigTarget();
            if (bestDigTarget != Vector3Int.zero)
            {
                currentDirection = ((Vector3)(bestDigTarget - Vector3Int.FloorToInt(transform.position))).normalized;
                currentState = State.Digging;
            }
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

            // Sample environment
            ChunkManager chunkManager = FindFirstObjectByType<ChunkManager>();
            if (chunkManager == null)
                continue;

            MarchingCubesGPU chunk = chunkManager.GetChunkAtWorldPosition(check);
            if (chunk == null)
                continue;

            float water = chunk.SampleWaterAtWorldPosition(check);
            float co2 = chunk.SampleCO2AtWorldPosition(check);

            // Environmental score
            float envScore = 0f;
            envScore += water * 0.8f;  // Prefer moist areas
            envScore -= co2 * 0.5f;    // Avoid high CO2 areas

            // Overall score
            float score = digPhero + 0.1f - trailPhero * 0.5f + envScore;

            if (offset.y < 0) score += 0.2f; // Prefer downward digging
            if (check.y < 2 && check.y > -10) score += 0.3f; // Stay near interesting depth

            if (score > bestScore)
            {
                bestScore = score;
                bestTarget = check;
            }
        }

        return bestScore > 0.15f ? bestTarget : Vector3Int.zero;
    }
    void ApplyOutlineColor()
    {
        Renderer rend = GetComponentInChildren<Renderer>();
        if (rend == null) return;

        // Ensure each ant has its own instance of the material
        if (!rend.material.name.Contains("Instance"))
            rend.material = new Material(rend.material);

        Color roleColor = Color.white;

        switch (currentRole)
        {
            case Role.Queen:
                roleColor = Color.magenta;
                break;
            case Role.Worker:
                roleColor = Color.yellow;
                break;
            case Role.Scout:
                roleColor = Color.cyan;
                break;
        }

        rend.material.SetColor("_OutlineColor", roleColor); // Match Shader Graph property name
    }

}
