using UnityEngine;
using UnityEngine.InputSystem;
public class DebugMover : MonoBehaviour
{
    [SerializeField] private float forceMultiplier = 10f;
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
        float h = Gmanager.Control.IManager.handle;
        if(rb ==null) return;
        Vector3 moveDir = Vector3.zero;

        if (Keyboard.current.upArrowKey.isPressed) moveDir += transform.forward;
        if (Keyboard.current.downArrowKey.isPressed)  moveDir += Vector3.back;
        if (Keyboard.current.leftArrowKey.isPressed)  moveDir += Vector3.left;
        if (Keyboard.current.rightArrowKey.isPressed) moveDir += Vector3.right;

        if (moveDir != Vector3.zero)
        {
            rb.AddForce(moveDir * forceMultiplier, ForceMode.Acceleration);
        }
    }
}
