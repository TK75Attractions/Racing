using UnityEngine;

public class GroundCheck : MonoBehaviour
{
    [SerializeField] private float checkDistance = 1.0f;
    [SerializeField] private Vector3 rayOriginOffset = Vector3.zero;
    [SerializeField] private LayerMask groundLayers = ~0;
    [SerializeField] private float minGroundNormalY = 0.2f;
    [Tooltip("タイヤ表面と路面の接地許容距離。空中で路面が見えても駆動力は出しません。")]
    [SerializeField, Min(0f)] private float wheelContactTolerance = 0.08f;
    [Tooltip("路面法線と車体上方向の内積の下限。横転・裏返し状態を接地として扱いません。")]
    [SerializeField, Range(0f, 1f)] private float minimumWheelUpDot = 0.5f;

    public bool isGround;
    public RaycastHit GroundHit { get; private set; }

    private Rigidbody ownerRigidbody;
    private SphereCollider wheelCollider;

    private void Awake()
    {
        ownerRigidbody = GetComponentInParent<Rigidbody>();
        if (GetComponent<TireForce>() != null) wheelCollider = GetComponent<SphereCollider>();
    }

    private void FixedUpdate()
    {
        CheckNow();
    }

    public bool CheckNow()
    {
        Vector3 wheelUp = ownerRigidbody != null ? ownerRigidbody.rotation * Vector3.up : Vector3.up;
        Vector3 origin = transform.position + rayOriginOffset;
        if (wheelCollider != null) origin = transform.TransformPoint(wheelCollider.center) + rayOriginOffset;
        RaycastHit[] hits = Physics.RaycastAll(origin, -wheelUp, checkDistance, groundLayers, QueryTriggerInteraction.Ignore);

        bool foundGround = false;
        float nearestDistance = float.MaxValue;
        GroundHit = default;
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null ||
                (ownerRigidbody != null && hit.collider.attachedRigidbody == ownerRigidbody) ||
                hit.normal.y < minGroundNormalY) continue;

            float alignment = Vector3.Dot(wheelUp, hit.normal);
            if (alignment < minimumWheelUpDot) continue;
            if (wheelCollider != null)
            {
                if (!wheelCollider.enabled || wheelCollider.isTrigger) continue;
                Vector3 scale = transform.lossyScale;
                float radius = wheelCollider.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                // 斜面ではレイ距離より面までの垂直距離で判定します。
                if (hit.distance * alignment > radius + wheelContactTolerance) continue;
            }
            if (hit.distance >= nearestDistance) continue;

            nearestDistance = hit.distance;
            GroundHit = hit;
            foundGround = true;
        }
        isGround = foundGround;
        return isGround;
    }
}
