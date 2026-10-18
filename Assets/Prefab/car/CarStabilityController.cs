using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class CarStabilityController : MonoBehaviour
{
    [SerializeField] private Vector3 centerOfMass = new Vector3(0f, -.2f, 0f);
    [SerializeField] private float assistStartAngle = 8f;
    [SerializeField] private float fullAssistAngle = 90f;
    [SerializeField] private float uprightStrength = 8f;
    [SerializeField] private float angularDamping = 6f;
    [SerializeField] private float maxUprightTorque = 20f;

    [Header("Road Contact")]
    [Tooltip("タイヤが路面からこの距離以内にある時だけ接地を補助します。ジャンプ中には適用しません。")]
    [SerializeField, Min(0f)] private float roadContactRange = .18f;
    [SerializeField, Min(0f)] private float roadDownforce = 6f;
    [SerializeField, Min(0f)] private float speedDownforce = .04f;
    [SerializeField, Min(0f)] private float maximumDownforce = 32f;
    [Tooltip("路面から離れる速度がこれ以上ならジャンプとして補助を止めます。")]
    [SerializeField, Min(0f)] private float jumpReleaseSpeed = 2.5f;

    [Header("Airborne Stability")]
    [Tooltip("空中で車体の上方向を鉛直へ戻す強さ。進行方向や上下速度は変更しません。")]
    [SerializeField, Min(0f)] private float airborneUprightStrength = 12f;
    [Tooltip("空中の縦・横回転を抑える係数。旋回の回転は残します。")]
    [SerializeField, Min(0f)] private float airborneAngularDamping = 6f;

    [Tooltip("車体が裏返って地面に乗った場合だけ、広い車体コライダーを起こすトルクを増やします。")]
    [SerializeField, Range(1f, 5f)] private float overturnedRecoveryMultiplier = 3f;

    [Header("Runtime Toggle")]
    [Tooltip("空中・接地中の姿勢補助を適用するか。プレイ中の原因切り分け用。")]
    [SerializeField] private bool enableUprightAssist = true;

    private Rigidbody rb;
    private GroundCheck[] tireGroundChecks;
    private Collider[] bodyColliders;
    private bool overturnedRecoveryActive;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.centerOfMass = centerOfMass;
        RefreshTireGroundChecks();
        bodyColliders = GetComponentsInChildren<Collider>();
    }

    private void FixedUpdate()
    {
        if (!enableUprightAssist || rb.isKinematic) return;

        bool grounded = TryGetGroundNormal(out Vector3 targetUp);
        ApplyRoadContact();
        if (!grounded) targetUp = Vector3.up;

        // 坂では路面の法線へ、空中では鉛直へ。回転を瞬間的に書き換えずトルクで戻します。
        Vector3 vehicleUp = rb.rotation * Vector3.up;
        float tiltAngle = Vector3.Angle(vehicleUp, targetUp);
        float assist = grounded
            ? Mathf.InverseLerp(assistStartAngle, fullAssistAngle, tiltAngle)
            : 1f;
        Vector3 uprightAxis = Vector3.Cross(vehicleUp, targetUp);
        if (uprightAxis.sqrMagnitude < 0.0001f && Vector3.Dot(vehicleUp, targetUp) < 0f)
        {
            // 完全な裏返しでも復元軸を決めます。前後方向を軸に起こし、向きを反転させません。
            uprightAxis = Vector3.ProjectOnPlane(rb.rotation * Vector3.forward, targetUp);
            if (uprightAxis.sqrMagnitude < 0.0001f)
                uprightAxis = Vector3.ProjectOnPlane(rb.rotation * Vector3.right, targetUp);
        }

        float upDot = Vector3.Dot(vehicleUp, Vector3.up);
        bool supportedOverturn = !grounded && upDot < 0.5f &&
            (overturnedRecoveryActive || upDot < -0.5f) && IsBodySupported();
        overturnedRecoveryActive = supportedOverturn;
        float recovery = supportedOverturn ? overturnedRecoveryMultiplier : 1f;
        float strength = (grounded ? uprightStrength : airborneUprightStrength) * recovery;
        float damping = grounded ? angularDamping : airborneAngularDamping;
        Vector3 correction = uprightAxis.normalized * (tiltAngle * Mathf.Deg2Rad * strength * assist);
        // 路面法線まわりの旋回は保ち、離陸時の回転がそのまま転倒へ育つのを防ぎます。
        Vector3 rollPitchVelocity = Vector3.ProjectOnPlane(rb.angularVelocity, targetUp);
        Vector3 torque = correction - rollPitchVelocity * damping;
        rb.AddTorque(Vector3.ClampMagnitude(torque, Mathf.Max(0f, maxUprightTorque) * recovery), ForceMode.Acceleration);
    }

    private void ApplyRoadContact()
    {
        Vector3 normal = Vector3.zero;
        int supportedWheels = 0;
        foreach (GroundCheck wheel in tireGroundChecks)
        {
            if (wheel == null || !wheel.TryGetNearbyGround(roadContactRange, out RaycastHit hit)) continue;
            Rigidbody support = hit.collider.attachedRigidbody;
            if (support != null && !support.isKinematic) continue;
            normal += hit.normal;
            supportedWheels++;
        }
        // Two wheels and an upright body are needed: walls, another car and flight provide no adhesion.
        if (supportedWheels < 2) return;
        normal.Normalize();
        if (Vector3.Dot(rb.rotation * Vector3.up, normal) < .7f) return;
        float departingSpeed = Vector3.Dot(rb.linearVelocity, normal);
        if (departingSpeed > jumpReleaseSpeed) return;
        float speedSquared = Vector3.ProjectOnPlane(rb.linearVelocity, normal).sqrMagnitude;
        float acceleration = roadDownforce + speedSquared * speedDownforce + Mathf.Max(0f, departingSpeed) * 12f;
        rb.AddForce(-normal * Mathf.Min(maximumDownforce, acceleration), ForceMode.Acceleration);
    }

    private bool IsBodySupported()
    {
        // 空中では強さを変えず、車体コライダーが地面に乗った裏返し状態だけを補助します。
        float lowest = float.MaxValue;
        foreach (Collider collider in bodyColliders)
            if (collider != null && collider.enabled && !collider.isTrigger && collider.attachedRigidbody == rb)
                lowest = Mathf.Min(lowest, collider.bounds.min.y);
        if (lowest == float.MaxValue) return false;
        Vector3 origin = rb.worldCenterOfMass + Vector3.up * 0.02f;
        float distance = Mathf.Max(0f, origin.y - lowest) + 0.08f;
        foreach (RaycastHit hit in Physics.RaycastAll(origin, Vector3.down, distance, ~0, QueryTriggerInteraction.Ignore))
            if (hit.collider != null && hit.collider.attachedRigidbody != rb && hit.normal.y >= 0.2f &&
                (hit.collider.attachedRigidbody == null || hit.collider.attachedRigidbody.isKinematic)) return true;
        return false;
    }

    private bool TryGetGroundNormal(out Vector3 normal)
    {
        normal = Vector3.zero;
        if (tireGroundChecks == null || tireGroundChecks.Length == 0) RefreshTireGroundChecks();

        int count = 0;
        foreach (GroundCheck groundCheck in tireGroundChecks)
        {
            if (groundCheck != null && groundCheck.CheckNow())
            {
                normal += groundCheck.GroundHit.normal;
                count++;
            }
        }
        if (count == 0) return false;
        normal.Normalize();
        return true;
    }

    private void RefreshTireGroundChecks()
    {
        TireForce[] tires = GetComponentsInChildren<TireForce>();
        tireGroundChecks = new GroundCheck[tires.Length];
        for (int index = 0; index < tires.Length; index++)
            tireGroundChecks[index] = tires[index].GetComponent<GroundCheck>();
    }
}
