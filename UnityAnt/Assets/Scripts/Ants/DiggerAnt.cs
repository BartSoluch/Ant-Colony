// DiggerAnt.cs
using UnityEngine;

[RequireComponent(typeof(Animator))]
public class DiggerAnt : AntAgent
{
    public enum State { Roaming, Digging, Expanding }
    public State currentState = State.Roaming;

    [Header("Dig Settings")]
    public float digRadius = 6f;
    public float digCooldown = 4f;

    [Header("Ant Settings")]
    public float moveSpeed = 1.5f;
    public float pheromoneDepositAmount = 1f;
    public float directionUpdateCooldown = 2f;

    float lastDigTime;
    float lastDirectionUpdateTime;
    Animator animator;

    protected override void Awake()
    {
        base.Awake();
        animator = GetComponentInChildren<Animator>();
    }

    protected override void TickBehavior()
    {
        switch (currentState)
        {
            case State.Roaming: Roam(); break;
            case State.Digging: TryDig(); break;
            case State.Expanding: ExpandChamber(); break;
        }

        if (animator != null)
        {
            float spd = (currentState == State.Roaming) ? 1f : 0f;
            animator.SetFloat("Speed", spd);
        }
    }

    void Roam()
    {
        if (!FindClimbableSurface(out Vector3 p, out Vector3 n)) return;
        smoothedNormal = Vector3.Slerp(smoothedNormal, n, Time.deltaTime * rotationSpeed);

        Vector3 move = Vector3.ProjectOnPlane(currentDirection, smoothedNormal).normalized;
        transform.position += move * moveSpeed * Time.deltaTime;

        PheromoneField.Instance.DepositTrail(transform.position, pheromoneDepositAmount * 0.25f);

        if (Time.time - lastDirectionUpdateTime > directionUpdateCooldown)
        {
            PickNewDirection();
            lastDirectionUpdateTime = Time.time;
        }

        Vector3Int best = GetBestDigTarget();
        if (best != Vector3Int.zero)
        {
            currentDirection = ((Vector3)(best - Vector3Int.FloorToInt(transform.position))).normalized;
            currentState = State.Digging;
        }
    }

    void TryDig()
    {
        if (Time.time - lastDigTime < digCooldown) return;
        lastDigTime = Time.time;

        Vector3 origin = transform.position + transform.up * 0.1f;
        Vector3 dir = GetBestDigDirection();
        int mask = LayerMask.GetMask("Terrain");

        if (Physics.Raycast(origin, dir, out RaycastHit hit, stickDistance, mask))
        {
            VoxelWorld.Instance.TryDigAt(hit.point, digRadius);
            PheromoneField.Instance.DepositDig(hit.point, pheromoneDepositAmount);
        }
        else
        {
            Vector3 fallback = transform.position + transform.forward * 0.6f;
            VoxelWorld.Instance.TryDigAt(fallback, digRadius * 0.8f);
            PheromoneField.Instance.DepositDig(fallback, pheromoneDepositAmount * 0.5f);
        }

        float dph = PheromoneField.Instance.GetDig(Vector3Int.FloorToInt(transform.position));
        if (dph > 1f)
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
        int mask = LayerMask.GetMask("Terrain");

        if (Physics.Raycast(origin, dir, out RaycastHit hit, stickDistance, mask))
        {
            VoxelWorld.Instance.TryDigAt(hit.point, digRadius * 1.5f);
            PheromoneField.Instance.DepositDig(hit.point, pheromoneDepositAmount * 1.5f);
        }
        currentState = State.Roaming;
    }

    Vector3Int GetBestDigTarget()
    {
        Vector3Int cur = Vector3Int.FloorToInt(transform.position);
        float bestScore = 0f;
        Vector3Int bestT = Vector3Int.zero;

        Vector3Int[] offsets = {
            new Vector3Int(0, -1, 0), new Vector3Int(1, -1, 0), new Vector3Int(-1, -1, 0),
            new Vector3Int(0, -1, 1), new Vector3Int(0, -1, -1),
            new Vector3Int(1, 0, 0),  new Vector3Int(-1, 0, 0),
            new Vector3Int(0, 0, 1),  new Vector3Int(0, 0, -1),
        };

        foreach (var off in offsets)
        {
            Vector3Int chk = cur + off;
            float dph = PheromoneField.Instance.GetDig(chk);
            float tph = PheromoneField.Instance.GetTrail(chk);
            float score = dph + 0.1f - tph * 0.5f;
            if (off.y < 0) score += 0.2f;
            if (chk.y < 2 && chk.y > -10) score += 0.3f;
            if (score > bestScore)
            {
                bestScore = score;
                bestT = chk;
            }
        }
        return bestScore > 0.15f ? bestT : Vector3Int.zero;
    }

    Vector3 GetBestDigDirection()
    {
        Vector3Int cur = Vector3Int.FloorToInt(transform.position);
        Vector3 bestDir = transform.forward;
        float bestVal = 0f;

        // Scan the 3󫎿 neighborhood (preferring same level or downward)
        for (int x = -1; x <= 1; x++)
            for (int y = -1; y <= 0; y++)
                for (int z = -1; z <= 1; z++)
                {
                    Vector3Int offset = new Vector3Int(x, y, z);
                    float ph = PheromoneField.Instance.GetDig(cur + offset);
                    if (ph > bestVal)
                    {
                        bestVal = ph;
                        // build a Vector3 here, not a Vector3Int
                        bestDir = new Vector3(offset.x, offset.y, offset.z);
                    }
                }

        // now normalize the Vector3
        return bestDir.normalized;
    }
}
