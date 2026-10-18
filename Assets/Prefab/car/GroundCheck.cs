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
        isGround = TryGetSurface(wheelContactTolerance, out RaycastHit hit);
        GroundHit = hit;
        return isGround;
    }

    /// <summary>接地判定を変えず、微小な浮き上がりを抑える補助用の路面を探します。</summary>
    public bool TryGetNearbyGround(float surfaceGap, out RaycastHit hit) => TryGetSurface(surfaceGap, out hit);

    public float GetUpcomingStepRise(Vector3 velocity, Vector3 normal, Vector3 supportPoint, float maximumHeight)
    {
        if (wheelCollider == null || !wheelCollider.enabled || wheelCollider.isTrigger) return 0f;
        Vector3 travel = Vector3.ProjectOnPlane(velocity, normal);
        if (travel.sqrMagnitude < 1f) return 0f;
        Vector3 scale = transform.lossyScale;
        float radius = wheelCollider.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        Vector3 futureCenter = transform.TransformPoint(wheelCollider.center) + travel * Time.fixedDeltaTime +
            travel.normalized * Mathf.Max(radius, 1.3f);
        float rise = 0f;
        foreach (RaycastHit hit in Physics.RaycastAll(futureCenter + normal * maximumHeight, -normal,
                     checkDistance + maximumHeight, groundLayers, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.attachedRigidbody == ownerRigidbody ||
                (hit.collider.attachedRigidbody != null && !hit.collider.attachedRigidbody.isKinematic) ||
                Vector3.Dot(hit.normal, normal) < .995f) continue;
            float required = Vector3.Dot(hit.point - futureCenter, normal) + radius + .015f;
            float stepHeight = Vector3.Dot(hit.point - supportPoint, normal);
            if (stepHeight > .025f && stepHeight <= maximumHeight && required > .025f &&
                required <= maximumHeight + .08f) rise = Mathf.Max(rise, required);
        }
        return rise;
    }

    private bool TryGetSurface(float surfaceGap, out RaycastHit surface)
    {
        Vector3 wheelUp = ownerRigidbody != null ? ownerRigidbody.rotation * Vector3.up : Vector3.up;
        Vector3 origin = transform.position + rayOriginOffset;
        if (wheelCollider != null) origin = transform.TransformPoint(wheelCollider.center) + rayOriginOffset;
        RaycastHit[] hits = Physics.RaycastAll(origin, -wheelUp, checkDistance, groundLayers, QueryTriggerInteraction.Ignore);

        bool foundGround = false;
        float nearestDistance = float.MaxValue;
        surface = default;
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null ||
                (ownerRigidbody != null && hit.collider.attachedRigidbody == ownerRigidbody) ||
                (hit.normal.y < minGroundNormalY && !TubeDriveSurface.Contains(hit.collider))) continue;

            float alignment = Vector3.Dot(wheelUp, hit.normal);
            if (alignment < minimumWheelUpDot) continue;
            if (wheelCollider != null)
            {
                if (!wheelCollider.enabled || wheelCollider.isTrigger) continue;
                Vector3 scale = transform.lossyScale;
                float radius = wheelCollider.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                // 斜面ではレイ距離より面までの垂直距離で判定します。
                if (hit.distance * alignment > radius + surfaceGap) continue;
            }
            if (hit.distance >= nearestDistance) continue;

            nearestDistance = hit.distance;
            surface = hit;
            foundGround = true;
        }
        return foundGround;
    }
}
