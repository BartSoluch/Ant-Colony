using UnityEngine;

[RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
public abstract class AntAgent : MonoBehaviour
{
    [Header("Climb & Stick Settings")]
    public float stickSpeed = 10f;
    public float rotationSpeed = 8f;
    public float stickDistance = 1.2f;
    public float groundOffset = 0.5f;

    protected Vector3 smoothedNormal = Vector3.up;
    protected Vector3 currentDirection;

    Rigidbody rb;
    SphereCollider sphereCol;
    protected float sphereRadius;

    protected virtual void Awake()
    {
        rb = GetComponent<Rigidbody>();
        sphereCol = GetComponent<SphereCollider>();
        rb.useGravity = false;
        rb.isKinematic = true;

        // match our sphere-casts to the real collider size
        sphereRadius = sphereCol.radius * transform.localScale.y;

        PickNewDirection();
    }

    void Update()
    {
        // 1) subclass-specific logic
        TickBehavior();

        // 2) always stick/climb last
        ApplyStickyGravity();
    }

    // implemented by DiggerAnt, ScoutAnt, etc.
    protected abstract void TickBehavior();

    protected void ApplyStickyGravity()
    {
        if (FindClimbableSurface(out Vector3 p, out Vector3 n))
        {
            // blend into the new normal
            smoothedNormal = Vector3.Slerp(smoothedNormal, n, Time.deltaTime * rotationSpeed);

            // re-project our heading onto that plane
            currentDirection = Vector3.ProjectOnPlane(currentDirection, smoothedNormal).normalized;

            // bank the ant so up = smoothedNormal
            Quaternion targetRot = Quaternion.LookRotation(currentDirection, smoothedNormal);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * rotationSpeed);

            // hover at collider-radius + offset above the surface point
            float hover = sphereCol.radius * transform.localScale.y + groundOffset;
            Vector3 targetPos = p + smoothedNormal * hover;
            transform.position = Vector3.Lerp(transform.position, targetPos, Time.deltaTime * stickSpeed);
        }
        // no falling branch: ants never drop
    }

    protected bool FindClimbableSurface(out Vector3 hitPoint, out Vector3 hitNormal)
    {
        Vector3 origin = transform.position;
        int mask = ~LayerMask.GetMask("Ant");

        Vector3[] dirs = {
            transform.up,    -transform.up,
            transform.right, -transform.right,
            transform.forward, -transform.forward
        };

        // 1) sphere-cast in all 6 directions
        foreach (var d in dirs)
        {
            if (Physics.SphereCast(origin, sphereRadius, d, out RaycastHit h, stickDistance, mask))
            {
                hitPoint = h.point;
                hitNormal = h.normal;
                return true;
            }
        }

        // 2) fallback ray down for flat ground
        if (Physics.Raycast(origin + Vector3.up * 0.5f,
                            Vector3.down,
                            out RaycastHit fr,
                            stickDistance * 2f,
                            mask))
        {
            hitPoint = fr.point;
            hitNormal = fr.normal;
            return true;
        }

        hitPoint = origin;
        hitNormal = smoothedNormal;
        return false;
    }

    protected void PickNewDirection()
    {
        float ang = Random.Range(0f, 360f) * Mathf.Deg2Rad;
        Vector3 rand = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
        currentDirection = Vector3.ProjectOnPlane(rand, smoothedNormal).normalized;
        transform.rotation = Quaternion.LookRotation(currentDirection, smoothedNormal);
    }
}
