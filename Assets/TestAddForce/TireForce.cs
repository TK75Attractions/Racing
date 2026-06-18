using UnityEngine;

[RequireComponent(typeof(GroundCheck))]
public class TireForce : MonoBehaviour
{
    [SerializeField] private float Grip;
    private float k = 10f;
    private float friction = 100f;
    [SerializeField] private float sideVel;
    [SerializeField] private bool isGrounded;

    [Header("tire Setting")]
    public bool isFrontTire;
    public bool isDriveWheel;

    private Rigidbody carRb;
    private GroundCheck groundCheck;

    private void Awake()
    {
        groundCheck = GetComponent<GroundCheck>();
        if (groundCheck == null)
        {
            groundCheck = gameObject.AddComponent<GroundCheck>();
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

    public void ApplyPhysics(float h, float p, float forceMultiplier, float torqueMultiplier)
    {
        if (carRb == null)
        {
            return;
        }

        if (isFrontTire)
        {
            transform.localRotation = Quaternion.Euler(0, h, 0);
        }

        isGrounded = groundCheck != null && groundCheck.CheckNow();
        if (!isGrounded)
        {
            sideVel = 0f;
            Grip *= 0.98f;
            return;
        }

        if (isDriveWheel && p != 0)
        {
            Vector3 accelForce = transform.forward * p * forceMultiplier;
            carRb.AddForceAtPosition(accelForce * Time.deltaTime, transform.position, ForceMode.Acceleration);
        }

        Vector3 sideDir = transform.right;
        Vector3 tireWorldVelocity = carRb.GetPointVelocity(transform.position);
        sideVel = Vector3.Dot(tireWorldVelocity, sideDir);

        float dt = Time.fixedDeltaTime;
        Grip = (Grip + sideVel * dt) * 0.98f;
        Grip = Mathf.Clamp(Grip, -1f, 1f);

        float sideForceAmount = -(sideVel * friction + Grip * k);
        Vector3 finalSideForce = sideForceAmount * sideDir;
        carRb.AddForceAtPosition(finalSideForce * Time.deltaTime, transform.position, ForceMode.Acceleration);
    }
}
