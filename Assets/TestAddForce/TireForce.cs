using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(GroundCheck))]
public class TireForce : MonoBehaviour
{
    [Header("Wheel Role")]
    [Tooltip("前輪の場合に有効。前輪は操舵輪かつ駆動輪として扱われます。")]
    [SerializeField] private bool isFrontTire;

    [Header("Runtime Monitor")]
    [SerializeField] private bool isGrounded;
    [SerializeField] private float lateralVelocity;
    [SerializeField] private float appliedDriveForce;
    [SerializeField] private float appliedLateralForce;

    [Header("表示専用ホイール")]
    [SerializeField, Min(0.05f)] private float visualWheelRadius = .34f;
    [SerializeField, Min(0.1f)] private float maximumVisibleWheelSpeed = 36f;

    private Rigidbody carRb;
    private GroundCheck groundCheck;
    private Quaternion initialLocalRotation;
    private readonly List<Transform> wheelVisuals = new List<Transform>();
    private readonly List<Vector3> wheelVisualBasePositions = new List<Vector3>();
    private readonly List<Quaternion> wheelVisualBaseRotations = new List<Quaternion>();
    private float visibleWheelAngle;

    public bool IsFrontWheel => isFrontTire;
    /// <summary>直近の物理フレームで接地していたか。タイヤ痕や煙などの表示用です。</summary>
    public bool IsGrounded => isGrounded;
    /// <summary>タイヤの向きに対する横滑りの速さ（m/s、絶対値）。</summary>
    public float LateralSlipSpeed => Mathf.Abs(lateralVelocity);
    public RaycastHit GroundHit => groundCheck != null ? groundCheck.GroundHit : default;

    private void Awake()
    {
        groundCheck = GetComponent<GroundCheck>();
        initialLocalRotation = transform.localRotation;
        foreach (Transform child in transform)
        {
            if (child.GetComponentInChildren<MeshRenderer>(true) == null ||
                child.GetComponentInChildren<Collider>(true) != null ||
                child.GetComponentInChildren<TireForce>(true) != null ||
                child.GetComponentInChildren<Rigidbody>(true) != null) continue;
            wheelVisuals.Add(child);
            wheelVisualBasePositions.Add(child.localPosition);
            wheelVisualBaseRotations.Add(child.localRotation);
        }
    }

    public void Init(Rigidbody rb)
    {
        carRb = rb;

        if (groundCheck == null)
        {
            groundCheck = GetComponent<GroundCheck>();
        }
    }

    public void CenterVisualSteering()
    {
        UpdateVisualSteering(0f);
    }

    public void ApplyForces(
        Vector3 vehicleForward,
        Vector3 vehicleUp,
        float steeringAngle,
        float pedalInput,
        float driveForcePerFrontWheel,
        float corneringStiffness,
        float maxLateralForce,
        bool enableLateralForce,
        bool enableDriveForce)
    {
        if (carRb == null || groundCheck == null)
        {
            return;
        }

        UpdateVisualSteering(steeringAngle);
        UpdateWheelVisuals(Time.fixedDeltaTime);

        isGrounded = groundCheck.CheckNow();
        if (!isGrounded)
        {
            ResetMonitorValues();
            return;
        }

        Vector3 groundNormal = groundCheck.GroundHit.normal;
        Vector3 tireForward = GetTireForward(vehicleForward, vehicleUp, groundNormal, steeringAngle);
        Vector3 tireRight = Vector3.Cross(groundNormal, tireForward).normalized;
        // タイヤの前後・左右位置は保ち、力点を路面に平行な重心の平面へ移します。
        // 操舵の旋回トルクは残し、傾いたタイヤへの駆動・横力で車体をひっくり返しません。
        Vector3 tirePosition = carRb.worldCenterOfMass +
            Vector3.ProjectOnPlane(transform.position - carRb.worldCenterOfMass, groundNormal);
        Vector3 pointVelocity = carRb.GetPointVelocity(tirePosition);
        // Visual slip stays current even while lateral tire forces are disabled (e.g. oil).
        lateralVelocity = Vector3.Dot(pointVelocity, tireRight);

        if (enableLateralForce)
        {
            ApplyLateralForce(tirePosition, tireRight, corneringStiffness, maxLateralForce);
        }
        else
        {
            appliedLateralForce = 0f;
        }

        if (enableDriveForce)
        {
            ApplyFrontDriveForce(tirePosition, tireForward, pedalInput, driveForcePerFrontWheel);
        }
        else
        {
            appliedDriveForce = 0f;
        }
    }

    private Vector3 GetTireForward(
        Vector3 vehicleForward,
        Vector3 vehicleUp,
        Vector3 groundNormal,
        float steeringAngle)
    {
        Vector3 forward = vehicleForward;
        if (isFrontTire)
        {
            forward = Quaternion.AngleAxis(steeringAngle, vehicleUp) * forward;
        }

        forward = Vector3.ProjectOnPlane(forward, groundNormal);
        return forward.sqrMagnitude > 0.0001f ? forward.normalized : vehicleForward;
    }

    private void ApplyLateralForce(
        Vector3 tirePosition,
        Vector3 tireRight,
        float corneringStiffness,
        float maxLateralForce)
    {
        appliedLateralForce = Mathf.Clamp(
            -lateralVelocity * corneringStiffness,
            -maxLateralForce,
            maxLateralForce);

        carRb.AddForceAtPosition(
            tireRight * appliedLateralForce,
            tirePosition,
            ForceMode.Force);
    }

    private void ApplyFrontDriveForce(
        Vector3 tirePosition,
        Vector3 tireForward,
        float pedalInput,
        float driveForcePerFrontWheel)
    {
        if (!isFrontTire)
        {
            appliedDriveForce = 0f;
            return;
        }

        appliedDriveForce = pedalInput * driveForcePerFrontWheel;
        carRb.AddForceAtPosition(
            tireForward * appliedDriveForce,
            tirePosition,
            ForceMode.Force);
    }

    private void UpdateVisualSteering(float steeringAngle)
    {
        float visualAngle = isFrontTire ? steeringAngle : 0f;
        transform.localRotation = initialLocalRotation * Quaternion.Euler(0f, visualAngle, 0f);
    }

    private void UpdateWheelVisuals(float deltaTime)
    {
        float speed = carRb != null ? Vector3.Dot(carRb.GetPointVelocity(transform.position), transform.forward) : 0f;
        // Keep the low-speed roll cue, then cap visible rotation to avoid strobing at racing speed.
        float angularSpeed = Mathf.Clamp(speed / Mathf.Max(.05f, visualWheelRadius),
            -maximumVisibleWheelSpeed, maximumVisibleWheelSpeed);
        visibleWheelAngle = Mathf.Repeat(visibleWheelAngle + angularSpeed * deltaTime * Mathf.Rad2Deg, 360f);
        Vector3 suspensionOffset = Vector3.zero;
        if (isGrounded && groundCheck != null)
        {
            Vector3 normal = groundCheck.GroundHit.normal;
            if (normal.sqrMagnitude > .001f)
                suspensionOffset = transform.InverseTransformVector(
                    normal.normalized * (visualWheelRadius - Vector3.Dot(transform.position - groundCheck.GroundHit.point, normal.normalized)));
        }
        Quaternion spin = Quaternion.AngleAxis(visibleWheelAngle, Vector3.right);
        for (int index = 0; index < wheelVisuals.Count; index++)
        {
            Transform visual = wheelVisuals[index];
            if (visual == null) continue;
            visual.localPosition = wheelVisualBasePositions[index] + suspensionOffset;
            visual.localRotation = wheelVisualBaseRotations[index] * spin;
        }
    }

    private void ResetMonitorValues()
    {
        lateralVelocity = 0f;
        appliedDriveForce = 0f;
        appliedLateralForce = 0f;
    }
}
