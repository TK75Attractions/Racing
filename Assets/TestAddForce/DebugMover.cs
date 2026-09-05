using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

[RequireComponent(typeof(Rigidbody))]
public class DebugMover : MonoBehaviour
{
    [Header("Front-Wheel Drive")]
    [Tooltip("接地している前輪1本あたりの最大駆動力。")]
    [SerializeField, Min(0f)] private float driveForcePerFrontWheel = 15f;

    [Header("Velocity Resistance")]
    [Tooltip("水平速度に比例し、速度と反対方向に加える抵抗力の係数。")]
    [SerializeField, Min(0f)] private float velocityResistance = 0.6f;

    [Header("Steering")]
    [Tooltip("入力ハンドル値を前輪角度に変換する倍率。")]
    [SerializeField, Min(0f)] private float steeringInputMultiplier = 1f;

    [Tooltip("前輪の最大操舵角（度）。")]
    [SerializeField, Range(0f, 60f)] private float maxSteeringAngle = 30f;

    [Tooltip("この速度（m/s）から操舵角を小さくし始めます。")]
    [SerializeField, Min(0f)] private float steeringFadeStartSpeed = 10f;

    [Tooltip("この速度（m/s）で高速時操舵倍率に達します。")]
    [SerializeField, Min(0f)] private float steeringFadeFullSpeed = 30f;

    [Tooltip("高速時に残す操舵角の割合。")]
    [SerializeField, Range(0f, 1f)] private float highSpeedSteeringMultiplier = 0.35f;

    [Header("Drift")]
    [Tooltip("ドリフトを開始する生のハンドル入力の絶対値（高速時の操舵補正前）。")]
    [SerializeField, Min(0.01f)] private float driftStartHandle = 8f;

    [Tooltip("チャージが最大速度でたまるハンドル入力の絶対値。")]
    [SerializeField, Min(0.01f)] private float driftFullChargeHandle = 30f;

    [SerializeField, Min(0f)] private float maxDriftCharge = 3f;
    [Tooltip("最大ハンドル入力時の1秒あたりのチャージ量。")]
    [SerializeField, Min(0f)] private float driftChargePerSecond = 1f;

    [Tooltip("ドリフト中の速度抵抗倍率。1より大きくすると減速します。")]
    [SerializeField, Min(1f)] private float driftResistanceMultiplier = 1.25f;
    [Tooltip("ドリフト中に残す後輪の横グリップの割合。")]
    [SerializeField, Range(0f, 1f)] private float driftRearGripMultiplier = 0.65f;
    [Header("Drift Boost")]
    [Tooltip("ドリフト解放後に加速を続ける時間（秒）。0で加速を無効化します。")]
    [SerializeField, Min(0f)] private float driftBoostDuration = 1f;
    [Tooltip("解放したチャージ1あたりの加速度（m/s²）。実際の加速度はチャージ量に比例します。")]
    [FormerlySerializedAs("driftBoostSpeedPerCharge")]
    [SerializeField, Min(0f)] private float driftBoostAccelerationPerCharge = 3f;

    [Header("Respawn")]
    [Tooltip("リスポーン直後にアクセル、ハンドルなどの運転入力を無効化する時間（秒）。")]
    [FormerlySerializedAs("respawnSteeringSuppressionSeconds")]
    [SerializeField, Min(0f)] private float respawnInputSuppressionSeconds = 0.3f;

    [Header("Tire Lateral Force")]
    [Tooltip("前輪の横滑り速度を横力に変換する係数。")]
    [SerializeField, Min(0f)] private float frontCorneringStiffness = 10f;

    [Tooltip("後輪の横滑り速度を横力に変換する係数。")]
    [SerializeField, Min(0f)] private float rearCorneringStiffness = 12f;

    [Tooltip("タイヤ1本あたりの横力上限。")]
    [SerializeField, Min(0f)] private float maxLateralForcePerTire = 15f;

    [Header("Runtime References")]
    [SerializeField] private List<TireForce> tires = new List<TireForce>();

    [Header("Runtime Monitor")]
    [SerializeField] private float speedMetersPerSecond;
    [SerializeField] private float rawPedalInput;
    [SerializeField] private float appliedPedalInput;
    [SerializeField] private float rawSteeringInput;
    [SerializeField] private float appliedSteeringAngle;
    [SerializeField] private float resistanceForce;
    [SerializeField] private bool isDrifting;
    [SerializeField] private float driftCharge;
    [SerializeField] private float driftBoostTimeRemaining;
    [SerializeField] private float activeDriftBoostAcceleration;

    private Rigidbody rb;
    private IDriveInputSource inputSource;
    private float inputSuppressedUntil;
    private float driftDirection;

    public IDriveInputSource InputSource => inputSource;
    public bool IsInputSuppressed => Time.time < inputSuppressedUntil;
    public bool IsDrifting => isDrifting;
    public float DriftCharge => driftCharge;
    public float NormalizedDriftCharge => maxDriftCharge > 0f ? driftCharge / maxDriftCharge : 0f;
    public bool IsDriftBoosting => isActiveAndEnabled && !IsInputSuppressed &&
        driftBoostTimeRemaining > 0f && activeDriftBoostAcceleration > 0f;
    public float DriftBoostVisualIntensity => IsDriftBoosting
        ? Mathf.Clamp01(activeDriftBoostAcceleration /
            Mathf.Max(0.0001f, maxDriftCharge * driftBoostAccelerationPerCharge))
        : 0f;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        RefreshTires();
    }

    private void FixedUpdate()
    {
        if (inputSource == null ||
            Gmanager.Control == null ||
            !Gmanager.Control.IsDrivingEnabled)
        {
            ResetDrift();
            return;
        }

        if (IsInputSuppressed)
        {
            ClearUserInput();
            ResetDrift();
        }
        else
        {
            ReadInput();
            float boostAcceleration = UpdateDrift(Time.fixedDeltaTime);
            if (boostAcceleration > 0f)
            {
                StartDriftBoost(boostAcceleration);
            }

            float boostSpeedDelta = ConsumeDriftBoost(Time.fixedDeltaTime);
            if (boostSpeedDelta > 0f)
            {
                Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
                // 加速度×経過時間を各物理フレームに加算し、質量に依存しない持続加速にする。
                rb.AddForce(forward * boostSpeedDelta, ForceMode.VelocityChange);
            }
        }

        ApplyTireForces();
        ApplyVelocityResistance();
    }

    public void SetInputSource(IDriveInputSource source)
    {
        ResetDrift();
        inputSource = source;
    }

    public void SuppressInputAfterRespawn()
    {
        float duration = Mathf.Max(0f, respawnInputSuppressionSeconds);
        inputSuppressedUntil = Mathf.Max(inputSuppressedUntil, Time.time + duration);
        ClearUserInput();
        ResetDrift();

        foreach (TireForce tire in tires)
        {
            tire.CenterVisualSteering();
        }
    }

    private void OnDisable()
    {
        ResetDrift();
    }

    private void ResetDrift()
    {
        isDrifting = false;
        driftCharge = 0f;
        driftDirection = 0f;
        driftBoostTimeRemaining = 0f;
        activeDriftBoostAcceleration = 0f;
    }

    private void StartDriftBoost(float acceleration)
    {
        // 再解放時は、新しいチャージ量と設定時間で置き換える。
        driftBoostTimeRemaining = Mathf.Max(0f, driftBoostDuration);
        activeDriftBoostAcceleration = driftBoostTimeRemaining > 0f ? Mathf.Max(0f, acceleration) : 0f;
    }

    private float ConsumeDriftBoost(float deltaTime)
    {
        float elapsed = Mathf.Min(Mathf.Max(0f, deltaTime), driftBoostTimeRemaining);
        float speedDelta = activeDriftBoostAcceleration * elapsed;
        driftBoostTimeRemaining = Mathf.Max(0f, driftBoostTimeRemaining - elapsed);
        if (driftBoostTimeRemaining <= 0f)
        {
            activeDriftBoostAcceleration = 0f;
        }

        return speedDelta;
    }

    // 中立入力を挟んでも開始時の方向を保持し、逆符号になったときだけ解放する。
    private float UpdateDrift(float deltaTime)
    {
        if (isDrifting && rawSteeringInput * driftDirection < 0f)
        {
            float boostAcceleration = driftCharge * Mathf.Max(0f, driftBoostAccelerationPerCharge);
            ResetDrift();
            // この物理フレームでは反対方向のドリフトを開始しない。
            return boostAcceleration;
        }

        float handleMagnitude = Mathf.Abs(rawSteeringInput);
        float startHandle = Mathf.Max(0.01f, driftStartHandle);
        if (!isDrifting && handleMagnitude >= startHandle)
        {
            isDrifting = true;
            driftDirection = Mathf.Sign(rawSteeringInput);
        }

        if (isDrifting)
        {
            float chargeRatio = Mathf.Clamp01(
                handleMagnitude / Mathf.Max(startHandle, driftFullChargeHandle));
            driftCharge = Mathf.Clamp(
                driftCharge + chargeRatio * Mathf.Max(0f, driftChargePerSecond) * Mathf.Max(0f, deltaTime),
                0f,
                Mathf.Max(0f, maxDriftCharge));
        }

        return 0f;
    }

    private void RefreshTires()
    {
        tires.Clear();
        tires.AddRange(GetComponentsInChildren<TireForce>());

        foreach (TireForce tire in tires)
        {
            tire.Init(rb);
        }
    }

    private void ReadInput()
    {
        DriveInputState input = inputSource.CurrentState;
        rawPedalInput = input.pedal;
        appliedPedalInput = Mathf.Clamp(rawPedalInput, -1f, 1f);
        rawSteeringInput = input.steering;

        Vector3 planarVelocity = Vector3.ProjectOnPlane(rb.linearVelocity, Vector3.up);
        speedMetersPerSecond = planarVelocity.magnitude;

        float fullSpeed = Mathf.Max(steeringFadeStartSpeed + 0.01f, steeringFadeFullSpeed);
        float speedRatio = Mathf.InverseLerp(steeringFadeStartSpeed, fullSpeed, speedMetersPerSecond);
        float speedSteeringMultiplier = Mathf.Lerp(1f, highSpeedSteeringMultiplier, speedRatio);

        appliedSteeringAngle = Mathf.Clamp(
            rawSteeringInput * steeringInputMultiplier * speedSteeringMultiplier,
            -maxSteeringAngle,
            maxSteeringAngle);
    }

    private void ClearUserInput()
    {
        rawPedalInput = 0f;
        appliedPedalInput = 0f;
        rawSteeringInput = 0f;
        appliedSteeringAngle = 0f;
    }

    private void ApplyTireForces()
    {
        Vector3 vehicleForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;

        foreach (TireForce tire in tires)
        {
            float corneringStiffness = tire.IsFrontWheel
                ? frontCorneringStiffness
                : rearCorneringStiffness;
            float gripMultiplier = isDrifting && !tire.IsFrontWheel
                ? Mathf.Clamp01(driftRearGripMultiplier)
                : 1f;

            tire.ApplyForces(
                vehicleForward,
                Vector3.up,
                appliedSteeringAngle,
                appliedPedalInput,
                driveForcePerFrontWheel,
                corneringStiffness * gripMultiplier,
                maxLateralForcePerTire * gripMultiplier);
        }
    }

    private void ApplyVelocityResistance()
    {
        Vector3 planarVelocity = Vector3.ProjectOnPlane(rb.linearVelocity, Vector3.up);
        Vector3 resistance = -planarVelocity * velocityResistance;
        if (isDrifting)
        {
            resistance *= Mathf.Max(1f, driftResistanceMultiplier);
        }
        resistanceForce = resistance.magnitude;
        rb.AddForce(resistance, ForceMode.Force);
    }
}
