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

    [Tooltip("操舵角を小さくしていく曲線の形。1で直線的、1より大きいと高速側で一気に曲がりにくくなり、1より小さいと低速側から曲がりにくくなります。")]
    [SerializeField, Range(0.1f, 5f)] private float steeringFadeSharpness = 1f;

    [Tooltip("速度に応じて操舵角を変えるか。無効にすると全速度で同じ曲がりやすさになります。")]
    [SerializeField] private bool enableSpeedSensitiveSteering = true;

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

    private Rigidbody rb;
    private IDriveInputSource inputSource;
    private float inputSuppressedUntil;
    private float driftDirection;
    private CarItemEffects itemEffects;

    public IDriveInputSource InputSource => inputSource;
    /// <summary>実際に走行へ反映しているペダル入力（-1:ブレーキ 〜 1:アクセル）。</summary>
    public float PedalInput => appliedPedalInput;
    public float SpeedMetersPerSecond => speedMetersPerSecond;
    /// <summary>速度によって操舵角にかけている倍率（1で通常、小さいほど曲がりにくい）。</summary>
    public float SteeringSpeedMultiplier => appliedSteeringMultiplier;
    public bool IsInputSuppressed => Time.time < inputSuppressedUntil;
    public bool IsDrifting => isDrifting;
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
    public bool IsRocketBoosting => isActiveAndEnabled && itemEffects != null && itemEffects.IsRocketActive;
    /// <summary>ドリフト、加速度盤、ロケットを合わせた、既存の加速画面演出用の強度です。</summary>
    public float BoostVisualIntensity => Mathf.Max(DriftBoostVisualIntensity,
        IsAccelerationPadBoosting || IsRocketBoosting ? 1f : 0f);

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        if (itemEffects == null) itemEffects = GetComponent<CarItemEffects>();
        RefreshTires();
    }

    private void FixedUpdate()
    {
        if (inputSource == null ||
            Gmanager.Control == null ||
            !Gmanager.Control.IsDrivingEnabled)
        {
            ResetBoosts();
            return;
        }

        if (IsInputSuppressed)
        {
            ClearUserInput();
            ResetBoosts();
        }
        else if (itemEffects != null && itemEffects.IsRocketActive)
        {
            // ロケット中は自動操縦が速度と向きを直接決めるため、運転入力・タイヤ力・速度抵抗を使いません。
            // ドリフトチャージは保持し、ロケット後に解放できるようにします。
            ClearUserInput();
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
                Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
                // 加速度×経過時間を各物理フレームに加算し、質量に依存しない持続加速にする。
                rb.AddForce(forward * boostSpeedDelta, ForceMode.VelocityChange);
            }

            float padSpeedDelta = ConsumeAccelerationPadBoost(Time.fixedDeltaTime, out Vector3 padDirection);
            if (padSpeedDelta > 0f)
            {
                rb.AddForce(padDirection * padSpeedDelta, ForceMode.VelocityChange);
            }
        }

        ApplyTireForces();
        ApplyVelocityResistance();
    }

    public void SetInputSource(IDriveInputSource source)
    {
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

    private void ResetDrift()
    {
        isDrifting = false;
        driftCharge = 0f;
        driftDirection = 0f;
        driftBoostTimeRemaining = 0f;
        activeDriftBoostAcceleration = 0f;
    }

    private void ResetBoosts()
    {
        ResetDrift();
        accelerationPadBoostTimeRemaining = 0f;
        activeAccelerationPadBoostAcceleration = 0f;
        accelerationPadBoostDirection = Vector3.zero;
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

    private Vector3 accelerationPadBoostDirection;

    /// <summary>加速度盤から呼び出す、質量に依存しない時間制限付き加速です。</summary>
    public void StartAccelerationPadBoost(float acceleration, float duration, Vector3 direction)
    {
        accelerationPadBoostTimeRemaining = Mathf.Max(0f, duration);
        activeAccelerationPadBoostAcceleration = accelerationPadBoostTimeRemaining > 0f
            ? Mathf.Max(0f, acceleration)
            : 0f;
        accelerationPadBoostDirection = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
        if (accelerationPadBoostDirection.sqrMagnitude < 0.0001f)
        {
            accelerationPadBoostDirection = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        }
    }

    private float ConsumeAccelerationPadBoost(float deltaTime, out Vector3 direction)
    {
        float elapsed = Mathf.Min(Mathf.Max(0f, deltaTime), accelerationPadBoostTimeRemaining);
        float speedDelta = activeAccelerationPadBoostAcceleration * elapsed;
        direction = accelerationPadBoostDirection;
        accelerationPadBoostTimeRemaining = Mathf.Max(0f, accelerationPadBoostTimeRemaining - elapsed);
        if (accelerationPadBoostTimeRemaining <= 0f)
        {
            activeAccelerationPadBoostAcceleration = 0f;
            accelerationPadBoostDirection = Vector3.zero;
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
        // コンフューズ中はハンドルを左右反転します。ドリフト判定も反転後の値で行います。
        rawSteeringInput = input.steering * (itemEffects != null ? itemEffects.SteeringSign : 1f);
        if (itemEffects != null && itemEffects.BlocksDriverInput)
        {
            rawPedalInput = 0f;
            appliedPedalInput = 0f;
            rawSteeringInput = 0f;
        }

        Vector3 planarVelocity = Vector3.ProjectOnPlane(rb.linearVelocity, Vector3.up);
        speedMetersPerSecond = planarVelocity.magnitude;

        appliedSteeringMultiplier = GetSpeedSteeringMultiplier(speedMetersPerSecond);
        appliedSteeringAngle = Mathf.Clamp(
            rawSteeringInput * steeringInputMultiplier * appliedSteeringMultiplier,
            -maxSteeringAngle,
            maxSteeringAngle);
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
        return Mathf.Lerp(1f, Mathf.Clamp01(highSpeedSteeringMultiplier), fadeRatio);
    }

    private void ClearUserInput()
    {
        rawPedalInput = 0f;
        appliedPedalInput = 0f;
        rawSteeringInput = 0f;
        appliedSteeringAngle = 0f;
        appliedSteeringMultiplier = 1f;
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
                && enableDriftDynamics && enableDriftRearGripReduction
                ? Mathf.Max(0f, driftRearGripMultiplier)
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
                Vector3.up,
                appliedSteeringAngle,
                appliedPedalInput,
                driveForcePerFrontWheel,
                corneringStiffness * gripMultiplier,
                maxLateralForcePerTire * gripMultiplier,
                enableLateralTireForces,
                enableFrontDriveForce);
        }
    }

    private void ApplyVelocityResistance()
    {
        Vector3 planarVelocity = Vector3.ProjectOnPlane(rb.linearVelocity, Vector3.up);
        Vector3 resistance = -planarVelocity * velocityResistance;
        if (enableDriftDynamics && isDrifting)
        {
            resistance *= Mathf.Max(1f, driftResistanceMultiplier);
        }
        resistanceForce = resistance.magnitude;
        rb.AddForce(resistance, ForceMode.Force);
    }
}
