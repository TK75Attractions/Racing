using UnityEngine;
using UnityEngine.InputSystem;
public class DebugMover : MonoBehaviour
{
    [SerializeField] private float forceMultiplier = 1000f;
    [SerializeField] private float torqueMultiplier = 15f;
    [SerializeField] private float h;
    [SerializeField] private float p;
    [SerializeField] private float v;
    private Rigidbody rb;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        rb = GetComponent<Rigidbody>();

        if (rb == null)
        {
            Debug.LogError($"{gameObject.name} に Rigidbody が付いていません！AddForceできません。");
        }
    }

    // Update is called once per frame
    void Update()
    {
        if(rb ==null) return;
        h = Gmanager.Control.IManager.handle;
        p = Gmanager.Control.IManager.peddale;
        
        v = rb.linearVelocity.magnitude;



        if (p != 0)
        {
            Vector3 force = transform.forward * p * forceMultiplier;
            Vector3 forcePoint = transform.TransformPoint(new Vector3(0,-0.5f,-2f));
            rb.AddForceAtPosition(force * Time.deltaTime,forcePoint,  ForceMode.Acceleration);
        }
        
        if (h != 0)
        {
            float rotationAngle = Mathf.Sign(h) * Mathf.Abs(h) + 90f);
            Quaternion rotation = Quaternion.AngleAxis(rotationAngle, transform.up);
            Vector3 forceDirection = rotation * transform.forward;
            Vector3 force = forceDirection * Mathf.Abs(h) * v * v *torqueMultiplier;
            Vector3 frontPoint = transform.TransformPoint(new Vector3(0, 0,2f));
            rb.AddForceAtPosition(force * Time.deltaTime, frontPoint, ForceMode.Acceleration);
            //Vector3 torque = Vector3.up * h * torqueMultiplier;
            //rb.AddTorque(torque * Time.deltaTime, ForceMode.Acceleration);
        }
        
    }
}
