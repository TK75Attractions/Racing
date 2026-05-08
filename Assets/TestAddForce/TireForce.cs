using UnityEngine;

public class TireForce : MonoBehaviour
{
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
        //旋回力計算
        if (isFrontTire && h != 0)
        {
            
        }

    }

}
