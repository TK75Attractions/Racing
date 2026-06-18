using UnityEngine;

public class GroundCheck : MonoBehaviour
{
    [SerializeField] private float checkDistance = 1.0f;
    [SerializeField] private Vector3 rayOriginOffset = Vector3.zero;
    [SerializeField] private LayerMask groundLayers = ~0;
    [SerializeField] private float minGroundNormalY = 0.2f;

    public bool isGround;
    public RaycastHit GroundHit { get; private set; }

    private Rigidbody ownerRigidbody;

    private void Awake()
    {
        ownerRigidbody = GetComponentInParent<Rigidbody>();
    }

    private void FixedUpdate()
    {
        CheckNow();
    }

    public bool CheckNow()
    {
        Vector3 origin = transform.position + rayOriginOffset;
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, checkDistance, groundLayers, QueryTriggerInteraction.Ignore);

        bool foundGround = false;
        float nearestDistance = float.MaxValue;

        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null)
            {
                continue;
            }

            if (ownerRigidbody != null && hit.collider.attachedRigidbody == ownerRigidbody)
            {
                continue;
            }

            if (hit.normal.y < minGroundNormalY)
            {
                continue;
            }

            if (hit.distance >= nearestDistance)
            {
                continue;
            }

            nearestDistance = hit.distance;
            GroundHit = hit;
            foundGround = true;
        }

        isGround = foundGround;
        return isGround;
    }
}
