using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class CarStabilityController : MonoBehaviour
{
    [SerializeField] private Vector3 centerOfMass = Vector3.zero;
    [SerializeField] private float assistStartAngle = 35f;
    [SerializeField] private float fullAssistAngle = 90f;
    [SerializeField] private float uprightStrength = 8f;
    [SerializeField] private float angularDamping = 2f;
    [SerializeField] private float maxUprightTorque = 20f;

    [Header("Airborne Stability")]
    [Tooltip("空中で車体の上方向を鉛直へ戻す強さ。進行方向や上下速度は変更しません。")]
    [SerializeField, Min(0f)] private float airborneUprightStrength = 12f;
    [Tooltip("空中の縦・横回転を抑える係数。旋回の回転は残します。")]
    [SerializeField, Min(0f)] private float airborneAngularDamping = 6f;

    [Header("Runtime Toggle")]
    [Tooltip("空中・接地中の姿勢補助を適用するか。プレイ中の原因切り分け用。")]
    [SerializeField] private bool enableUprightAssist = true;

    private Rigidbody rb;
    private GroundCheck[] tireGroundChecks;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.centerOfMass = centerOfMass;
        RefreshTireGroundChecks();
    }

    private void FixedUpdate()
    {
        if (!enableUprightAssist || rb.isKinematic) return;

        bool grounded = TryGetGroundNormal(out Vector3 targetUp);
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

        float strength = grounded ? uprightStrength : airborneUprightStrength;
        float damping = grounded ? angularDamping : airborneAngularDamping;
        Vector3 correction = uprightAxis.normalized * (tiltAngle * Mathf.Deg2Rad * strength * assist);
        // 路面法線まわりの旋回は保ち、離陸時の回転がそのまま転倒へ育つのを防ぎます。
        Vector3 rollPitchVelocity = Vector3.ProjectOnPlane(rb.angularVelocity, targetUp);
        Vector3 torque = correction - rollPitchVelocity * damping;
        rb.AddTorque(Vector3.ClampMagnitude(torque, Mathf.Max(0f, maxUprightTorque)), ForceMode.Acceleration);
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
