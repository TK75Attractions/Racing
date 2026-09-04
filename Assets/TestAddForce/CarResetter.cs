using UnityEngine;

public class CarResetter : MonoBehaviour
{
    private Vector3 initialPosition;
    private Quaternion initialRotation;
    private Rigidbody rb;
    private DebugMover debugMover;
    private IDriveInputSource inputSource;

    public void SetInputSource(IDriveInputSource source)
    {
        inputSource = source;
    }

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        debugMover = GetComponent<DebugMover>();
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        //初期位置と回転を記録
        initialPosition = transform.position;
        initialRotation = transform.rotation;
    }

    // Update is called once per frame
    void Update()
    {
        if (debugMover != null && debugMover.IsInputSuppressed)
        {
            return;
        }

        if (inputSource != null && inputSource.CurrentState.resetPressed)
        {
            ResetCar();
        }
    }

    public void ResetCar()
    {
        if (rb != null)
        {
            // Rigidbodyを直接リセットし、次の物理更新へ古い回転を持ち越さない。
            rb.position = initialPosition;
            rb.rotation = initialRotation;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        else
        {
            transform.SetPositionAndRotation(initialPosition, initialRotation);
        }

        debugMover?.SuppressInputAfterRespawn();

        Debug.Log($"{name} Reset");
    }
}
