using UnityEngine;

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

    private Rigidbody carRb;
    private GroundCheck groundCheck;
    private Quaternion initialLocalRotation;

    public bool IsFrontWheel => isFrontTire;

    private void Awake()
    {
        groundCheck = GetComponent<GroundCheck>();
        initialLocalRotation = transform.localRotation;
    }

    public void Init(Rigidbody rb)
    {
        carRb = rb;

        if (groundCheck == null)
        {
            groundCheck = GetComponent<GroundCheck>();
        }
    }

    public void ApplyForces(
        Vector3 vehicleForward,
        Vector3 vehicleUp,
        float steeringAngle,
        float pedalInput,
        float driveForcePerFrontWheel,
        float corneringStiffness,
        float maxLateralForce)
    {
        if (carRb == null || groundCheck == null)
        {
            return;
        }

        UpdateVisualSteering(steeringAngle);

        isGrounded = groundCheck.CheckNow();
        if (!isGrounded)
        {
            ResetMonitorValues();
            return;
        }

        Vector3 groundNormal = groundCheck.GroundHit.normal;
        Vector3 tireForward = GetTireForward(vehicleForward, vehicleUp, groundNormal, steeringAngle);
        Vector3 tireRight = Vector3.Cross(groundNormal, tireForward).normalized;
        Vector3 tirePosition = transform.position;
        Vector3 pointVelocity = carRb.GetPointVelocity(tirePosition);

        ApplyLateralForce(tirePosition, tireRight, pointVelocity, corneringStiffness, maxLateralForce);
        ApplyFrontDriveForce(tirePosition, tireForward, pedalInput, driveForcePerFrontWheel);
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
        Vector3 pointVelocity,
        float corneringStiffness,
        float maxLateralForce)
    {
        lateralVelocity = Vector3.Dot(pointVelocity, tireRight);
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

    private void ResetMonitorValues()
    {
        lateralVelocity = 0f;
        appliedDriveForce = 0f;
        appliedLateralForce = 0f;
    }
}
