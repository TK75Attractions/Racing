using UnityEngine;

public class TireForce : MonoBehaviour
{
    [SerializeField] private float Grip;
    private float k = 10f;
    private float friction= 100f;
    [SerializeField] private float sideVel;
    [Header("tire Setting")]
    public bool isFrontTire;
    public bool isDriveWheel;

    private Rigidbody carRb;

    public void Init(Rigidbody rb)
    {
        carRb = rb;
    }

    public void ApplyPhysics(float h, float p, float forceMultiplier, float torqueMultiplier)
    {
        if (carRb == null) return;

        //推進力計算
        if (isDriveWheel && p != 0)
        {
            Vector3 accelForce = transform.forward * p * forceMultiplier;

            carRb.AddForceAtPosition(accelForce * Time.deltaTime, transform.position, ForceMode.Acceleration);
        }
        //旋回
        if (isFrontTire)
        {
            transform.localRotation = Quaternion.Euler(0, h, 0);
        }
        //グリップ
        Vector3 sideDir = transform.right;
        Vector3 tireWorldVelocity = carRb.GetPointVelocity(transform.position);
        sideVel = Vector3.Dot(tireWorldVelocity, sideDir);

        Grip = (Grip + sideVel * Time.deltaTime) * 0.9f;
        float sideForceAmount =  -(sideVel * friction +Grip* k);
        Vector3 finalSideForce = sideForceAmount * sideDir;
        carRb.AddForceAtPosition(finalSideForce * Time.deltaTime, transform.position, ForceMode.Acceleration);





        
        

    }

}
