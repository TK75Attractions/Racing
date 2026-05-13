using UnityEngine;
using UnityEngine.InputSystem;

public class CarResetter : MonoBehaviour
{
    private Vector3 initialPosition;
    private Quaternion initialRotation;
    private Rigidbody rb;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        //初期位置と回転を記録
        initialPosition = transform.position;
        initialRotation = transform.rotation;

        rb = GetComponent<Rigidbody>();
    }

    // Update is called once per frame
    void Update()
    {
        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            //位置と回転をリセット
            transform.position = initialPosition;
            transform.rotation = initialRotation;
            //速度リセット
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            Debug.Log("Car Reset");
        }
    }
}
