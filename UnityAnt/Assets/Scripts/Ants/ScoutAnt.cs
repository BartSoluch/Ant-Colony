// ScoutAnt.cs
using UnityEngine;

[RequireComponent(typeof(Animator))]
public class ScoutAnt : AntAgent
{
    [Header("Ant Settings")]
    public float moveSpeed = 1.5f;
    public float pheromoneDepositAmount = 1f;
    public float directionUpdateCooldown = 2f;

    float lastDirectionUpdateTime;
    Animator animator;

    protected override void Awake()
    {
        base.Awake();
        animator = GetComponentInChildren<Animator>();
    }

    protected override void TickBehavior()
    {
        RoamScout();

        if (animator != null)
            animator.SetFloat("Speed", 1f);
    }

    void RoamScout()
    {
        if (!FindClimbableSurface(out Vector3 p, out Vector3 n)) return;

        smoothedNormal = Vector3.Slerp(smoothedNormal, n, Time.deltaTime * rotationSpeed);
        Vector3 move = Vector3.ProjectOnPlane(currentDirection, smoothedNormal).normalized;
        transform.position += move * moveSpeed * Time.deltaTime;

        PheromoneField.Instance.DepositTrail(transform.position, pheromoneDepositAmount * Time.deltaTime);

        if (Time.time - lastDirectionUpdateTime > directionUpdateCooldown)
        {
            PickNewDirection();
            lastDirectionUpdateTime = Time.time;
        }
    }
}
