// === AntAgent.cs (with Nest Site Selection, Stigmergy) ===
using MarchingCubesGPUProject;
using System.Collections.Generic;
using UnityEngine;

public class AntAgent : MonoBehaviour
{
    public enum State { Roaming, Digging, Expanding }
    public enum Role { Worker, Scout, Queen }
    public enum ChamberType
    {
        None,
        FungusGarden,
        Nursery,
        WasteDump
    }

    public ChamberType currentChamberType = ChamberType.None;

    [Header("Settings")]
    public State currentState = State.Roaming;
    public Role currentRole = Role.Worker;

    [Header("Initial Dig Settings")]
    public float initialNestPheroThreshold = 3f;  // how much nest pheromone we need to start digging

    public bool hasStartedDigging = false;         // flips true on the very first dig

    [Header("Dig Settings")]
    public float digRadius = 1.2f;
    public float digCooldown = 2f;
    public float digPheroThreshold = 3f;

    [Header("Ant Movement Settings")]
    public float moveSpeed = 1.5f;
    public float pheromoneDepositAmount = 1f;
    public float directionUpdateCooldown = 2f;

    private float lastDigTime;
    private float lastDirectionUpdateTime;
    private Vector3 currentDirection;
    private Vector3 smoothedNormal = Vector3.up;

    [Header("Lifecycle")]
    public float maxAge = 300f;
    public float energy = 100f;
    public float energyDecayRate = 1f;
    private float age = 0f;
    private bool isDead = false;

    [Header("Ant Biases")]
    public float waterBias = 1f;   // preference for humidity
    public float co2Bias = 1f;     // avoidance of CO2
    public float nestPheroBias = 1f;
    public float chamberPheroBias = 1f;
    public float randomnessBias = 1f;

    [Header("Overcrowding Settings")]
    public float crowdingCheckRadius = 1.5f;
    public int crowdingThreshold = 4;
    private float lastCrowdCheckTime = 0f;
    private float crowdCooldown = 1f;

    private Animator animator;
    private ChunkManager _chunkManager;

    void Start()
    {
        _chunkManager = FindFirstObjectByType<ChunkManager>();
        PickNewDirectionACO();
        animator = GetComponentInChildren<Animator>();
        AssignLifespanAndEnergy();
        AssignBiases();
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
                HandleQueenBehavior();
                break;
            case Role.Worker:
                HandleWorkerBehavior();
                break;
            case Role.Scout:
                HandleScoutBehavior();
                break;
        }

        ApplyStickyGravitySDF();
    }

    void Die()
    {
        isDead = true;
        GameManager.Instance.NotifyAntDeath(this);
        Destroy(gameObject);
    }
    void AssignLifespanAndEnergy()
    {
        switch (currentRole)
        {
            case Role.Worker:
                maxAge = Random.Range(360f, 480f); // 6-8 min
                energy = 100f;
                energyDecayRate = 0.5f; // Slow decay
                break;
            case Role.Scout:
                maxAge = Random.Range(240f, 360f); // 4-6 min
                energy = 80f;
                energyDecayRate = 0.7f; // Faster decay
                break;
            case Role.Queen:
                maxAge = Random.Range(1200f, 3600f); // 20-60 min (or even infinite for test)
                energy = 300f;
                energyDecayRate = 0.1f; // Very slow decay
                break;
        }
    }
    void AssignBiases()
    {
        waterBias = Random.Range(0.8f, 1.2f);
        co2Bias = Random.Range(0.8f, 1.2f);
        nestPheroBias = Random.Range(0.8f, 1.2f);
        chamberPheroBias = Random.Range(0.8f, 1.2f);
        randomnessBias = Random.Range(0.5f, 1.5f);
    }
    void HandleQueenBehavior()
    {
        if (GameManager.Instance.ShouldQueenLayEgg() && GameManager.Instance.CanSpawnMoreAnts())
        {
            Vector3 spawnPos = transform.position + Random.insideUnitSphere * 2f;
            spawnPos.y = transform.position.y;

            AntAgent.Role role = GameManager.Instance.DecideNextAntRole();
            GameManager.Instance.SpawnAntAt(spawnPos, role);
        }
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
            currentDirection = ((Vector3)(best - current)).normalized;

        if (Time.time - lastDirectionUpdateTime > directionUpdateCooldown)
        {
            PickNewDirectionACO();
            lastDirectionUpdateTime = Time.time;
        }

        Roam();
    }

    void TryDig()
    {
        if (Time.time - lastDigTime < digCooldown) return;
        lastDigTime = Time.time;

        ChunkManager chunkManager = _chunkManager;
        if (chunkManager == null) return;

        Vector3 digPos = transform.position + transform.forward * 0.6f;
        chunkManager.DigAtWorldPosition(digPos, digRadius);
        GameManager.Instance.RegisterDigEvent();

        PheromoneField.Instance.DepositDig(digPos, pheromoneDepositAmount);

        float digPhero = PheromoneField.Instance.GetDig(Vector3Int.FloorToInt(transform.position));
        currentState = (digPhero > 5f && transform.position.y < 150f) ? State.Expanding : State.Roaming;
    }

    void ExpandChamber()
    {
        ChunkManager chunkManager = _chunkManager;
        if (chunkManager == null) return;

        Vector3 direction = Random.insideUnitSphere;
        Vector3 digPos = transform.position + direction * 1.5f;
        chunkManager.DigAtWorldPosition(digPos, digRadius * 1.5f);
        PheromoneField.Instance.DepositDig(digPos, pheromoneDepositAmount * 1.5f);

        // If no current chamber type, assign one
        if (currentChamberType == ChamberType.None)
        {
            AssignChamberType(transform.position);
            Debug.Log("Assigned Chamber: " + currentChamberType);
        }

        currentState = State.Roaming;
    }

    void AssignChamberType(Vector3 position)
    {
        ChunkManager chunkManager = _chunkManager;
        if (chunkManager == null) return;

        MarchingCubesGPU chunk = chunkManager.GetChunkAtWorldPosition(position);
        if (chunk == null) return;

        float water = chunk.SampleWaterAtWorldPosition(position);
        float co2 = chunk.SampleCO2AtWorldPosition(position);
        float depth = position.y;

        if (water > 0.5f && co2 < 0.3f && depth > 5f && depth < 20f)
        {
            currentChamberType = ChamberType.FungusGarden;
        }
        else if (water > 0.3f && water < 0.6f && depth > 10f && depth < 20f)
        {
            currentChamberType = ChamberType.Nursery;
        }
        else if (co2 > 0.5f && water < 0.3f && depth < 5f)
        {
            currentChamberType = ChamberType.WasteDump;
        }
        else
        {
            currentChamberType = ChamberType.None; // No special chamber
        }
    }

    void TryRecoverEnergy()
    {
        if (currentChamberType == ChamberType.FungusGarden)
        {
            energy = Mathf.Min(energy + 20f * Time.deltaTime, 100f);
        }
    }

    void Roam()
    {
        Vector3 normal = SampleSurfaceNormal(transform.position);
        if (normal == Vector3.zero) return;

        if (Time.time - lastCrowdCheckTime > crowdCooldown && IsOvercrowded())
        {
            lastCrowdCheckTime = Time.time;
            PickNewDirectionACO();
            currentDirection = Quaternion.AngleAxis(Random.Range(90f, 180f), Vector3.up) * currentDirection;
            return;
        }

        smoothedNormal = Vector3.Slerp(smoothedNormal, normal, Time.deltaTime * 8f);
        Vector3 move = Vector3.ProjectOnPlane(currentDirection, smoothedNormal).normalized;
        transform.position += move * moveSpeed * Time.deltaTime;

        Vector3 projectedForward = Vector3.ProjectOnPlane(currentDirection, -smoothedNormal).normalized;
        if (projectedForward.sqrMagnitude > 0.01f)
        {
            Quaternion targetRot = Quaternion.LookRotation(projectedForward, -smoothedNormal);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * 8f);
        }

        EvaluateNestSite();
        EvaluateChamberSite();

        PheromoneField.Instance.DepositTrail(transform.position, pheromoneDepositAmount * 0.25f);

        if (currentRole == Role.Worker)
        {
            Vector3Int pos = Vector3Int.FloorToInt(transform.position);
            float localNest = PheromoneField.Instance.GetNest(pos);
            Vector3Int bestDigTarget = GetBestDigTarget();

            Debug.Log($"[Roam] Ant at {pos} sees NestPhero = {localNest:F2}; startedDigging = {hasStartedDigging}");

            if (!hasStartedDigging)
            {
                if (bestDigTarget != Vector3Int.zero && localNest >= initialNestPheroThreshold)
                {
                    currentDirection = ((Vector3)(bestDigTarget - pos)).normalized;
                    currentState = State.Digging;
                    hasStartedDigging = true;

                }
            }

            else
            {
                if (bestDigTarget != Vector3Int.zero)
                {
                    currentDirection = ((Vector3)(bestDigTarget - pos)).normalized;
                    currentState = State.Digging;
                }
                else if (Random.value < 0.01f)
                {
                    currentState = State.Digging;
                }
                if (PheromoneField.Instance.GetChamber(pos) > 2f)
                {
                    currentState = State.Expanding;
                }
            }
        }

        if (Time.time - lastDirectionUpdateTime > directionUpdateCooldown)
        {
            PickNewDirectionACO();
            lastDirectionUpdateTime = Time.time;
        }
    }
    bool IsOvercrowded()
    {
        Collider[] nearby = Physics.OverlapSphere(transform.position, crowdingCheckRadius);
        int antCount = 0;

        foreach (var col in nearby)
        {
            if (col.CompareTag("Ant")) // ← Ensure ant prefab has tag "Ant"
                antCount++;
        }

        return antCount > crowdingThreshold;
    }
    void EvaluateNestSite()
    {
        if (hasStartedDigging) return;

        Vector3Int pos = Vector3Int.FloorToInt(transform.position);
        float density = SampleDensity(transform.position);
        float depth = transform.position.y;

        float nestScore = 0f;

        if (density < 0.3f) nestScore += 0.3f;
        if (depth > 140f) nestScore += (190f - depth) * 0.2f;

        if (nestScore > 0.5f)
            PheromoneField.Instance.DepositNest(transform.position, nestScore * 0.13f);

        //Debug.Log($"Depositing {nestScore} pheromones");
    }

    void EvaluateChamberSite()
    {
        Vector3Int pos = Vector3Int.FloorToInt(transform.position);

        ChunkManager chunkManager = _chunkManager;
        if (chunkManager == null) return;

        MarchingCubesGPU chunk = chunkManager.GetChunkAtWorldPosition(pos);
        if (chunk == null) return;

        float water = chunk.SampleWaterAtWorldPosition(pos);
        float co2 = chunk.SampleCO2AtWorldPosition(pos);
        float depth = pos.y;

        float chamberScore = 0f;

        // Prefer fungus garden conditions
        if (water > 0.6f && co2 < 0.3f && depth > 5f && depth < 20f)
            chamberScore += 1.0f;

        // Prefer nursery conditions
        else if (water > 0.4f && water < 0.6f && co2 < 0.3f && depth > 10f && depth < 20f)
            chamberScore += 0.8f;

        // Prefer waste dump conditions
        else if (water < 0.3f && co2 > 0.5f && depth < 5f)
            chamberScore += 0.6f;

        if (chamberScore > 0f)
        {
            Debug.Log($"Depositing chamber pheromone at {pos}: Water={water:F2}, CO₂={co2:F2}, Depth={depth:F2}");
            PheromoneField.Instance.DepositChamber(transform.position, chamberScore * 0.5f);
        }
    }

    Vector3Int GetBestTrailTarget(Vector3Int current)
    {
        Vector3Int[] offsets = { new(1, 0, 0), new(-1, 0, 0), new(0, 0, 1), new(0, 0, -1), new(1, 0, 1), new(-1, 0, -1), new(1, 0, -1), new(-1, 0, 1) };
        float bestPhero = 0f;
        Vector3Int bestPos = Vector3Int.zero;

        foreach (var offset in offsets)
        {
            Vector3Int check = current + offset;
            float score = PheromoneField.Instance.GetTrail(check) + 0.5f * PheromoneField.Instance.GetDig(check) + 1.5f * PheromoneField.Instance.GetNest(check);
            if (score > bestPhero)
            {
                bestPhero = score;
                bestPos = check;
            }
        }

        return bestPhero > 0.1f ? bestPos : Vector3Int.zero;
    }
    Vector3Int GetBestDigTarget()
    {
        Vector3Int current = Vector3Int.FloorToInt(transform.position);
        Vector3Int[] offsets = {
        new(1, 0, 0), new(-1, 0, 0),
        new(0, 0, 1), new(0, 0, -1),
        new(1, 0, 1), new(-1, 0, -1),
        new(1, 0, -1), new(-1, 0, 1),
        new(0, -1, 0) // Prefer downward digging too
    };

        float bestScore = float.MinValue;
        Vector3Int bestTarget = Vector3Int.zero;

        foreach (var offset in offsets)
        {
            Vector3Int check = current + offset;
            float density = SampleDensity(check);
            if (density <= 0f) continue; // Only dig into solid

            // Sample environmental quality
            ChunkManager chunkManager = _chunkManager;
            if (chunkManager == null) continue;

            MarchingCubesGPU chunk = chunkManager.GetChunkAtWorldPosition(check);
            if (chunk == null) continue;

            float water = chunk.SampleWaterAtWorldPosition(check);
            float co2 = chunk.SampleCO2AtWorldPosition(check);
            float depth = check.y;

            // Calculate score
            float score = 0f;
            score += water * 0.6f * waterBias;
            score -= co2 * 0.4f * co2Bias;

            if (offset.y < 0) score += 0.2f; // Prefer digging downward
            if (depth < 10f) score += 0.2f;  // Prefer staying underground (but not too deep)

            float nestPheromone = PheromoneField.Instance.GetNest(check);
            score += nestPheromone * 1.5f * nestPheroBias;

            if (score > bestScore)
            {
                bestScore = score;
                bestTarget = check;
            }
        }

        return bestScore > 0.2f ? bestTarget : Vector3Int.zero;
    }

    void ApplyStickyGravitySDF()
    {
        Vector3 pos = transform.position;
        float densityAtCurrent = SampleDensity(pos);
        float densityBelow = SampleDensity(pos + Vector3.down * 0.5f);
        Vector3 normal = SampleSurfaceNormal(pos);

        if (densityAtCurrent > 0.1f)
            transform.position -= normal * Time.deltaTime * 2f;
        else if (densityBelow > 0.1f)
        {
            Vector3 groundPoint = pos + Vector3.down * 0.5f;
            Vector3 targetPos = new Vector3(pos.x, groundPoint.y + 1.8f, pos.z);
            transform.position = Vector3.Lerp(transform.position, targetPos, Time.deltaTime * 4f);
        }
        else
            transform.position += Vector3.down * Time.deltaTime * 2f;
    }

    float SampleDensity(Vector3 pos)
    {
        ChunkManager chunkManager = _chunkManager;
        if (chunkManager == null) return 0f;
        MarchingCubesGPU chunk = chunkManager.GetChunkAtWorldPosition(pos);
        return chunk == null ? 0f : chunk.SampleDensityAtWorldPosition(pos);
    }

    Vector3 SampleSurfaceNormal(Vector3 pos)
    {
        ChunkManager chunkManager = _chunkManager;
        if (chunkManager == null) return Vector3.up;
        MarchingCubesGPU chunk = chunkManager.GetChunkAtWorldPosition(pos);
        if (chunk == null) return Vector3.up;

        float eps = 0.5f;
        float dx = chunk.SampleDensityAtWorldPosition(pos + Vector3.right * eps) - chunk.SampleDensityAtWorldPosition(pos - Vector3.right * eps);
        float dy = chunk.SampleDensityAtWorldPosition(pos + Vector3.up * eps) - chunk.SampleDensityAtWorldPosition(pos - Vector3.up * eps);
        float dz = chunk.SampleDensityAtWorldPosition(pos + Vector3.forward * eps) - chunk.SampleDensityAtWorldPosition(pos - Vector3.forward * eps);

        Vector3 gradient = new Vector3(dx, dy, dz);
        return gradient.sqrMagnitude > 0.0001f ? gradient.normalized : Vector3.zero;
    }

    void PickNewDirection()
    {
        float angle = Random.Range(0f, 360f);
        currentDirection = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)).normalized;
        transform.rotation = Quaternion.LookRotation(currentDirection);
    }
    void PickNewDirectionACO()
    {
        Vector3Int current = Vector3Int.FloorToInt(transform.position);
        Vector3Int[] offsets = {
        new(1, 0, 0), new(-1, 0, 0),
        new(0, 0, 1), new(0, 0, -1),
        new(1, 0, 1), new(-1, 0, -1),
        new(1, 0, -1), new(-1, 0, 1)
    };

        List<(Vector3Int pos, float score)> candidates = new();
        float totalScore = 0f;

        foreach (var offset in offsets)
        {
            Vector3Int neighbor = current + offset;

            float trail = PheromoneField.Instance.GetTrail(neighbor);
            float dig = PheromoneField.Instance.GetDig(neighbor);
            float nest = PheromoneField.Instance.GetNest(neighbor);
            float chamber = PheromoneField.Instance.GetChamber(neighbor);

            float score = 0f;
            score += trail * 0.4f * nestPheroBias;
            score += dig * 0.3f * chamberPheroBias;
            score += nest * 0.6f * nestPheroBias;
            score += chamber * 0.5f * chamberPheroBias;

            // Add a bit of noise for exploration
            score *= Random.Range(1f, randomnessBias);

            if (score > 0.01f)
            {
                candidates.Add((neighbor, score));
                totalScore += score;
            }
        }

        if (candidates.Count > 0)
        {
            float r = Random.Range(0f, totalScore);
            float runningTotal = 0f;

            foreach (var (pos, score) in candidates)
            {
                runningTotal += score;
                if (r <= runningTotal)
                {
                    currentDirection = ((Vector3)(pos - current)).normalized;
                    return;
                }
            }
        }

        // fallback
        PickNewDirection();
    }

}