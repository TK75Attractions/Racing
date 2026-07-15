using UnityEngine;
using Unity.Cinemachine;
using Unity.Cinemachine.TargetTracking;

/// <summary>
/// 車の後方から車体の前方向を映すための
/// Cinemachine 用の追従・注視ターゲットを管理します。
/// </summary>
[DefaultExecutionOrder(-100)]
public sealed class RaceDirectionCameraController : MonoBehaviour
{
    [Header("Camera Position")]
    [SerializeField, Min(0.01f)] private float cameraDistance = 5f;
    [SerializeField, Min(0f)] private float cameraHeight = 2f;
    [SerializeField, Min(0f)] private float rotationSmoothTime = 0.15f;

    private Transform car;
    private Transform cameraTarget;
    private Transform cameraLookTarget;
    private CinemachineFollow cinemachineFollow;
    private Vector3 lastCarDirection = Vector3.forward;
    private float yawVelocity;

    public Transform CameraTarget => cameraTarget;
    public Transform LookTarget => cameraLookTarget;

    public void SetCamera(CinemachineCamera virtualCamera)
    {
        cinemachineFollow = virtualCamera != null
            ? virtualCamera.GetComponent<CinemachineFollow>()
            : null;
        ConfigureCinemachineFollow();
    }

    public void SetCar(Transform targetCar)
    {
        car = targetCar;
        EnsureCameraTarget();
        UpdateCameraTarget(immediately: true);
    }

    public void ClearCar()
    {
        car = null;
        yawVelocity = 0f;

        if (cameraTarget != null)
        {
            Destroy(cameraTarget.gameObject);
            cameraTarget = null;
        }

        if (cameraLookTarget != null)
        {
            Destroy(cameraLookTarget.gameObject);
            cameraLookTarget = null;
        }
    }

    private void LateUpdate()
    {
        UpdateCameraTarget(immediately: false);
    }

    private void OnDestroy()
    {
        ClearCar();
    }

    private void EnsureCameraTarget()
    {
        if (cameraTarget == null)
        {
            cameraTarget = new GameObject("RaceCameraTarget").transform;
        }

        if (cameraLookTarget == null)
        {
            cameraLookTarget = new GameObject("RaceCameraLookTarget").transform;
        }
    }

    private void UpdateCameraTarget(bool immediately)
    {
        if (car == null || cameraTarget == null || cameraLookTarget == null)
        {
            return;
        }

        Vector3 carDirection = GetCarDirection();
        float targetYaw = Mathf.Atan2(carDirection.x, carDirection.z) * Mathf.Rad2Deg;
        float yaw;

        if (immediately)
        {
            yawVelocity = 0f;
            yaw = targetYaw;
        }
        else
        {
            yaw = Mathf.SmoothDampAngle(
                cameraTarget.eulerAngles.y,
                targetYaw,
                ref yawVelocity,
                rotationSmoothTime);
        }

        Quaternion cameraRotation = Quaternion.Euler(0f, yaw, 0f);
        Vector3 cameraPosition = car.position
            - cameraRotation * Vector3.forward * Mathf.Max(0.01f, cameraDistance)
            + Vector3.up * Mathf.Max(0f, cameraHeight);

        cameraTarget.SetPositionAndRotation(cameraPosition, cameraRotation);

        cameraLookTarget.SetPositionAndRotation(
            car.position,
            cameraRotation);
    }

    private Vector3 GetCarDirection()
    {
        Vector3 direction = Vector3.ProjectOnPlane(car.forward, Vector3.up);
        if (direction.sqrMagnitude > Mathf.Epsilon)
        {
            lastCarDirection = direction.normalized;
        }

        return lastCarDirection;
    }

    private void ConfigureCinemachineFollow()
    {
        if (cinemachineFollow == null)
        {
            return;
        }

        cinemachineFollow.TrackerSettings.BindingMode = BindingMode.WorldSpace;
        cinemachineFollow.TrackerSettings.PositionDamping = Vector3.zero;
        cinemachineFollow.TrackerSettings.RotationDamping = Vector3.zero;
        cinemachineFollow.TrackerSettings.QuaternionDamping = 0f;
        cinemachineFollow.FollowOffset = Vector3.zero;
    }
}
