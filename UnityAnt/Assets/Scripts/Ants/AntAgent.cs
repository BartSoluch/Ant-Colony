// === AntAgent.cs (with Nest Site Selection, Stigmergy) ===
using MarchingCubesGPUProject;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class AntAgent : MonoBehaviour
{
    public enum State { Roaming, Digging, Expanding, Depositing, Returning}
    public enum Role { Worker, Scout, Queen }
    public enum ChamberType
    {
        None,
        FungusGarden,
        Nursery,
        WasteDump
    }

    [Header("Settings")]
    public State currentState = State.Roaming;
    public Role currentRole = Role.Worker;
    public ChamberType currentChamberType = ChamberType.None;

    [Header("Initial Dig Settings")]
    public float initialNestPheroThreshold = 150f;  // how much nest pheromone we need to start digging

    [Header("Dig Settings")]
    public float digRadius = 1.2f;
    public float digCooldown = 2f;
    public float digPheroThreshold = 250f;

    [Header("Chamber Expansion Settings")]
    public int maxExpansionDigs = 50;
    private int expansionDigCount = 0;
    private int failCount = 0;
    private int maxFails = 25;

    [Header("Ant Movement Settings")]
    public float moveSpeed = 1.5f;
    public float pheromoneDepositAmount = 1f;
    public float directionUpdateCooldown = 2f;

    //Queen stats
    private float homeBias = 0.2f;       // how strongly she returns home
    private bool queenHasSettled = false;
    private Vector3 queenSettlePosition;

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
    public float digPheroBias = 1f;
    public float trailPheroBias = 1f;
    public float randomnessBias = 1f;
    public float downwardBias = 1f;
    public float upwardBias = 1f;

    [Header("ACO Sensing")]
    public int senseDist = 7;   // max radius
    public int incPerSearch = 2;   // gap between shells (e.g. 1,3,5)

    private float descentThreshold = 160f;    // above this Y, prefer going down
    //private float descentBias = 50f;     // multiplier for downward moves

    [Header("Overcrowding Settings")]
    public float crowdingCheckRadius = 5f;
    public int crowdingThreshold = 2;
    private float lastCrowdCheckTime = 0f;
    private float crowdCooldown = 1f;

    private float spawnTime;
    private Animator animator;
    private ChunkManager _chunkManager;

    void Start()
    {
        spawnTime = Time.time;
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

        StickToSDFSurface();
    }

    void Die()
    {
        isDead = true;
        GameManager.Instance.NotifyAntDeath(this);
        AntSpawner.AllAnts.Remove(this);
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
                maxAge = 1800f; // 20-60 min (or even infinite for test)
                energy = 180f;
                energyDecayRate = 0.1f; // Very slow decay
                break;
        }
    }
    void AssignBiases()
    {
        waterBias = Random.Range(50f, 100f);
        co2Bias = Random.Range(25f, 75f);
        trailPheroBias = Random.Range(0.5f, 1.5f);
        digPheroBias = Random.Range(0.5f, 1.5f);
        randomnessBias = Random.Range(-1f, 2f);
        downwardBias = Random.Range(0.75f, 2f);
        upwardBias = Random.Range(0.5f, 1.5f);
    }

    void HandleQueenBehavior()
    {
        float dt = Time.deltaTime;
        Vector3 me = transform.position;

        if (!queenHasSettled && transform.position.y <= 160f)
        {
            queenHasSettled = true;
            queenSettlePosition = transform.position;
        }

        // 1) Compute centroid of all worker ants
        Vector3 target = queenHasSettled ? queenSettlePosition : (GameManager.Instance.FindWorkerCentre(transform.position, 20f) ?? transform.position);

        if (Time.time - lastDirectionUpdateTime > directionUpdateCooldown)
        {
            currentDirection = (target - transform.position).normalized;
            lastDirectionUpdateTime = Time.time;
        }
        Debug.DrawLine(transform.position, target, Color.magenta, 0.1f);

        // 2) Stick to surface + move every frame (like Roam())
        Vector3 surfN = SampleSurfaceNormal(transform.position);
        smoothedNormal = Vector3.Slerp(smoothedNormal, surfN, dt * 8f);

        Vector3 move = Vector3.ProjectOnPlane(currentDirection, smoothedNormal).normalized;
        transform.position += move * moveSpeed * Time.deltaTime;

        // 3) Egg‐laying as before
        if (GameManager.Instance.ShouldQueenLayEgg() && GameManager.Instance.CanSpawnMoreAnts())
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                Vector3 raw = transform.position + Random.onUnitSphere * 2f;
                raw.y = transform.position.y; // maintain Queen's Y

                Vector3 n = SampleSurfaceNormal(raw);
                if (n == Vector3.zero) continue; // skip bad surface

                float d = SampleDensity(raw);
                Vector3 surface = raw - n * d + n * 0.5f;

                // Check if spawn point is inside solid
                if (SampleDensity(surface) > 0.1f) continue;

                // Check for collision with other ants
                Collider[] overlaps = Physics.OverlapSphere(surface, 0.5f);
                bool blocked = false;
                foreach (var col in overlaps)
                {
                    if (col.CompareTag("Ant"))
                    {
                        blocked = true;
                        break;
                    }
                }
                if (blocked) continue;

                // ✅ Valid spawn position
                GameManager.Instance.SpawnAntAt(
                    surface,
                    GameManager.Instance.DecideNextAntRole()
                );
                break; // only spawn one per frame
            }
        }
    }

    void HandleWorkerBehavior()
    {
        var zone = _chunkManager.GetZoneAtVoxel(transform.position);
        switch (currentState)
        {
            case State.Roaming:
                if (zone != ChunkManager.ChamberType.None)
                {
                    // begin expanding this chamber
                    Debug.Log($"Found a Chamber!");
                    currentChamberType = (ChamberType)zone;
                    currentState = State.Expanding;
                    expansionDigCount = 0;
                    return;
                }
                Roam(); 
                break;
            case State.Digging:
                if (zone == ChunkManager.ChamberType.None) TryDig(); 
                break;
            case State.Expanding: ExpandChamber(); break;
        }
    }
    void HandleScoutBehavior()
    {
        //Vector3Int current = Vector3Int.FloorToInt(transform.position);
        //Vector3Int best = GetBestTrailTarget(current);

        //if (best != Vector3Int.zero)
        //   currentDirection = ((Vector3)(best - current)).normalized;

        Roam(); // Already uses sticking and pheromone drop
    }

    void TryDig()
    {
        if (Time.time - lastDigTime < digCooldown)
            return;

        lastDigTime = Time.time;

        ChunkManager chunkManager = _chunkManager;
        if (chunkManager == null) return;

        PickNewDirectionACO();
        Vector3 digPos = transform.position + currentDirection * 1.5f;

        float digPhero = PheromoneField.Instance.GetDig(Vector3Int.FloorToInt(transform.position));
        var zone = _chunkManager.GetZoneAtVoxel(transform.position);

        if (zone == ChunkManager.ChamberType.None)
        {
            chunkManager.DigAtWorldPosition(digPos, digRadius);
            GameManager.Instance.RegisterDigEvent();
            PheromoneField.Instance.DepositDig(digPos, pheromoneDepositAmount);
        }

        if (digPhero > digPheroThreshold && transform.position.y < 150f)
            currentState = State.Expanding;
        else
            currentState = State.Roaming;
    }

    void ExpandChamber()
    {
        // stop after max digs
        if (expansionDigCount >= maxExpansionDigs || failCount >= maxFails)
        {
            currentState = State.Roaming;
            failCount = 0;
            return;
        }

        // obey dig cooldown
        if (Time.time - lastDigTime < digCooldown)
            return;

        lastDigTime = Time.time;

        // pick a random horizontal direction
        Vector2 dir2D = Random.insideUnitCircle.normalized;
        Vector3 dir3D = new Vector3(dir2D.x, 0f, dir2D.y);
        Vector3 digPos = transform.position + dir3D * (digRadius * 1.2f);

        // only dig within the same chamber zone
        var targetZone = _chunkManager.GetZoneAtVoxel(digPos);
        if ((ChamberType)targetZone != currentChamberType)
        {
            failCount++;
            return;
        }

        // do the actual dig
        _chunkManager?.DigAtWorldPosition(digPos, digRadius * 1.2f);
        PheromoneField.Instance.DepositDig(digPos, pheromoneDepositAmount * 1.2f);

        expansionDigCount++;
    }


    void AssignChamberType(Vector3 position)
    {
        float water = _chunkManager.SampleWaterAtWorldPosition(position);
        float co2 = _chunkManager.SampleCO2AtWorldPosition(position);
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
        Vector3 move = Vector3.ProjectOnPlane(currentDirection, smoothedNormal).normalized;
        transform.position += move * moveSpeed * Time.deltaTime;

        if (currentRole == Role.Worker)
        {
            EvaluateNestSite();
            EvaluateChamberSite();

            PheromoneField.Instance.DepositTrail(transform.position, pheromoneDepositAmount * 0.25f);
            Vector3Int pos = Vector3Int.FloorToInt(transform.position);
            float localNest = PheromoneField.Instance.GetNest(pos);
            Vector3Int bestDigTarget = GetBestDigTarget();
            var zone = _chunkManager.GetZoneAtVoxel(transform.position);

            if (!GameManager.colonyDiggingStarted && Time.time - spawnTime >= 15f)
            {
                if (bestDigTarget != Vector3Int.zero && localNest >= initialNestPheroThreshold)
                {
                    currentDirection = ((Vector3)(bestDigTarget - pos)).normalized;
                    currentState = State.Digging;
                    GameManager.colonyDiggingStarted = true;
                }
            }
            else if (Time.time - spawnTime >= 15f)
            {
                if (bestDigTarget != Vector3Int.zero)
                {
                    currentState = State.Digging;
                }
                else if (Random.value < 0.01f)
                {
                    currentState = State.Digging;
                }
            }
            if (Time.time - lastDirectionUpdateTime > directionUpdateCooldown)
            {
                PickNewDirectionACO();
                lastDirectionUpdateTime = Time.time;
            }
        }
        if (currentRole == Role.Scout)
        {
            // Leave stronger trails to encourage others to follow
            if (Time.time - lastDirectionUpdateTime > directionUpdateCooldown)
            {
                PickNewDirectionACO(); // Now includes 3D
                lastDirectionUpdateTime = Time.time;
            }
            PheromoneField.Instance.DepositTrail(transform.position, pheromoneDepositAmount * 2.0f);
            EvaluateNestSite();
            EvaluateChamberSite();
        }
    }
    void EvaluateChamberPotential()
    {
        Vector3Int pos = Vector3Int.FloorToInt(transform.position);

        float water = _chunkManager.SampleWaterAtWorldPosition(pos);
        float co2 = _chunkManager.SampleCO2AtWorldPosition(pos);
        float depth = pos.y;

        float chamberScore = 0f;

        if (water > 0.6f && co2 < 0.3f && depth > 5f && depth < 20f)
            chamberScore = 1.0f; // Fungus
        else if (water > 0.4f && water < 0.6f && co2 < 0.3f && depth > 10f && depth < 20f)
            chamberScore = 0.8f; // Nursery
        else if (water < 0.3f && co2 > 0.5f && depth < 5f)
            chamberScore = 0.6f; // Waste

        if (chamberScore > 0f)
        {
            // Reinforce the idea this is a good chamber site
            PheromoneField.Instance.DepositChamber(transform.position, chamberScore * 0.6f);
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
        if (GameManager.colonyDiggingStarted) return;

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
        var zone = _chunkManager.GetZoneAtVoxel(transform.position);
        if (zone == ChunkManager.ChamberType.None) return;
        float score = zone == ChunkManager.ChamberType.FungusGarden ? 1f
                     : zone == ChunkManager.ChamberType.Nursery ? 0.8f
                     : zone == ChunkManager.ChamberType.WasteDump ? 0.6f : 0f;
        PheromoneField.Instance.DepositChamber(transform.position, score);
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

            float water = _chunkManager.SampleWaterAtWorldPosition(check);
            float co2 = _chunkManager.SampleCO2AtWorldPosition(check);
            float depth = check.y;

            // Calculate score
            float score = 0f;
            score += water * 0.6f * waterBias;
            score -= co2 * 0.4f * co2Bias;

            if (offset.y < 0) score *= 1.2f; // Prefer digging downward
            if (depth < 150f) score *= 1.2f;  // Prefer staying underground (but not too deep)

            if (!GameManager.colonyDiggingStarted)
            {
                float nestPheromone = PheromoneField.Instance.GetNest(check);
                score += nestPheromone;
            }

            // **branching penalty**: avoid places with lots of previous digging
            //float already = PheromoneField.Instance.GetDig(check);
            //score -= already * 0.2f;

            if (score > bestScore)
            {
                bestScore = score;
                bestTarget = check;
            }
        }
        return bestScore > 0.2f ? bestTarget : Vector3Int.zero;
    }

    void StickToSDFSurface()
    {
        Vector3 pos = transform.position;

        // Early exit if we're outside terrain
        if (_chunkManager == null || _chunkManager.GetChunkAtWorldPosition(pos) == null)
            return;

        float density = SampleDensity(pos);
        Vector3 normal = SampleSurfaceNormal(pos);

        if (density <= -0.9f)
        {
            transform.position += Vector3.down * moveSpeed * Time.deltaTime;
            return;
        }
        else if (density > 0.5f)
        {
            // small upward nudge to get it back into the tunnel
            transform.position += transform.up * moveSpeed * Time.deltaTime * density;
        }
        if (normal == Vector3.zero)
        {
            // Try to nudge ant in a random direction to escape flat zone
            Vector3 nudge = Random.onUnitSphere * 0.2f;
            transform.position += nudge;

            return; // skip alignment this frame
        }

        // Smooth normal to avoid jitter
        smoothedNormal = Vector3.Slerp(smoothedNormal, normal, Time.deltaTime * 8f);

        // Move ant toward surface (iso-surface is at density = 0)
        transform.position -= density * normal * Time.deltaTime;

        // Align rotation to surface
        Vector3 forward = Vector3.ProjectOnPlane(currentDirection, -smoothedNormal).normalized;
        if (forward.sqrMagnitude > 0.001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(forward, -smoothedNormal);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * 10f);
        }
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
        if (chunkManager == null) return smoothedNormal;

        MarchingCubesGPU chunk = chunkManager.GetChunkAtWorldPosition(pos);
        if (chunk == null) return smoothedNormal;

        float eps = 0.5f;
        float dx = chunk.SampleDensityAtWorldPosition(pos + Vector3.right * eps) - chunk.SampleDensityAtWorldPosition(pos - Vector3.right * eps);
        float dy = chunk.SampleDensityAtWorldPosition(pos + Vector3.up * eps) - chunk.SampleDensityAtWorldPosition(pos - Vector3.up * eps);
        float dz = chunk.SampleDensityAtWorldPosition(pos + Vector3.forward * eps) - chunk.SampleDensityAtWorldPosition(pos - Vector3.forward * eps);

        Vector3 gradient = new Vector3(dx, dy, dz);
        return (gradient.sqrMagnitude > 0.0001f) ? gradient.normalized : smoothedNormal;
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
        var offsets = new List<Vector3Int>();
        for (int r = 1; r <= senseDist; r += incPerSearch)
        {
            // walk the cube from [-r..r] in each axis,
            // but only take those exactly at Chebyshev distance == r
            for (int x = -r; x <= r; x++)
                for (int y = -r; y <= r; y++)
                    for (int z = -r; z <= r; z++)
                        if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(y), Mathf.Abs(z)) == r)
                            offsets.Add(new Vector3Int(x, y, z));
        }

        var requiredZone = (ChunkManager.ChamberType) currentChamberType;
        List<(Vector3Int pos, float score)> candidates = new();
        float totalScore = 0f;

        foreach (var offset in offsets)
        {
            Vector3Int neighbor = current + offset;
            var neighborZone = _chunkManager.GetZoneAtVoxel(neighbor);
            if (currentState == State.Expanding && neighborZone != requiredZone) continue;
            float score = 0f;

            float trail = PheromoneField.Instance.GetTrail(neighbor);
            float dig = PheromoneField.Instance.GetDig(neighbor);
            //float nest = PheromoneField.Instance.GetNest(neighbor);
            //float chamber = PheromoneField.Instance.GetChamber(neighbor);

            float water = _chunkManager.SampleWaterAtWorldPosition(neighbor);
            float co2 = _chunkManager.SampleCO2AtWorldPosition(neighbor);

            score += water * waterBias;
            score -= co2 * co2Bias;

            score += trail * 0.1f * trailPheroBias;
            score += dig * digPheroBias;
            //score += nest * 0.6f * nestPheroBias;
            //score += chamber * 0.5f * chamberPheroBias;

            if (offset.y < 0) score *= downwardBias;
            else if (offset.y > 0) score *= upwardBias;
            score *= Random.Range(1f, randomnessBias);

            
            var crowdCentre = GameManager.Instance.FindWorkerCentre(transform.position, crowdingCheckRadius);
            if (crowdCentre.HasValue)
            {
                Vector3 repelDir = (transform.position - crowdCentre.Value).normalized;
                Vector3 offsetDir = ((Vector3)offset).normalized;

                float alignment = Vector3.Dot(repelDir, offsetDir); // ranges -1 to 1

                score *= Mathf.Lerp(2f, 5f, Mathf.Clamp01(alignment)); // reward directions away from cluster
            }
            

            if (score > 0.01f)
            {
                candidates.Add((neighbor, score));
                totalScore += score;
            }
        }

        if (candidates.Count > 0)
        {
            var best = candidates[0];
            foreach (var candidate in candidates)
            {
                if (candidate.score > best.score)
                    best = candidate;
            }

            currentDirection = ((Vector3)(best.pos - current)).normalized;
            return;
        }
        //Debug.Log("Using Fallback PickNewDirection()");
        PickNewDirection(); // fallback
    }

    void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying) return;

        Vector3 pos = transform.position;

        // Surface normal
        Vector3 normal = SampleSurfaceNormal(pos);
        Gizmos.color = Color.green;
        Gizmos.DrawLine(pos, pos + normal);

        // Smoothed normal
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(pos, pos + smoothedNormal);

        // Movement direction
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(pos, pos + currentDirection);

        // Display densities
        float here = SampleDensity(pos);
        float below = SampleDensity(pos + Vector3.down * 0.5f);
        float above = SampleDensity(pos + Vector3.up * 0.5f);
        GUIStyle style = new GUIStyle { normal = new GUIStyleState { textColor = Color.white } };
        #if UNITY_EDITOR
                UnityEditor.Handles.Label(pos + Vector3.up * 1.5f, $"Densities: here={here:F2}, up={above:F2}, down={below:F2}", style);
        #endif
    }
    public string SerializeState()
    {
        string id = gameObject.GetInstanceID().ToString();
        Vector3 pos = transform.position;
        string role = currentRole.ToString();
        string state = currentState.ToString();

        return $"{id},{name},{pos.x:F2},{pos.y:F2},{pos.z:F2},{role},{state}," +
               $"{waterBias:F2},{co2Bias:F2},{trailPheroBias:F2},{digPheroBias:F2},{randomnessBias:F2},{downwardBias:F2},{upwardBias:F2}";
    }
}