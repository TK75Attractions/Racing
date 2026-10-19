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
    [Tooltip("全タイヤが路面から離れている間の水平抵抗倍率。0で減速なし、1で地上と同じ抵抗。")]
    [SerializeField, Range(0f, 1f)] private float airborneResistanceMultiplier = .05f;

    [Header("Steering")]
    [Tooltip("入力ハンドル値を前輪角度に変換する倍率。")]
    [SerializeField, Min(0f)] private float steeringInputMultiplier = 1f;

    [Tooltip("前輪の最大操舵角（度）。")]
    [SerializeField, Range(0f, 60f)] private float maxSteeringAngle = 30f;

    [Tooltip("この速度（m/s）から操舵角を小さくし始めます。")]
    [SerializeField, Min(0f)] private float steeringFadeStartSpeed = 10f;

    [Tooltip("この速度（m/s）で高速時操舵倍率に達し、以降は速度の二乗に反比例してさらに下がります。")]
    [SerializeField, Min(0f)] private float steeringFadeFullSpeed = 30f;

    [Tooltip("高速時に残す操舵角の割合。")]
    [SerializeField, Range(0f, 1f)] private float highSpeedSteeringMultiplier = 0.35f;

    [Tooltip("操舵角を小さくしていく曲線の形。1で直線的、1より大きいと高速側で一気に曲がりにくくなり、1より小さいと低速側から曲がりにくくなります。")]
    [SerializeField, Range(0.1f, 5f)] private float steeringFadeSharpness = 1f;

    [Tooltip("速度に応じて操舵角を変えるか。無効にすると全速度で同じ曲がりやすさになります。")]
    [SerializeField] private bool enableSpeedSensitiveSteering = true;

    [Tooltip("旋回要求の横加速度（m/s²）から速度別の切れ角上限を求めます。実際の横加速度を保証する値ではありません。")]
    [SerializeField, Min(1f)] private float steeringLateralAcceleration = 22f;
    [Tooltip("ゲーム入力単位の中央不感帯。シーンの除算値6.5では0.08が実ハンドル約0.52度です。")]
    [SerializeField, Min(0f)] private float steeringDeadzone = .08f;
    [Tooltip("入力平滑化の時定数（秒）。高速時も切り返し・中央復帰は速く応答します。0で無効。")]
    [SerializeField, Min(0f)] private float steeringResponseSeconds = .045f;

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
    [Tooltip("ドリフト中に残す後輪の横グリップ倍率。1が通常、1より大きい値で通常以上のグリップ。")]
    [SerializeField, Min(0f)] private float driftRearGripMultiplier = 0.9f;

    [Header("Runtime Force Toggles")]
    [Tooltip("タイヤの横滑りを抑える横力を適用するか。プレイ中の原因切り分け用。")]
    [SerializeField] private bool enableLateralTireForces = true;

    [Tooltip("前輪の駆動力を適用するか。プレイ中の原因切り分け用。")]
    [SerializeField] private bool enableFrontDriveForce = true;

    [Tooltip("ドリフト状態、後輪グリップ低下、ドリフト抵抗、ドリフトブーストを有効にするか。")]
    [SerializeField] private bool enableDriftDynamics = true;

    [Tooltip("ドリフト中に後輪の横グリップを低下させるか。")]
    [SerializeField] private bool enableDriftRearGripReduction = true;

    [Tooltip("前進速度がこの値以上で、前後輪が接地しているときだけドリフトを開始します。")]
    [SerializeField, Min(0f)] private float driftMinimumSpeed = 6f;
    [SerializeField, Min(0f)] private float driftStartHoldSeconds = .18f;
    [SerializeField, Min(0f)] private float driftCountersteerHandle = 2f;
    [SerializeField, Min(0f)] private float driftReleaseHoldSeconds = .1f;
    [SerializeField, Min(.01f)] private float driftGripTransitionSeconds = .3f;

    [Header("Drift Spin Protection")]
    [SerializeField] private bool enableDriftSpinProtection = true;
    [Tooltip("自然な横滑りとして残す、車の向きと水平移動方向の角度（度）。")]
    [SerializeField, Range(0f, 30f)] private float driftMaxSlipAngle = 12f;
    [Tooltip("ドリフト中の旋回速度の目標上限（rad/s）。")]
    [SerializeField, Min(.1f)] private float driftMaxYawRate = 1.6f;
    [Tooltip("高速ドリフトの旋回を抑える横加速度の目安（m/s²）。")]
    [SerializeField, Min(1f)] private float driftMaxLateralAcceleration = 22f;
    [SerializeField, Min(0f)] private float driftYawResponse = 10f;
    [SerializeField, Min(0f)] private float driftMaxYawAcceleration = 15f;
    [SerializeField, Min(0f)] private float driftSlipRecoveryDegreesPerSecond = 60f;
    [SerializeField, Min(0f)] private float driftRecoverySeconds = .75f;

    [Header("Drift Boost")]
    [Tooltip("ドリフト解放後に加速を続ける時間（秒）。0で加速を無効化します。")]
    [SerializeField, Min(0f)] private float driftBoostDuration = 1f;
    [Tooltip("解放したチャージ1あたりの加速度（m/s²）。実際の加速度はチャージ量に比例します。")]
    [FormerlySerializedAs("driftBoostSpeedPerCharge")]
    [SerializeField, Min(0f)] private float driftBoostAccelerationPerCharge = 3f;

    [Header("Drift Charge Display")]
    [Tooltip("チャージ表示を段階に分けるしきい値（正規化チャージ 0〜1）。小さい順に並べます。挙動は変わらず、見た目の段階だけが変わります。")]
    [SerializeField] private float[] driftChargeTierThresholds = { 0.34f, 0.67f, 1f };

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
    [Tooltip("速度によって操舵角にかけている倍率（実行時モニター）。1で通常、小さいほど曲がりにくい状態です。")]
    [SerializeField] private float appliedSteeringMultiplier = 1f;
    [SerializeField] private float resistanceForce;
    [SerializeField] private bool isDrifting;
    [SerializeField] private float driftCharge;
    [SerializeField] private float driftBoostTimeRemaining;
    [SerializeField] private float activeDriftBoostAcceleration;

    [Header("Acceleration Pad Boost")]
    [Tooltip("加速度盤から受けた加速の残り時間（実行時モニター）。")]
    [SerializeField] private float accelerationPadBoostTimeRemaining;
    [Tooltip("加速度盤から受けた加速度（実行時モニター）。")]
    [SerializeField] private float activeAccelerationPadBoostAcceleration;

    private float slipstreamBoostRemaining;
    public bool IsSlipstreamBoosting => slipstreamBoostRemaining > 0f;
    public void CancelSlipstreamBoost() { slipstreamBoostRemaining = 0f; }
    public bool StartSlipstreamBoost()
    {
        if (!CanApplySlipstreamBoost() || activeDriftBoostAcceleration + activeAccelerationPadBoostAcceleration >= 18f ||
            Vector3.ProjectOnPlane(rb.linearVelocity, GetDrivingUp()).magnitude >= 32f) return false;
        slipstreamBoostRemaining = .7f; return true;
    }
    private float landingBoostRemaining;
    public bool IsLandingBoosting => landingBoostRemaining > 0f;
    public void CancelLandingBoost() { landingBoostRemaining = 0f; }
    public bool StartLandingBoost()
    {
        if (!CanApplyLandingBoost() || activeDriftBoostAcceleration + activeAccelerationPadBoostAcceleration >= 18f ||
            Vector3.ProjectOnPlane(rb.linearVelocity, GetDrivingUp()).magnitude >= 32f) return false;
        landingBoostRemaining = .5f; return true;
    }
    private bool CanApplyLandingBoost()
    {
        if (!RaceFeatureController.Enabled(RaceFeature.LandingBoost) || !CanApplyRoadReward(15f)) return false;
        RaceCourse course = Gmanager.Control.course;
        float progress = course.GetProgressDistance(rb.position);
        return course.TryGetSampleAtProgress(progress, out RaceCourse.CourseSample here) &&
            course.TryGetSampleAtProgress(progress + Gmanager.Control.CourseProgressSign * 12f, out RaceCourse.CourseSample ahead) &&
            Vector3.Angle(here.forward, ahead.forward) < 40f;
    }
    private bool CanApplySlipstreamBoost() => RaceFeatureController.Enabled(RaceFeature.Slipstream) && CanApplyRoadReward();
    private bool CanApplyRoadReward(float gapLookAhead = 45f)
    {
        Gmanager race = Gmanager.Control;
        return race != null && race.state == Gmanager.State.Game &&
            rb != null && !rb.isKinematic && !IsInputSuppressed && inputSource != null && inputSource.IsConnected &&
            !(inputSource is PlayerDriveInputGate gate && (gate.NeedsPedalRelease || gate.IsBlocked)) &&
            HasDriftGroundContact() && Vector3.Dot(transform.up, Vector3.up) > .85f &&
            (itemEffects == null || !(itemEffects.IsRocketActive || itemEffects.BlocksDriverInput || itemEffects.IsOiled || itemEffects.IsConfused || itemEffects.IsRecoveryProtected)) &&
            race.course != null && !race.course.IsJumpAhead(transform.position, race.CourseProgressSign, gapLookAhead);
    }
    private void ApplyRoadRewardBoost(float dt, float legacySpeedDelta)
    {
        if (slipstreamBoostRemaining > 0f && !CanApplySlipstreamBoost()) CancelSlipstreamBoost();
        if (landingBoostRemaining > 0f && !CanApplyLandingBoost()) CancelLandingBoost();
        // Added rewards use the strongest acceleration, never their sum. Legacy boosts retain their rules.
        float speed = Vector3.ProjectOnPlane(rb.linearVelocity, GetDrivingUp()).magnitude;
        float delta = RoadRewardBudget.SpeedDelta(dt, slipstreamBoostRemaining, landingBoostRemaining, legacySpeedDelta, speed);
        slipstreamBoostRemaining = Mathf.Max(0f, slipstreamBoostRemaining - dt);
        landingBoostRemaining = Mathf.Max(0f, landingBoostRemaining - dt);
        if (delta > 0f) rb.AddForce(Vector3.ProjectOnPlane(transform.forward, GetDrivingUp()).normalized * delta, ForceMode.VelocityChange);
    }

    private Rigidbody rb;
    private IDriveInputSource inputSource;
    private float inputSuppressedUntil;
    private float driftDirection;
    private float pendingDriftDirection;
    private float driftStartTime;
    private float driftReleaseTime;
    private float driftGripBlend;
    private float driftRecoveryTimeRemaining;
    private float wheelbase = 2.6f;
    private CarItemEffects itemEffects;
    private Vector3 drivingUp;
    private bool useCachedDrivingUp;
    private float filteredSteeringInput;
    private float steeringAngleLimit = 30f;

    public IDriveInputSource InputSource => inputSource;
    /// <summary>実際に走行へ反映しているペダル入力（-1:ブレーキ 〜 1:アクセル）。</summary>
    public float PedalInput => appliedPedalInput;
    public float SpeedMetersPerSecond => speedMetersPerSecond;
    /// <summary>速度によって操舵角にかけている倍率（1で通常、小さいほど曲がりにくい）。</summary>
    public float SteeringSpeedMultiplier => appliedSteeringMultiplier;
    public float AppliedSteeringAngle => appliedSteeringAngle;
    public float SteeringAngleLimit => steeringAngleLimit;
    public float SteeringMaximumAngle => maxSteeringAngle;
    // HUD uses input units so faded ends show exactly where further handle travel stops helping.
    public float SteeringInputRange => maxSteeringAngle / Mathf.Max(.0001f, steeringInputMultiplier);
    public float SteeringInputLimit => steeringAngleLimit /
        Mathf.Max(.0001f, steeringInputMultiplier * appliedSteeringMultiplier);
    public float RawSteeringInput => rawSteeringInput;
    public bool IsInputSuppressed => Time.time < inputSuppressedUntil;
    public bool HasGroundedWheels
    {
        get { foreach (TireForce tire in tires) if (tire.IsGrounded) return true; return false; }
    }
    public bool IsDrifting => isDrifting;
    public event System.Action<float> DriftBoostReleased;
    public float DriftCharge => driftCharge;
    public float NormalizedDriftCharge => maxDriftCharge > 0f ? driftCharge / maxDriftCharge : 0f;
    /// <summary>チャージ表示の段階数です。0 は段階分けなしを表します。</summary>
    public int MaxDriftChargeTier => driftChargeTierThresholds != null ? driftChargeTierThresholds.Length : 0;
    /// <summary>現在のチャージ段階です。0 はドリフト開始直後のチャージが乏しい状態を表します。</summary>
    public int DriftChargeTier => GetDriftChargeTier(NormalizedDriftCharge);
    /// <summary>解放時に最大の加速を得られる段階へ達しているかどうかです。</summary>
    public bool IsDriftChargeFull => MaxDriftChargeTier > 0 && DriftChargeTier >= MaxDriftChargeTier;
    public bool IsDriftBoosting => isActiveAndEnabled && !IsInputSuppressed &&
        driftBoostTimeRemaining > 0f && activeDriftBoostAcceleration > 0f;
    public float DriftBoostVisualIntensity => IsDriftBoosting
        ? Mathf.Clamp01(activeDriftBoostAcceleration /
            Mathf.Max(0.0001f, maxDriftCharge * driftBoostAccelerationPerCharge))
        : 0f;
    public bool IsAccelerationPadBoosting => isActiveAndEnabled && !IsInputSuppressed &&
        accelerationPadBoostTimeRemaining > 0f && activeAccelerationPadBoostAcceleration > 0f;
    public float AccelerationPadBoostTimeRemaining => IsAccelerationPadBoosting
        ? accelerationPadBoostTimeRemaining : 0f;
    public float AccelerationPadBoostDuration => IsAccelerationPadBoosting
        ? accelerationPadBoostDuration : 0f;
    public bool IsRocketBoosting => isActiveAndEnabled && itemEffects != null && itemEffects.IsRocketActive;
    /// <summary>ドリフト、加速度盤、ロケットを合わせた加速画面演出用の強度です。</summary>
    public float BoostVisualIntensity => Mathf.Max(DriftBoostVisualIntensity,
        IsAccelerationPadBoosting || IsRocketBoosting ? 1f : (IsSlipstreamBoosting || IsLandingBoosting) ? .35f : 0f);

    private float[] vehicleDefaults;
    public Vector4 VehicleMultipliers { get; private set; } = Vector4.one;

    /// <summary>Applies a grade relative to the prefab's settings, without accumulating on repeated calls.</summary>
    public void ConfigureVehicle(float acceleration, float topSpeed, float handling, float drift)
    {
        vehicleDefaults ??= new[] { driveForcePerFrontWheel, velocityResistance, frontCorneringStiffness,
            rearCorneringStiffness, maxSteeringAngle, driftChargePerSecond };
        acceleration = Mathf.Clamp(acceleration, .7f, 1.3f);
        topSpeed = Mathf.Clamp(topSpeed, .7f, 1.3f);
        handling = Mathf.Clamp(handling, .7f, 1.3f);
        drift = Mathf.Clamp(drift, .7f, 1.3f);
        VehicleMultipliers = new Vector4(acceleration, topSpeed, handling, drift);
        driveForcePerFrontWheel = vehicleDefaults[0] * acceleration;
        velocityResistance = vehicleDefaults[1] * acceleration / topSpeed;
        frontCorneringStiffness = vehicleDefaults[2] * handling;
        rearCorneringStiffness = vehicleDefaults[3] * handling;
        maxSteeringAngle = Mathf.Min(60f, vehicleDefaults[4] * handling);
        driftChargePerSecond = vehicleDefaults[5] * drift;
    }

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        if (itemEffects == null) itemEffects = GetComponent<CarItemEffects>();
        RefreshTires();
        if (Application.isPlaying && GetComponent<CarBodyMotion>() == null)
            gameObject.AddComponent<CarBodyMotion>();
    }

    private void FixedUpdate()
    {
        if (inputSource == null ||
            Gmanager.Control == null ||
            !Gmanager.Control.IsDrivingEnabled)
        {
            ClearUserInput();
            ResetBoosts();
            return;
        }

        drivingUp = GetDrivingUp();
        useCachedDrivingUp = true;
        try { ApplyDriving(); }
        finally { useCachedDrivingUp = false; }
    }

    private void ApplyDriving()
    {
        // Observe the gate even with stale held input; reconnecting never reuses a held pedal.
        _ = inputSource.CurrentState;
        if (!inputSource.IsConnected || (inputSource is PlayerDriveInputGate gate && gate.NeedsPedalRelease))
        {
            ClearUserInput(); ResetBoosts(); itemEffects?.StopRocketAutopilot();
            ApplyTireForces(); ApplyVelocityResistance();
            Vector3 velocity = Vector3.ProjectOnPlane(rb.linearVelocity, GetDrivingUp());
            speedMetersPerSecond = velocity.magnitude;
            bool grounded = false;
            foreach (TireForce tire in tires) grounded |= tire.GetComponent<GroundCheck>().CheckNow();
            if (grounded && velocity.sqrMagnitude > .0001f)
                rb.AddForce(-velocity.normalized * Mathf.Min(12f, velocity.magnitude / Time.fixedDeltaTime), ForceMode.Acceleration);
            return;
        }
        if (!RaceFeatureController.Enabled(RaceFeature.Slipstream)) CancelSlipstreamBoost();
        itemEffects?.LimitRocketJumpMomentum();

        if (IsInputSuppressed)
        {
            ClearUserInput();
            ResetBoosts();
        }
        else if (itemEffects != null && itemEffects.IsRocketActive)
        {
            // ロケット中は自動操縦が速度と向きを直接決めるため、運転入力・タイヤ力・速度抵抗を使いません。
            // ドリフトチャージは保持し、ロケット後に解放できるようにします。
            ClearUserInput(); CancelSlipstreamBoost(); CancelLandingBoost();
            itemEffects.ApplyRocketAutopilot(Time.fixedDeltaTime);
            speedMetersPerSecond = Vector3.ProjectOnPlane(rb.linearVelocity, Vector3.up).magnitude;
            return;
        }
        else
        {
            ReadInput();
            float boostAcceleration = enableDriftDynamics
                ? UpdateDrift(Time.fixedDeltaTime)
                : 0f;
            if (boostAcceleration > 0f)
            {
                StartDriftBoost(boostAcceleration);
            }

            if (!enableDriftDynamics)
            {
                ResetDrift();
            }

            float boostSpeedDelta = ConsumeDriftBoost(Time.fixedDeltaTime);
            if (boostSpeedDelta > 0f)
            {
                Vector3 forward = Vector3.ProjectOnPlane(transform.forward, GetDrivingUp()).normalized;
                // 加速度×経過時間を各物理フレームに加算し、質量に依存しない持続加速にする。
                rb.AddForce(forward * boostSpeedDelta, ForceMode.VelocityChange);
            }

            float padSpeedDelta = ConsumeAccelerationPadBoost(Time.fixedDeltaTime, out Vector3 padDirection);
            if (padSpeedDelta > 0f)
            {
                rb.AddForce(padDirection * padSpeedDelta, ForceMode.VelocityChange);
            }
            ApplyRoadRewardBoost(Time.fixedDeltaTime, boostSpeedDelta + padSpeedDelta);
        }

        ApplyTireForces();
        ApplyDriftSpinProtection(Time.fixedDeltaTime);
        ApplyVelocityResistance();
    }

    public void SetInputSource(IDriveInputSource source)
    {
        ClearUserInput();
        ResetBoosts();
        inputSource = source;
    }

    public void SuppressInputAfterRespawn()
    {
        float duration = Mathf.Max(0f, respawnInputSuppressionSeconds);
        inputSuppressedUntil = Mathf.Max(inputSuppressedUntil, Time.time + duration);
        ClearUserInput();
        ResetBoosts();
        itemEffects?.ClearAll();

        foreach (TireForce tire in tires)
        {
            tire.CenterVisualSteering();
        }
    }

    private void OnDisable()
    {
        ClearUserInput();
        ResetBoosts();
    }

    /// <summary>CarItemEffects が自身を登録します。車両生成後に追加された場合にも対応します。</summary>
    public void BindItemEffects(CarItemEffects effects)
    {
        itemEffects = effects;
    }

    /// <summary>スピンなどでドリフト状態とたまったチャージを失わせます。</summary>
    public void CancelDrift()
    {
        ResetDrift();
    }

    /// <summary>
    /// ドリフトチャージを加算します（最大チャージに対する割合）。チャージはドリフト解放まで保持されます。
    /// 既に満タンなどで増えなかった場合は false を返します。
    /// </summary>
    public bool AddDriftCharge(float normalizedAmount)
    {
        if (!enableDriftDynamics || maxDriftCharge <= 0f || normalizedAmount <= 0f) return false;
        float previous = driftCharge;
        driftCharge = Mathf.Clamp(driftCharge + normalizedAmount * maxDriftCharge, 0f, maxDriftCharge);
        return driftCharge > previous + 0.0001f;
    }

    private void ResetDrift(bool resetGrip = true)
    {
        isDrifting = false;
        driftCharge = 0f;
        driftDirection = 0f;
        pendingDriftDirection = 0f;
        driftStartTime = 0f;
        driftReleaseTime = 0f;
        if (resetGrip)
        {
            driftGripBlend = 0f;
            driftRecoveryTimeRemaining = 0f;
        }
        driftBoostTimeRemaining = 0f;
        activeDriftBoostAcceleration = 0f;
    }

    private void ResetBoosts()
    {
        CancelLandingBoost();
        CancelSlipstreamBoost();
        ResetDrift();
        accelerationPadBoostTimeRemaining = 0f;
        accelerationPadBoostDuration = 0f;
        activeAccelerationPadBoostAcceleration = 0f;
        accelerationPadBoostDirection = Vector3.zero;
    }

    private void StartDriftBoost(float acceleration)
    {
        // 再解放時は、新しいチャージ量と設定時間で置き換える。
        driftBoostTimeRemaining = Mathf.Max(0f, driftBoostDuration);
        activeDriftBoostAcceleration = driftBoostTimeRemaining > 0f ? Mathf.Max(0f, acceleration) : 0f;
        if (activeDriftBoostAcceleration > 0f) DriftBoostReleased?.Invoke(
            Mathf.Clamp01(acceleration / Mathf.Max(.0001f, maxDriftCharge * driftBoostAccelerationPerCharge)));
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

    private Vector3 accelerationPadBoostDirection;
    private float accelerationPadBoostDuration;

    /// <summary>加速度盤から呼び出す、質量に依存しない瞬間加速と時間制限付き加速です。</summary>
    public void StartAccelerationPadBoost(float acceleration, float duration, float instantSpeedBonus, Vector3 direction)
    {
        bool wasBoosting = IsAccelerationPadBoosting;
        accelerationPadBoostTimeRemaining = Mathf.Max(0f, duration);
        accelerationPadBoostDuration = accelerationPadBoostTimeRemaining;
        activeAccelerationPadBoostAcceleration = accelerationPadBoostTimeRemaining > 0f
            ? Mathf.Max(0f, acceleration)
            : 0f;
        accelerationPadBoostDirection = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
        if (accelerationPadBoostDirection.sqrMagnitude < 0.0001f)
        {
            accelerationPadBoostDirection = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        }

        // 複数の車体Colliderが同じ盤面へ入っても、瞬間加速は一度だけ与える。
        if (!wasBoosting && accelerationPadBoostTimeRemaining > 0f && instantSpeedBonus > 0f)
        {
            rb.AddForce(GetAccelerationPadBoostDirection() * instantSpeedBonus, ForceMode.VelocityChange);
        }
    }

    private Vector3 GetAccelerationPadBoostDirection()
    {
        Vector3 up = GetDrivingUp();
        Vector3 travel = Vector3.ProjectOnPlane(rb.linearVelocity, up);
        if (travel.sqrMagnitude > 1f) return travel.normalized;

        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, up);
        return forward.sqrMagnitude > 0.0001f ? forward.normalized : accelerationPadBoostDirection;
    }

    private float ConsumeAccelerationPadBoost(float deltaTime, out Vector3 direction)
    {
        float elapsed = Mathf.Min(Mathf.Max(0f, deltaTime), accelerationPadBoostTimeRemaining);
        float speedDelta = activeAccelerationPadBoostAcceleration * elapsed;
        direction = GetAccelerationPadBoostDirection();
        accelerationPadBoostTimeRemaining = Mathf.Max(0f, accelerationPadBoostTimeRemaining - elapsed);
        if (accelerationPadBoostTimeRemaining <= 0f)
        {
            activeAccelerationPadBoostAcceleration = 0f;
            accelerationPadBoostDirection = Vector3.zero;
        }

        return speedDelta;
    }

    private bool HasDriftGroundContact()
    {
        bool front = false, rear = false;
        foreach (TireForce tire in tires)
        {
            if (!tire.GetComponent<GroundCheck>().CheckNow()) continue;
            if (tire.IsFrontWheel) front = true;
            else rear = true;
        }
        return front && rear;
    }

    private float UpdateDrift(float deltaTime)
    {
        float elapsed = Mathf.Max(0f, deltaTime);
        float forwardSpeed = Vector3.Dot(rb.linearVelocity,
            Vector3.ProjectOnPlane(transform.forward, GetDrivingUp()).normalized);
        bool grounded = HasDriftGroundContact();
        float handleMagnitude = Mathf.Abs(rawSteeringInput);
        float startHandle = Mathf.Max(.01f, driftStartHandle);
        float releasedAcceleration = 0f;

        // Low speed and reversing end the slide without rewarding a boost.
        if (isDrifting && forwardSpeed < driftMinimumSpeed * .5f)
            ResetDrift(resetGrip: false);

        if (isDrifting)
        {
            bool countersteering = rawSteeringInput * driftDirection <= -Mathf.Max(.01f, driftCountersteerHandle);
            driftReleaseTime = grounded && countersteering ? driftReleaseTime + elapsed : 0f;
            if (grounded && countersteering && driftReleaseTime >= driftReleaseHoldSeconds)
            {
                releasedAcceleration = driftCharge * Mathf.Max(0f, driftBoostAccelerationPerCharge);
                ResetDrift(resetGrip: false);
            }
            else if (grounded && rawSteeringInput * driftDirection > 0f)
            {
                float chargeRatio = Mathf.Clamp01(handleMagnitude / Mathf.Max(startHandle, driftFullChargeHandle));
                driftCharge = Mathf.Clamp(driftCharge + chargeRatio * Mathf.Max(0f, driftChargePerSecond) * elapsed,
                    0f, Mathf.Max(0f, maxDriftCharge));
            }
        }
        else if (grounded && forwardSpeed >= driftMinimumSpeed && handleMagnitude >= startHandle)
        {
            float direction = Mathf.Sign(rawSteeringInput);
            if (direction != pendingDriftDirection) driftStartTime = 0f;
            pendingDriftDirection = direction;
            float previousStartTime = driftStartTime;
            driftStartTime += elapsed;
            if (driftStartTime >= driftStartHoldSeconds)
            {
                isDrifting = true;
                driftDirection = direction;
                driftStartTime = 0f;
                float chargeElapsed = Mathf.Max(0f, elapsed - Mathf.Max(0f, driftStartHoldSeconds - previousStartTime));
                float chargeRatio = Mathf.Clamp01(handleMagnitude / Mathf.Max(startHandle, driftFullChargeHandle));
                driftCharge = Mathf.Clamp(driftCharge + chargeRatio * Mathf.Max(0f, driftChargePerSecond) * chargeElapsed,
                    0f, Mathf.Max(0f, maxDriftCharge));
            }
        }
        else
        {
            pendingDriftDirection = 0f;
            driftStartTime = 0f;
        }

        // Centering or countersteering restores grip progressively while retaining charge.
        float gripTarget = isDrifting && grounded
            ? Mathf.Clamp01(rawSteeringInput * driftDirection / startHandle) : 0f;
        driftGripBlend = Mathf.MoveTowards(driftGripBlend, gripTarget,
            elapsed / Mathf.Max(.01f, driftGripTransitionSeconds));
        return releasedAcceleration;
    }

    // しきい値を超えた数がそのまま段階になる。表示専用で、解放時の加速量は従来どおりチャージ量に比例する。
    private int GetDriftChargeTier(float normalizedCharge)
    {
        if (driftChargeTierThresholds == null)
        {
            return 0;
        }

        int tier = 0;
        for (int index = 0; index < driftChargeTierThresholds.Length; index++)
        {
            if (normalizedCharge >= driftChargeTierThresholds[index] - 0.0001f)
            {
                tier = index + 1;
            }
        }

        return tier;
    }

    private void RefreshTires()
    {
        tires.Clear();
        tires.AddRange(GetComponentsInChildren<TireForce>());

        float frontZ = 0f, rearZ = 0f;
        int frontCount = 0, rearCount = 0;
        foreach (TireForce tire in tires)
        {
            tire.Init(rb);
            float z = transform.InverseTransformPoint(tire.transform.position).z;
            if (tire.IsFrontWheel) { frontZ += z; frontCount++; }
            else { rearZ += z; rearCount++; }
        }
        if (frontCount > 0 && rearCount > 0)
            wheelbase = Mathf.Max(.5f, Mathf.Abs(frontZ / frontCount - rearZ / rearCount));
    }

    private CourseSteeringAssist courseSteeringAssist;

    private void ReadInput()
    {
        if (!inputSource.IsConnected)
        {
            ClearUserInput();
            ResetBoosts();
            return;
        }
        DriveInputState input = inputSource.CurrentState;
        rawPedalInput = input.pedal;
        appliedPedalInput = Mathf.Clamp(rawPedalInput, -1f, 1f);
        // コンフューズ中はハンドルを左右反転します。ドリフト判定も反転後の値で行います。
        rawSteeringInput = input.steering * (itemEffects != null ? itemEffects.SteeringSign : 1f);
        if (itemEffects != null && itemEffects.BlocksDriverInput)
        {
            ClearUserInput();
            return;
        }

        Vector3 planarVelocity = Vector3.ProjectOnPlane(rb.linearVelocity, GetDrivingUp());
        speedMetersPerSecond = planarVelocity.magnitude;

        appliedSteeringMultiplier = GetSpeedSteeringMultiplier(speedMetersPerSecond);
        steeringAngleLimit = enableSpeedSensitiveSteering
            ? Mathf.Min(maxSteeringAngle, Mathf.Atan(Mathf.Max(1f, steeringLateralAcceleration) * wheelbase /
                Mathf.Max(.01f, speedMetersPerSecond * speedMetersPerSecond)) * Mathf.Rad2Deg)
            : maxSteeringAngle;
        float limit = SteeringInputLimit;
        float request = Mathf.Sign(rawSteeringInput) * Mathf.Max(0f, Mathf.Abs(rawSteeringInput) - steeringDeadzone);
        // Saturate before filtering: turning past the limit must never store a delayed countersteer.
        request = Mathf.Clamp(request, -limit, limit);
        filteredSteeringInput = Mathf.Clamp(filteredSteeringInput, -limit, limit);
        // Keep small IMU fluctuations on one time constant; otherwise faster falling samples bias the center.
        bool returning = Mathf.Abs(request) < Mathf.Abs(filteredSteeringInput) * .65f || request * filteredSteeringInput < 0f;
        float response = steeringResponseSeconds * (returning ? .55f : 1f);
        float blend = response <= 0f ? 1f : 1f - Mathf.Exp(-Time.fixedDeltaTime / response);
        filteredSteeringInput = Mathf.Lerp(filteredSteeringInput, request, blend);
        appliedSteeringAngle = Mathf.Clamp(filteredSteeringInput * steeringInputMultiplier * appliedSteeringMultiplier,
            -steeringAngleLimit, steeringAngleLimit);
        if (courseSteeringAssist == null) courseSteeringAssist = GetComponent<CourseSteeringAssist>();
        if (courseSteeringAssist != null)
            appliedSteeringAngle = courseSteeringAssist.Assist(appliedSteeringAngle, steeringAngleLimit, GetDrivingUp(), Time.fixedDeltaTime);
    }

    // 遅いほど大きく、速いほど小さい操舵倍率を返す。ドリフト判定に使う生のハンドル入力には影響しない。
    private float GetSpeedSteeringMultiplier(float speed)
    {
        if (!enableSpeedSensitiveSteering)
        {
            return 1f;
        }

        float fullSpeed = Mathf.Max(steeringFadeStartSpeed + 0.01f, steeringFadeFullSpeed);
        float speedRatio = Mathf.InverseLerp(steeringFadeStartSpeed, fullSpeed, speed);
        // 1より大きい鋭さでは低速側の効きを保ち、1より小さいと早い段階から曲がりにくくする。
        float fadeRatio = Mathf.Pow(speedRatio, Mathf.Max(0.01f, steeringFadeSharpness));
        float multiplier = Mathf.Lerp(1f, Mathf.Clamp01(highSpeedSteeringMultiplier), fadeRatio);
        // Keep reducing beyond the fade range. Constant lock would increase yaw with speed.
        float overspeed = Mathf.Max(1f, speed / fullSpeed);
        return multiplier / (overspeed * overspeed);
    }

    private void ClearUserInput()
    {
        courseSteeringAssist?.Clear();
        rawPedalInput = 0f;
        appliedPedalInput = 0f;
        rawSteeringInput = 0f;
        appliedSteeringAngle = 0f;
        appliedSteeringMultiplier = 1f;
        filteredSteeringInput = 0f;
        steeringAngleLimit = maxSteeringAngle;
    }

    private bool ShouldProtectDrift => enableDriftDynamics && enableDriftSpinProtection &&
        !IsInputSuppressed && (itemEffects == null || !itemEffects.BlocksDriverInput) &&
        (isDrifting || driftStartTime > 0f || driftGripBlend > .001f || driftBoostTimeRemaining > 0f || driftRecoveryTimeRemaining > 0f);

    private float GetDriftYawLimit(float speed)
    {
        return Mathf.Min(driftMaxYawRate, driftMaxLateralAcceleration / Mathf.Max(1f, speed));
    }

    private float GetProtectedSteeringAngle(Vector3 forward, Vector3 velocity)
    {
        float safeAngle = GetDriftSteeringAngleLimit(forward, velocity);
        return Mathf.Clamp(appliedSteeringAngle, -safeAngle, safeAngle);
    }

    private float GetDriftSteeringAngleLimit(Vector3 forward, Vector3 velocity)
    {
        float forwardSpeed = Vector3.Dot(velocity, forward);
        if (!ShouldProtectDrift || forwardSpeed < 3f || !HasDriftGroundContact()) return float.PositiveInfinity;
        // At speed, full lock asks more lateral force than the tires can supply and builds a spin.
        return Mathf.Atan(GetDriftYawLimit(velocity.magnitude) * wheelbase / forwardSpeed) * Mathf.Rad2Deg;
    }

    private void ApplyDriftSpinProtection(float deltaTime)
    {
        if (isDrifting || driftStartTime > 0f || driftGripBlend > .001f || driftBoostTimeRemaining > 0f)
            driftRecoveryTimeRemaining = driftRecoverySeconds;
        else
            driftRecoveryTimeRemaining = Mathf.Max(0f, driftRecoveryTimeRemaining - deltaTime);
        if (!ShouldProtectDrift || !HasDriftGroundContact()) return;

        Vector3 up = GetDrivingUp();
        Vector3 forward = Vector3.ProjectOnPlane(rb.rotation * Vector3.forward, up).normalized;
        Vector3 velocity = Vector3.ProjectOnPlane(rb.linearVelocity, up);
        float forwardSpeed = Vector3.Dot(velocity, forward);
        if (forwardSpeed < 3f) return;

        float yawLimit = GetDriftYawLimit(velocity.magnitude);
        float steeringAngle = GetProtectedSteeringAngle(forward, velocity);
        float desiredYaw = Mathf.Clamp(forwardSpeed * Mathf.Tan(steeringAngle * Mathf.Deg2Rad) / wheelbase,
            -yawLimit, yawLimit);
        float slipAngle = Vector3.SignedAngle(velocity, forward, up);
        float excessSlip = slipAngle - Mathf.Clamp(slipAngle, -driftMaxSlipAngle, driftMaxSlipAngle);
        // Ease the body back toward its direction of travel rather than allowing rotation to run away.
        desiredYaw = Mathf.Clamp(desiredYaw - excessSlip * Mathf.Deg2Rad * driftYawResponse, -yawLimit, yawLimit);
        float yawRate = Vector3.Dot(rb.angularVelocity, up);
        float yawAcceleration = Mathf.Clamp((desiredYaw - yawRate) * driftYawResponse,
            -driftMaxYawAcceleration, driftMaxYawAcceleration);
        rb.AddTorque(up * yawAcceleration, ForceMode.Acceleration);

        if (Mathf.Abs(excessSlip) > .01f)
        {
            float correction = Mathf.Min(Mathf.Abs(excessSlip), driftSlipRecoveryDegreesPerSecond * deltaTime);
            Vector3 correctedVelocity = Quaternion.AngleAxis(Mathf.Sign(excessSlip) * correction, up) * velocity;
            // Only redirect excess slip within the road plane; preserve speed and separation/landing motion.
            rb.AddForce(correctedVelocity - velocity, ForceMode.VelocityChange);
        }
    }

    private void ApplyTireForces()
    {
        Vector3 up = GetDrivingUp();
        Vector3 vehicleForward = Vector3.ProjectOnPlane(rb.rotation * Vector3.forward, up).normalized;
        Vector3 velocity = Vector3.ProjectOnPlane(rb.linearVelocity, up);
        steeringAngleLimit = Mathf.Min(steeringAngleLimit, GetDriftSteeringAngleLimit(vehicleForward, velocity));
        float tireSteeringAngle = Mathf.Clamp(appliedSteeringAngle, -steeringAngleLimit, steeringAngleLimit);
        appliedSteeringAngle = tireSteeringAngle;

        foreach (TireForce tire in tires)
        {
            float corneringStiffness = tire.IsFrontWheel
                ? frontCorneringStiffness
                : rearCorneringStiffness;
            float gripMultiplier = !tire.IsFrontWheel
                && enableDriftDynamics && enableDriftRearGripReduction
                ? Mathf.Lerp(1f, Mathf.Max(0f, driftRearGripMultiplier), driftGripBlend)
                : 1f;
            // オイルやスピンによるグリップ低下を重ねます。
            if (itemEffects != null)
            {
                gripMultiplier *= tire.IsFrontWheel
                    ? itemEffects.FrontGripMultiplier
                    : itemEffects.RearGripMultiplier;
            }

            tire.ApplyForces(
                vehicleForward,
                up,
                tireSteeringAngle,
                appliedPedalInput,
                driveForcePerFrontWheel,
                corneringStiffness * gripMultiplier,
                maxLateralForcePerTire * gripMultiplier,
                enableLateralTireForces,
                enableFrontDriveForce);
        }
    }

    // All driving forces share the contact plane, including on banked tube walls.
    // With no wheel contact, retain world-horizontal airborne momentum handling.
    private Vector3 GetDrivingUp()
    {
        if (useCachedDrivingUp) return drivingUp;
        Vector3 normal = Vector3.zero;
        foreach (TireForce tire in tires)
        {
            GroundCheck ground = tire.GetComponent<GroundCheck>();
            if (ground.CheckNow()) normal += ground.GroundHit.normal;
        }
        return normal.sqrMagnitude > .0001f ? normal.normalized : Vector3.up;
    }

    private void ApplyVelocityResistance()
    {
        Vector3 planarVelocity = Vector3.ProjectOnPlane(rb.linearVelocity, GetDrivingUp());
        bool grounded = false;
        foreach (TireForce tire in tires)
        {
            if (!tire.GetComponent<GroundCheck>().CheckNow()) continue;
            grounded = true;
            break;
        }
        // In flight there is no tire propulsion to balance road resistance; retain takeoff momentum.
        float resistanceMultiplier = grounded ? 1f : Mathf.Clamp01(airborneResistanceMultiplier);
        Vector3 resistance = -planarVelocity * velocityResistance * resistanceMultiplier;
        if (grounded && enableDriftDynamics)
        {
            resistance *= Mathf.Lerp(1f, Mathf.Max(1f, driftResistanceMultiplier), driftGripBlend);
        }
        resistanceForce = resistance.magnitude;
        rb.AddForce(resistance, ForceMode.Force);
    }
}
