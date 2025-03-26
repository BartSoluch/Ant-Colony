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
    }
    void Roam()
    {
        transform.position += currentDirection * moveSpeed * Time.deltaTime;

        // Clamp position within voxel world bounds
        Vector3 pos = transform.position;
        float margin = 1f;
        float worldX = VoxelWorld.Instance.chunksX * 16; // replace 16 with your chunk width
        float worldZ = VoxelWorld.Instance.chunksZ * 16;
        pos.x = Mathf.Clamp(pos.x, margin, worldX - margin);
        pos.z = Mathf.Clamp(pos.z, margin, worldZ - margin);
        transform.position = pos;

        // Occasionally change direction randomly
        if (Time.time - lastDirectionUpdateTime > directionUpdateCooldown)
        {
            PickNewDirection();
            lastDirectionUpdateTime = Time.time;
        }

        // Look for best dig direction (based on pheromones)
        Vector3Int bestDigTarget = GetBestDigTarget();
        Debug.Log("Best Dig Target: " + bestDigTarget);

        if (bestDigTarget != Vector3Int.zero)
        {
            currentDirection = ((Vector3)(bestDigTarget - Vector3Int.FloorToInt(transform.position))).normalized;
            // Safe smooth turning
            if (currentDirection.sqrMagnitude > 0.01f)
            {
                Quaternion targetRot = Quaternion.LookRotation(currentDirection);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * 5f);
            }



            currentState = State.Digging;
        }
    }


    void TryDig()
    {
        if (Time.time - lastDigTime < digCooldown)
            return;

        lastDigTime = Time.time;

        Vector3 digPos = transform.position + currentDirection * 0.5f;
        digPos.y -= 0.5f;

        VoxelWorld.Instance.TryDigAt(digPos, radius: 2f);
        PheromoneField.Instance.Deposit(digPos, pheromoneDepositAmount);

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
        Debug.Log("Ant at: " + current);
        float bestPheromone = 0f;
        Vector3Int bestOffset = Vector3Int.zero;

        for (int x = -1; x <= 1; x++)
            for (int y = -1; y <= 0; y++) // downward bias
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
