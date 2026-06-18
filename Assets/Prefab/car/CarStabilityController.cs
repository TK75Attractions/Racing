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
        if (!HasGroundedWheel())
        {
            return;
        }

        float tiltAngle = Vector3.Angle(transform.up, Vector3.up);
        if (tiltAngle < assistStartAngle)
        {
            return;
        }

        float assist = Mathf.InverseLerp(assistStartAngle, fullAssistAngle, tiltAngle);
        Vector3 uprightAxis = Vector3.Cross(transform.up, Vector3.up);

        if (uprightAxis.sqrMagnitude < 0.0001f)
        {
            return;
        }

        uprightAxis.Normalize();

        Vector3 uprightTorque = uprightAxis * Mathf.Min(uprightStrength * assist, maxUprightTorque);
        rb.AddTorque(uprightTorque, ForceMode.Acceleration);

        Vector3 localAngularVelocity = transform.InverseTransformDirection(rb.angularVelocity);
        localAngularVelocity.y = 0f;
        Vector3 rollPitchAngularVelocity = transform.TransformDirection(localAngularVelocity);
        rb.AddTorque(-rollPitchAngularVelocity * angularDamping * assist, ForceMode.Acceleration);
    }

    private bool HasGroundedWheel()
    {
        if (tireGroundChecks == null || tireGroundChecks.Length == 0)
        {
            RefreshTireGroundChecks();
        }

        foreach (GroundCheck groundCheck in tireGroundChecks)
        {
            if (groundCheck != null && groundCheck.CheckNow())
            {
                return true;
            }
        }

        return false;
    }

    private void RefreshTireGroundChecks()
    {
        TireForce[] tires = GetComponentsInChildren<TireForce>();
        tireGroundChecks = new GroundCheck[tires.Length];

        for (int index = 0; index < tires.Length; index++)
        {
            tireGroundChecks[index] = tires[index].GetComponent<GroundCheck>();
        }
    }
}
