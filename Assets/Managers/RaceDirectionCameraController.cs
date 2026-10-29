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
    [SerializeField, Min(0.01f)] private float cameraDistance = 5.8f;
    [SerializeField, Min(0f)] private float cameraHeight = 2f;
    [SerializeField, Min(0f)] private float rotationSmoothTime = 0.15f;

    [Header("Slope Following")]
    [SerializeField, Min(0f)] private float pitchSmoothTime = .3f;
    [SerializeField, Range(0f, 60f)] private float maximumSlopePitch = 30f;
    [SerializeField, Min(0f)] private float lookAheadDistance = 3f;

    [Header("High Speed Framing")]
    [SerializeField, Min(1f)] private float framingSpeedMps = 50f;
    [SerializeField, Min(0f)] private float highSpeedHeight = .8f;
    [SerializeField, Min(0f)] private float highSpeedLookAhead = 4f;
    [SerializeField, Min(0f)] private float framingSmoothTime = .4f;

    [Header("Curve Look Ahead")]
    [SerializeField, Min(0f)] private float turnLookAheadSeconds = .35f;
    [SerializeField, Min(0f)] private float turnRateDeadZone = 4f;
    [SerializeField, Min(0f)] private float maximumLookYaw = 8f;
    [SerializeField, Min(0f)] private float maximumLateralLook = 1.5f;
    [SerializeField, Min(0f)] private float turnSmoothTime = .4f;

    [Header("Occlusion")]
    [SerializeField] private bool avoidOcclusion = true;
    [SerializeField] private LayerMask occlusionLayers = Physics.DefaultRaycastLayers;
    [SerializeField, Min(.01f)] private float occlusionRadius = .35f;
    [SerializeField, Min(0f)] private float occlusionPadding = .12f;
    [SerializeField, Min(0f)] private float occlusionOriginHeight = .8f;
    [SerializeField, Min(0f)] private float occlusionReturnTime = .25f;

    [SerializeField, Min(0f)] private float heightSmoothTime = .25f;
    [SerializeField, Min(1f)] private float teleportDistance = 12f;

    [Header("Post-goal framing")]
    [SerializeField, Min(0f)] private float finishPullback = 2.2f;
    [SerializeField, Range(0f, 35f)] private float finishOrbitDegrees = 12f;
    [SerializeField, Min(0.1f)] private float finishCameraSmoothTime = .8f;

    private float smoothedHeight;
    private float heightVelocity;
    private Vector3 lastCarPosition;
    private float lookHeight;
    private Transform car;
    private Transform cameraTarget;
    private Transform cameraLookTarget;
    private CinemachineFollow cinemachineFollow;
    private Vector3 lastCarDirection = Vector3.forward;
    private float yawVelocity;
    private float smoothedPitch;
    private float pitchVelocity;
    private Rigidbody carBody;
    private float framing01, framingVelocity;
    private float previousYaw, filteredTurnRate, turnVelocity;
    private float occlusionDistance, occlusionVelocity;
    private readonly RaycastHit[] occlusionHits = new RaycastHit[32];
    [SerializeField, HideInInspector] private int framingDefaultsVersion;
    private GroundCheck[] wheelGroundChecks = System.Array.Empty<GroundCheck>();

    public Transform CameraTarget => cameraTarget;
    public Transform LookTarget => cameraLookTarget;

    private void OnEnable()
    {
        // Upgrade the shipped serialized default without editing scenes or replacing custom tuning.
        if (framingDefaultsVersion != 0) return;
        if (cameraDistance == 5f) cameraDistance = 5.8f;
        framingDefaultsVersion = 1;
    }

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
        carBody = car != null ? car.GetComponent<Rigidbody>() : null;
        TireForce[] tires = car != null ? car.GetComponentsInChildren<TireForce>() : System.Array.Empty<TireForce>();
        wheelGroundChecks = new GroundCheck[tires.Length];
        for (int i = 0; i < tires.Length; i++) wheelGroundChecks[i] = tires[i].GetComponent<GroundCheck>();
        if (car != null && car.TryGetComponent<Rigidbody>(out var body))
            body.interpolation = RigidbodyInterpolation.Interpolate;
        EnsureCameraTarget();
        UpdateCameraTarget(immediately: true, Time.deltaTime);
    }

    /// <summary>Raise the practice view's aim to leave the car between the instrument panels and lesson strip.</summary>
    public void SetLookHeight(float height)
    {
        lookHeight = Mathf.Max(0f, height);
        UpdateCameraTarget(immediately: true, Time.deltaTime);
    }

    public void ClearCar()
    {
        car = null;
        carBody = null;
        framing01 = framingVelocity = filteredTurnRate = turnVelocity = 0f;
        occlusionDistance = occlusionVelocity = 0f;
        yawVelocity = 0f;
        pitchVelocity = smoothedPitch = 0f;
        wheelGroundChecks = System.Array.Empty<GroundCheck>();

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
        UpdateCameraTarget(immediately: false, Time.deltaTime);
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

    private void UpdateCameraTarget(bool immediately, float deltaTime)
    {
        if (car == null || cameraTarget == null || cameraLookTarget == null)
        {
            return;
        }

        immediately |= (car.position - lastCarPosition).sqrMagnitude > teleportDistance * teleportDistance;
        Vector3 movement = car.position - lastCarPosition;
        lastCarPosition = car.position;
        Vector3 carDirection = GetCarDirection();
        bool supported = TryGetRoadNormal(out Vector3 roadNormal);
        float targetPitch = immediately ? 0f : smoothedPitch;
        if (supported)
        {
            Vector3 roadForward = Vector3.ProjectOnPlane(car.forward, roadNormal).normalized;
            targetPitch = Mathf.Clamp(-Mathf.Atan2(roadForward.y,
                new Vector2(roadForward.x, roadForward.z).magnitude) * Mathf.Rad2Deg,
                -maximumSlopePitch, maximumSlopePitch);
            // Follow sustained road elevation without the lag of the vibration filter.
            // Normal-direction wheel/body bumps remain filtered; near-vertical banks are excluded.
            if (!immediately && roadNormal.y > .2f)
                smoothedHeight -= Vector3.Dot(new Vector3(movement.x, 0f, movement.z), roadNormal) / roadNormal.y;
        }
        if (immediately)
        {
            smoothedPitch = targetPitch;
            pitchVelocity = 0f;
        }
        else
            smoothedPitch = Mathf.SmoothDampAngle(smoothedPitch, targetPitch, ref pitchVelocity,
                pitchSmoothTime, Mathf.Infinity, deltaTime);
        if (immediately)
        {
            smoothedHeight = car.position.y;
            heightVelocity = 0f;
        }
        else
            smoothedHeight = Mathf.SmoothDamp(smoothedHeight, car.position.y, ref heightVelocity, heightSmoothTime, Mathf.Infinity, deltaTime);
        Vector3 anchorPosition = new Vector3(car.position.x, smoothedHeight, car.position.z);
        float targetYaw = Mathf.Atan2(carDirection.x, carDirection.z) * Mathf.Rad2Deg;
        float speed = carBody != null ? carBody.linearVelocity.magnitude : 0f;
        FinishRunController finishRun = car.GetComponent<FinishRunController>();
        float finish01 = finishRun != null
            ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(finishRun.ElapsedSeconds / FinishRunController.Duration))
            : 0f;
        targetYaw += finishOrbitDegrees * finish01;
        float targetFraming = Mathf.Clamp01(speed / Mathf.Max(1f, framingSpeedMps));
        if (immediately)
        {
            framing01 = targetFraming;
            framingVelocity = filteredTurnRate = turnVelocity = 0f;
        }
        else
        {
            framing01 = Mathf.SmoothDamp(framing01, targetFraming, ref framingVelocity,
                framingSmoothTime, Mathf.Infinity, deltaTime);
            float turnRate = supported && speed > 5f && deltaTime > 0f
                ? Mathf.Clamp(Mathf.DeltaAngle(previousYaw, targetYaw) / deltaTime, -45f, 45f) : 0f;
            filteredTurnRate = Mathf.SmoothDamp(filteredTurnRate, turnRate, ref turnVelocity,
                turnSmoothTime, Mathf.Infinity, deltaTime);
        }
        previousYaw = targetYaw;
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
                finish01 > 0f ? finishCameraSmoothTime : rotationSmoothTime,
                Mathf.Infinity,
                deltaTime);
        }

        // Road pitch only: banking and airborne body rotation must not roll or whip the view.
        Quaternion cameraRotation = Quaternion.Euler(smoothedPitch, yaw, 0f);
        Vector3 cameraPosition = anchorPosition
            - cameraRotation * Vector3.forward * (Mathf.Max(0.01f, cameraDistance) + finishPullback * finish01)
            + Vector3.up * (Mathf.Max(0f, cameraHeight) + highSpeedHeight * framing01);

        // Move inward immediately; only the return to the normal distance is damped.
        Vector3 origin = GetOcclusionOrigin();
        Vector3 desiredOffset = cameraPosition - origin;
        float desiredDistance = desiredOffset.magnitude;
        float safeDistance = Vector3.Distance(origin, ConstrainCameraPosition(cameraPosition));
        if (immediately || safeDistance < occlusionDistance)
        {
            occlusionDistance = safeDistance;
            occlusionVelocity = 0f;
        }
        else
            occlusionDistance = Mathf.SmoothDamp(occlusionDistance, safeDistance, ref occlusionVelocity,
                occlusionReturnTime, Mathf.Infinity, deltaTime);
        cameraPosition = origin + desiredOffset.normalized * Mathf.Min(desiredDistance, occlusionDistance);

        cameraTarget.SetPositionAndRotation(cameraPosition, cameraRotation);

        float lookDistance = lookAheadDistance + highSpeedLookAhead * framing01;
        float turn = Mathf.Sign(filteredTurnRate) * Mathf.Max(0f, Mathf.Abs(filteredTurnRate) - turnRateDeadZone);
        float lookYaw = Mathf.Clamp(turn * turnLookAheadSeconds, -maximumLookYaw, maximumLookYaw);
        float lateral = Mathf.Clamp(Mathf.Tan(lookYaw * Mathf.Deg2Rad) * lookDistance,
            -maximumLateralLook, maximumLateralLook);
        cameraLookTarget.SetPositionAndRotation(
            anchorPosition + cameraRotation * (Vector3.forward * lookDistance + Vector3.right * lateral)
                + Vector3.up * lookHeight,
            cameraRotation);
    }

    private Vector3 GetOcclusionOrigin() => car.position + car.up * occlusionOriginHeight;

    /// <summary>Also used after speed pullback/shake, so those offsets cannot push the lens through a wall.</summary>
    public Vector3 ConstrainCameraPosition(Vector3 desiredPosition)
    {
        if (!avoidOcclusion || car == null) return desiredPosition;
        Vector3 origin = GetOcclusionOrigin();
        Vector3 offset = desiredPosition - origin;
        float distance = offset.magnitude;
        if (distance < .001f) return desiredPosition;
        int count = Physics.SphereCastNonAlloc(origin, Mathf.Max(.01f, occlusionRadius), offset / distance,
            occlusionHits, distance, occlusionLayers, QueryTriggerInteraction.Ignore);
        // NonAlloc results are unordered; a full buffer may omit the nearest wall.
        RaycastHit[] hits = count == occlusionHits.Length
            ? Physics.SphereCastAll(origin, Mathf.Max(.01f, occlusionRadius), offset / distance,
                distance, occlusionLayers, QueryTriggerInteraction.Ignore) : occlusionHits;
        if (hits != occlusionHits) count = hits.Length;
        float safeDistance = distance;
        for (int i = 0; i < count; i++)
        {
            Collider obstacle = hits[i].collider;
            if (obstacle == null || obstacle.transform.IsChildOf(car) ||
                obstacle.GetComponentInParent<DebugMover>() != null) continue;
            // TubeDriveSurface is deliberately included, even though it is a driveable surface.
            safeDistance = Mathf.Min(safeDistance, Mathf.Max(0f, hits[i].distance - occlusionPadding));
        }
        return origin + offset / distance * safeDistance;
    }

    private bool TryGetRoadNormal(out Vector3 normal)
    {
        normal = Vector3.zero;
        int count = 0;
        foreach (GroundCheck wheel in wheelGroundChecks)
        {
            if (wheel == null || !wheel.isGround) continue;
            normal += wheel.GroundHit.normal;
            count++;
        }
        if (count < 2 || normal.sqrMagnitude < .0001f) return false;
        normal.Normalize();
        return true;
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
