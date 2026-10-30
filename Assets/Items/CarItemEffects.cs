using System;
using UnityEngine;

public enum ItemImpactKind { OilSlip, ShieldSpin, SteeringReversed, ConfusedWallContact }

/// <summary>
/// 車両ごとのアイテム効果（シールド、ロケット、オイル、コンフューズ、スピン）の状態を持ちます。
/// 走行入力やタイヤ力への反映は DebugMover がこのコンポーネントを参照して行います。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class CarItemEffects : MonoBehaviour
{
    [Header("Shield")]
    [Tooltip("シールドの持続時間（秒）。")]
    [SerializeField, Min(0f)] private float shieldDuration = 6f;
    [Tooltip("相手をスピンさせたらシールドを消費するか。")]
    [SerializeField] private bool consumeShieldOnHit = true;
    [SerializeField] private Color shieldColor = new Color(0.25f, 0.85f, 1f, 0.28f);
    [SerializeField, Min(0.5f)] private float shieldRadius = 2.1f;

    [Header("Spin (シールドで弾かれた側)")]
    [Tooltip("スピンして操作できない時間（秒）。")]
    [SerializeField, Min(0.1f)] private float spinDuration = 1.2f;
    [Tooltip("スピン中の回転数。")]
    [SerializeField, Min(0f)] private float spinRevolutions = 1.5f;
    [Tooltip("スピン開始時に残す水平速度の割合。")]
    [SerializeField, Range(0f, 1f)] private float spinSpeedRetention = 0.45f;
    [Tooltip("スピン中のタイヤグリップ倍率。小さいほど滑ります。")]
    [SerializeField, Range(0f, 1f)] private float spinGripMultiplier = 0.1f;

    [Header("Rocket")]
    [Tooltip("ロケットの持続時間（秒）。この間は自動操縦になります。")]
    [SerializeField, Min(0f)] private float rocketDuration = 3f;
    [Tooltip("発動直後に保証する最低速度（m/s）。")]
    [SerializeField, Min(0f)] private float rocketStartSpeed = 35f;
    [Tooltip("ロケットの最高速度（m/s）。")]
    [SerializeField, Min(1f)] private float rocketTopSpeed = 240f / 3.6f;
    [Tooltip("最高速度へ向かう加速度（m/s²）。")]
    [SerializeField, Min(0f)] private float rocketAcceleration = 45f;
    [Tooltip("コース中心線上の目標点を、何秒先の位置に置くか。")]
    [SerializeField, Min(0f)] private float rocketLookAheadSeconds = 0.25f;
    [Tooltip("目標点までの最短距離（m）。")]
    [SerializeField, Min(1f)] private float rocketMinLookAhead = 8f;
    [Tooltip("自動操縦で向きを変えられる最大の速さ（度/秒）。")]
    [SerializeField, Min(1f)] private float rocketTurnRate = 240f;
    [Tooltip("ロケット終了後、プレイヤー操作へ戻す前に入力を無効にする時間（秒）。急なハンドル操作での事故を防ぎます。")]
    [SerializeField, Min(0f)] private float rocketExitGraceSeconds = 0.15f;

    [Tooltip("ジャンプ付近でロケットの水平速度を制限する値（m/s）。")]
    [SerializeField, Min(1f)] private float rocketJumpSpeed = 22f;
    [Tooltip("踏切へ向かう減速の強さ（m/s²）。")]
    [SerializeField, Min(1f)] private float rocketJumpBraking = 65f;
    private bool protectRocketJump;
    private GroundCheck[] rocketGroundChecks;

    [Header("Oil")]
    [Tooltip("オイルを踏んだ後に滑る時間（秒）。")]
    [SerializeField, Min(0f)] private float oilDuration = 2.2f;
    [SerializeField, Range(0f, 1f)] private float oilFrontGripMultiplier = 0.6f;
    [SerializeField, Range(0f, 1f)] private float oilRearGripMultiplier = 0.3f;
    [Tooltip("踏んだ瞬間に加える横回転（rad/s）。向きはランダムです。")]
    [SerializeField, Min(0f)] private float oilYawKick = 2.2f;
    [Tooltip("アイテム取得からオイルを後方へ置くまでの時間（秒）。")]
    [SerializeField, Min(0f)] private float oilDropDelay = 0.5f;
    [Tooltip("車体の後ろへ何メートル離して置くか。")]
    [SerializeField, Min(0f)] private float oilDropDistance = 4f;

    [Header("Confuse")]
    [Tooltip("ハンドルが左右反転する時間（秒）。")]
    [SerializeField, Min(0f)] private float confuseDuration = 2.5f;

    private readonly HitProtectionWindow hitProtection = new HitProtectionWindow();
    private bool lastProtectionEnabled, lastGentleEnabled;
    public float HitProtectionRemaining => hitProtection.ProtectionRemaining;

    public void ApplyFeatureSwitches()
    {
        bool protect = RaceFeatureController.Enabled(RaceFeature.HitProtection);
        bool gentle = RaceFeatureController.Enabled(RaceFeature.ExperienceItems);
        if (protect != lastProtectionEnabled)
        {
            hitProtection.Clear();
            if (protect)
            {
                float active = Mathf.Max(oilTimeRemaining, Mathf.Max(confuseTimeRemaining, spinTimeRemaining));
                if (active > 0f)
                {
                    hitProtection.TryBegin(active);
                    oilTimeRemaining = Mathf.Min(oilTimeRemaining, HitProtectionWindow.MaximumEffectSeconds);
                    confuseTimeRemaining = Mathf.Min(confuseTimeRemaining, HitProtectionWindow.MaximumEffectSeconds);
                    spinTimeRemaining = Mathf.Min(spinTimeRemaining, HitProtectionWindow.MaximumEffectSeconds);
                }
            }
        }
        if (gentle && !lastGentleEnabled) { ClearHarmfulEffects(); hitProtection.Clear(); }
        lastProtectionEnabled = protect; lastGentleEnabled = gentle;
    }
    public void ClearHarmfulEffects(bool cancelPendingOil = true)
    {
        if (spinTimeRemaining > 0f && body != null && !body.isKinematic)
        {
            Vector3 angular = body.angularVelocity; angular.y = 0f; body.angularVelocity = angular;
        }
        oilTimeRemaining = confuseTimeRemaining = spinTimeRemaining = 0f;
        confuseSource = null;
        confuseWallReported = false;
        if (cancelPendingOil) oilDropTimeRemaining = -1f;
        if (body != null) body.maxAngularVelocity = originalMaxAngularVelocity;
    }
    private bool AcceptAffliction(float duration)
    {
        ApplyFeatureSwitches();
        if (RaceFeatureController.Enabled(RaceFeature.ExperienceItems) ||
            Gmanager.Control != null && Gmanager.Control.IsDebuffProtected(body)) return false;
        if (!RaceFeatureController.Enabled(RaceFeature.HitProtection)) return true;
        Gmanager race = Gmanager.Control;
        if (race != null && race.course != null &&
            (race.course.IsJumpAhead(transform.position, race.CourseProgressSign) ||
            mover != null && !mover.HasGroundedWheels)) return false;
        return hitProtection.TryBegin(duration);
    }
    private float AfflictionDuration(float seconds) => RaceFeatureController.Enabled(RaceFeature.HitProtection)
        ? Mathf.Min(seconds, HitProtectionWindow.MaximumEffectSeconds) : seconds;

    private Rigidbody body;
    private DebugMover mover;
    private float shieldTimeRemaining;
    private float rocketTimeRemaining;
    private float rocketCurrentSpeed;
    private float rocketExitGraceRemaining;
    private float oilTimeRemaining;
    private float oilDropTimeRemaining = -1f;
    private float confuseTimeRemaining;
    private float spinTimeRemaining;
    private float recoveryProtectionRemaining;
    private float spinStartRate;
    private float originalMaxAngularVelocity;
    private GameObject shieldBubble;
    private Mesh shieldMesh;
    private Material shieldMaterial;

    /// <summary>自分がアイテムボックスから引いて使ったアイテムを HUD へ知らせます。</summary>
    public event Action<RaceItemType> ItemUsed;
    /// <summary>
    /// 相手のアイテムで妨害を受けた瞬間を知らせます。
    /// Oil はオイルを踏んだ、Confuse はハンドル反転、Shield はシールドに弾かれてスピンしたことを表します。
    /// </summary>
    public event Action<RaceItemType> Afflicted;
    /// <summary>この車が発動した妨害が実際に成立した相手と結果。防御・自己被弾は含みません。</summary>
    public event Action<CarItemEffects, ItemImpactKind> AttackLanded;
    /// <summary>ドリフトチャージを拾った瞬間を知らせます。</summary>
    public event Action ChargeCollected;

    public int PlayerIndex { get; private set; } = -1;
    private CarItemEffects confuseSource;
    private bool confuseWallReported;
    public bool IsShieldActive => shieldTimeRemaining > 0f;
    public bool IsRocketActive => rocketTimeRemaining > 0f;
    public bool IsOiled => oilTimeRemaining > 0f;
    public bool IsConfused => confuseTimeRemaining > 0f;
    public bool IsSpinning => spinTimeRemaining > 0f;
    public bool IsRecoveryProtected => recoveryProtectionRemaining > 0f;
    public float ShieldTimeRemaining => shieldTimeRemaining;
    public float RocketTimeRemaining => rocketTimeRemaining;
    public float ConfuseTimeRemaining => confuseTimeRemaining;
    public float OilTimeRemaining => oilTimeRemaining;
    public float OilDuration => oilDuration;
    public float ShieldDuration => shieldDuration;
    public float RocketDuration => rocketDuration;
    public float ConfuseDuration => confuseDuration;

    /// <summary>ハンドル入力へ掛ける符号です。コンフューズ中は -1 になります。</summary>
    public float SteeringSign => IsConfused ? -1f : 1f;
    /// <summary>運転入力を受け付けない状態か（スピン中、ロケット直後の切り替え中）。</summary>
    public bool BlocksDriverInput => IsSpinning || rocketExitGraceRemaining > 0f;
    public float FrontGripMultiplier => IsSpinning ? spinGripMultiplier : IsOiled ? oilFrontGripMultiplier : 1f;
    public float RearGripMultiplier => IsSpinning ? spinGripMultiplier : IsOiled ? oilRearGripMultiplier : 1f;
    // 走行終了で無効化された車は妨害を受けません。
    private bool IsReceptive => enabled && gameObject.activeInHierarchy && !IsRecoveryProtected;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        mover = GetComponent<DebugMover>();
        rocketGroundChecks = GetComponentsInChildren<GroundCheck>();
        mover?.BindItemEffects(this);
        originalMaxAngularVelocity = body.maxAngularVelocity;
    }

    public void Configure(int playerIndex)
    {
        PlayerIndex = playerIndex;
    }

    private void OnDisable()
    {
        ClearAll();
    }

    private void OnDestroy()
    {
        ItemVisualFactory.DestroyObject(shieldBubble);
        ItemVisualFactory.DestroyObject(shieldMesh);
        ItemVisualFactory.DestroyObject(shieldMaterial);
    }

    private void FixedUpdate()
    {
        if (Gmanager.Control == null || !Gmanager.Control.IsDrivingEnabled)
        {
            ClearAll();
            return;
        }

        ApplyFeatureSwitches();
        float dt = Time.fixedDeltaTime;
        hitProtection.Tick(dt);
        recoveryProtectionRemaining = Mathf.Max(0f, recoveryProtectionRemaining - dt);
        shieldTimeRemaining = Mathf.Max(0f, shieldTimeRemaining - dt);
        oilTimeRemaining = Mathf.Max(0f, oilTimeRemaining - dt);
        confuseTimeRemaining = Mathf.Max(0f, confuseTimeRemaining - dt);
        if (confuseTimeRemaining <= 0f) confuseSource = null;
        rocketExitGraceRemaining = Mathf.Max(0f, rocketExitGraceRemaining - dt);
        if (rocketTimeRemaining > 0f)
        {
            rocketTimeRemaining = Mathf.Max(0f, rocketTimeRemaining - dt);
            if (rocketTimeRemaining <= 0f) rocketExitGraceRemaining = rocketExitGraceSeconds;
        }
        TickSpin(dt);
        TickOilDrop(dt);
    }

    private void LateUpdate()
    {
        UpdateShieldVisual();
    }

    /// <summary>アイテムボックスから引いたアイテムを発動します。相手に作用するものは opponent を使います。</summary>
    public void UseItem(RaceItemType item, CarItemEffects opponent)
    {
        if (RaceFeatureController.Enabled(RaceFeature.ExperienceItems) &&
            (item == RaceItemType.Oil || item == RaceItemType.Confuse)) return;
        switch (item)
        {
            case RaceItemType.Shield:
                shieldTimeRemaining = shieldDuration;
                break;
            case RaceItemType.Rocket:
                StartRocket();
                break;
            case RaceItemType.Oil:
                oilDropTimeRemaining = oilDropDelay;
                break;
            case RaceItemType.Confuse:
                if (opponent != null && opponent != this &&
                    Gmanager.Control?.FindParticipant(opponent.body)?.IsCpu != false) opponent.ApplyConfuse(this);
                break;
            default:
                return;
        }

        ItemUsed?.Invoke(item);
    }

    public void NotifyChargeCollected()
    {
        ChargeCollected?.Invoke();
    }

    /// <summary>
    /// オイルに触れたときに呼びます。油だまりを消費する場合に true を返します。
    /// シールド中・ロケット中は滑りませんが、油だまりは消費します。
    /// </summary>
    public bool ApplyOil(CarItemEffects source = null)
    {
        if (!IsReceptive) return false;
        if (IsShieldActive || IsRocketActive) return true;

        if (!AcceptAffliction(oilDuration)) return false;
        oilTimeRemaining = AfflictionDuration(oilDuration);
        if (body != null && !body.isKinematic)
        {
            float direction = UnityEngine.Random.value < 0.5f ? -1f : 1f;
            body.angularVelocity += Vector3.up * oilYawKick * direction;
        }
        Afflicted?.Invoke(RaceItemType.Oil);
        if (IsOiled) ReportAttack(source, ItemImpactKind.OilSlip);
        return true;
    }

    /// <summary>相手からのコンフューズです。シールド中は無効です。</summary>
    public bool ApplyConfuse(CarItemEffects source = null)
    {
        if (!IsReceptive || IsShieldActive) return false;
        if (!AcceptAffliction(confuseDuration)) return false;
        confuseTimeRemaining = AfflictionDuration(confuseDuration);
        confuseSource = source;
        confuseWallReported = false;
        Afflicted?.Invoke(RaceItemType.Confuse);
        if (IsConfused) ReportAttack(source, ItemImpactKind.SteeringReversed);
        return true;
    }

    /// <summary>シールド中の相手に当たったときのスピンです。シールド中・ロケット中・スピン中は無効です。</summary>
    public bool ApplySpin(Vector3 hitDirection, CarItemEffects source = null)
    {
        if (!IsReceptive || IsShieldActive || IsRocketActive || IsSpinning || body == null) return false;

        // 当たった向きに応じて回転方向を決め、弾かれたように見せます。
        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        float side = Vector3.Cross(forward, Vector3.ProjectOnPlane(hitDirection, Vector3.up)).y;
        float direction = side >= 0f ? 1f : -1f;

        if (!AcceptAffliction(spinDuration)) return false;
        spinTimeRemaining = AfflictionDuration(spinDuration);
        // 線形に減衰させたとき、総回転角が設定回転数になる初速です。
        spinStartRate = direction * 2f * spinRevolutions * Mathf.PI * 2f / Mathf.Max(0.1f, spinDuration);
        body.maxAngularVelocity = Mathf.Max(originalMaxAngularVelocity, Mathf.Abs(spinStartRate) + 1f);
        if (!body.isKinematic)
        {
            Vector3 velocity = body.linearVelocity;
            Vector3 vertical = Vector3.Project(velocity, Vector3.up);
            body.linearVelocity = vertical + (velocity - vertical) * spinSpeedRetention;
        }

        mover?.CancelDrift();
        Afflicted?.Invoke(RaceItemType.Shield);
        ReportAttack(source, ItemImpactKind.ShieldSpin);
        return true;
    }

    private void ReportAttack(CarItemEffects source, ItemImpactKind kind)
    {
        if (source != null && source != this && source.isActiveAndEnabled)
            source.AttackLanded?.Invoke(this, kind);
    }

    public void ClearAll()
    {
        hitProtection.Clear();
        recoveryProtectionRemaining = 0f;
        shieldTimeRemaining = 0f;
        rocketTimeRemaining = 0f;
        protectRocketJump = false;
        rocketExitGraceRemaining = 0f;
        oilTimeRemaining = 0f;
        oilDropTimeRemaining = -1f;
        confuseTimeRemaining = 0f;
        confuseSource = null;
        confuseWallReported = false;
        spinTimeRemaining = 0f;
        if (body != null) body.maxAngularVelocity = originalMaxAngularVelocity;
        UpdateShieldVisual();
    }

    public void GrantRecoveryProtection(float seconds)
    {
        ClearAll();
        recoveryProtectionRemaining = Mathf.Max(0f, seconds);
    }

    public void StopRocketAutopilot()
    {
        rocketTimeRemaining = rocketExitGraceRemaining = 0f;
        protectRocketJump = false;
    }

    private void StartRocket()
    {
        PlayRocketSound();
        rocketTimeRemaining = rocketDuration;
        protectRocketJump = true;
        rocketExitGraceRemaining = 0f;
        float currentSpeed = body != null ? Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up).magnitude : 0f;
        rocketCurrentSpeed = Mathf.Max(currentSpeed, rocketStartSpeed);
        // 発動と同時に、スピンやオイルの滑りから立て直します。
        spinTimeRemaining = 0f;
        oilTimeRemaining = 0f;
        if (body != null) body.maxAngularVelocity = originalMaxAngularVelocity;
    }

    private void PlayRocketSound()
    {
        var manager = Gmanager.Control;
        if (manager == null) return;
        var driver = manager.FindParticipant(body);
        if (manager.state == Gmanager.State.Game && driver != null && !driver.IsCpu && driver.IsRacing)
        {
            RaceSoundEffects.PlayForPlayer(RaceSoundCue.Rocket,driver.Index);
            return;
        }
        for (int player=0;player<2;player++)
            if (manager.TutorialCar(player) == transform)
            { RaceSoundEffects.PlayForPlayer(RaceSoundCue.Rocket,player); return; }
    }

    /// <summary>
    /// ロケット中に DebugMover から毎物理フレーム呼ばれ、コース中心線の少し先へ向けて速度と向きを直接決めます。
    /// 残り時間の管理は FixedUpdate で行います。
    /// </summary>
    public void ApplyRocketAutopilot(float deltaTime)
    {
        if (body == null || body.isKinematic || !IsRocketActive) return;

        float dt = Mathf.Max(0f, deltaTime);
        float safeSpeed = GetRocketJumpSpeedLimit();
        rocketCurrentSpeed = Mathf.Min(safeSpeed,
            Mathf.MoveTowards(rocketCurrentSpeed, rocketTopSpeed, rocketAcceleration * dt));

        Vector3 currentForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        if (currentForward.sqrMagnitude < 0.0001f) currentForward = Vector3.forward;
        Vector3 desired = GetRocketDirection(currentForward);
        Vector3 newForward = Vector3.RotateTowards(currentForward, desired,
            rocketTurnRate * Mathf.Deg2Rad * dt, 0f).normalized;

        body.MoveRotation(Quaternion.FromToRotation(currentForward, newForward) * body.rotation);
        Vector3 vertical = Vector3.Project(body.linearVelocity, Vector3.up);
        body.linearVelocity = newForward * rocketCurrentSpeed + vertical;
        // 自動操縦中はヨー回転を止め、ロール・ピッチは車体安定化に任せます。
        body.angularVelocity = Vector3.ProjectOnPlane(body.angularVelocity, Vector3.up);
    }

    // Preserve the approach/landing limit even if the Rocket expires during flight.
    // Ordinary jumps still retain their momentum; only a Rocket arms this safeguard.
    public void LimitRocketJumpMomentum()
    {
        if (!protectRocketJump || body == null || body.isKinematic) return;
        float limit = GetRocketJumpSpeedLimit();
        Vector3 velocity = body.linearVelocity;
        float horizontal = Vector3.ProjectOnPlane(velocity, Vector3.up).magnitude;
        if (horizontal <= limit) return;
        float ratio = limit / horizontal;
        velocity.x *= ratio;
        velocity.z *= ratio;
        // Ramp-induced upward momentum must slow with the car. Never slow its fall.
        if (velocity.y > 0f) velocity.y *= ratio;
        body.linearVelocity = velocity;
    }

    private float GetRocketJumpSpeedLimit()
    {
        RaceCourse course = Gmanager.Control != null ? Gmanager.Control.course : null;
        if (course == null || !protectRocketJump) return float.PositiveInfinity;
        float progress = course.GetProgressDistance(body.position);
        float direction = Gmanager.Control.CourseProgressSign;
        // A gap behind a grounded car is already cleared. The old fixed 60m run-out
        // could consume almost the entire Rocket at 22m/s, even for a pickup AFTER landing.
        // Query contacts now: TireForce.IsGrounded is stale while autopilot bypasses tire forces.
        bool grounded = false;
        if (rocketGroundChecks != null)
            foreach (GroundCheck wheel in rocketGroundChecks) grounded |= wheel.CheckNow();
        if (!grounded)
            for (float offset = -60f; offset < 0f; offset += 1f)
                if (!course.HasRoadAtProgress(progress + offset * direction)) return rocketJumpSpeed;
        // Retain the braking envelope on approach and the limit throughout the actual gap.
        for (float offset = 0f; offset <= 90f; offset += 1f)
        {
            if (course.HasRoadAtProgress(progress + offset * direction)) continue;
            float brakingDistance = Mathf.Max(0f, offset - 18f);
            return Mathf.Sqrt(rocketJumpSpeed * rocketJumpSpeed + 2f * rocketJumpBraking * brakingDistance);
        }
        if (!IsRocketActive) protectRocketJump = false;
        return float.PositiveInfinity;
    }

    private Vector3 GetRocketDirection(Vector3 fallback)
    {
        RaceCourse course = Gmanager.Control != null ? Gmanager.Control.course : null;
        if (course == null) return fallback;

        Vector3 position = body.position;
        float progress = course.GetProgressDistance(position);
        float lookAhead = Mathf.Max(rocketMinLookAhead, rocketCurrentSpeed * rocketLookAheadSeconds);
        // スタート地点の向きによっては waypoint 順と逆向きに走るため、周回判定と同じ向きへ先読みします。
        float direction = Gmanager.Control.CourseProgressSign;
        if (!course.TryGetPointAtProgress(progress + lookAhead * direction, out Vector3 target)) return fallback;

        Vector3 toTarget = Vector3.ProjectOnPlane(target - position, Vector3.up);
        return toTarget.sqrMagnitude > 0.01f ? toTarget.normalized : fallback;
    }

    private void TickSpin(float dt)
    {
        if (spinTimeRemaining <= 0f) return;
        spinTimeRemaining = Mathf.Max(0f, spinTimeRemaining - dt);
        if (body == null || body.isKinematic) return;

        float ratio = spinTimeRemaining / Mathf.Max(0.1f, spinDuration);
        Vector3 angular = body.angularVelocity;
        angular.y = spinStartRate * ratio;
        body.angularVelocity = angular;
        if (spinTimeRemaining <= 0f) body.maxAngularVelocity = originalMaxAngularVelocity;
    }

    private void TickOilDrop(float dt)
    {
        if (oilDropTimeRemaining < 0f) return;
        oilDropTimeRemaining -= dt;
        if (oilDropTimeRemaining > 0f) return;
        oilDropTimeRemaining = -1f;

        Vector3 back = -Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        Vector3 origin = transform.position + back * oilDropDistance + Vector3.up * 3f;
        Vector3 position = transform.position + back * oilDropDistance;
        Vector3 normal = Vector3.up;
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 12f, Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
        float nearest = float.PositiveInfinity;
        foreach (RaycastHit hit in hits)
        {
            // 車体（自分・相手とも）には置かず、その下の路面を探します。
            if (hit.rigidbody != null || hit.distance >= nearest) continue;
            nearest = hit.distance;
            position = hit.point;
            normal = hit.normal;
        }

        OilSlick.Spawn(position, normal, back, this);
    }

    private void UpdateShieldVisual()
    {
        bool visible = IsShieldActive && isActiveAndEnabled;
        if (!visible)
        {
            if (shieldBubble != null) shieldBubble.SetActive(false);
            return;
        }

        if (shieldBubble == null)
        {
            shieldBubble = new GameObject("ItemShieldBubble");
            shieldBubble.transform.SetParent(transform, false);
            shieldMesh = ItemVisualFactory.CreateSphere(shieldRadius);
            shieldMaterial = ItemVisualFactory.CreateTransparentMaterial("ItemShieldMaterial", shieldColor);
            shieldBubble.AddComponent<MeshFilter>().sharedMesh = shieldMesh;
            MeshRenderer renderer = shieldBubble.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = shieldMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        shieldBubble.SetActive(true);
        shieldBubble.transform.localPosition = Vector3.up * 0.5f;
        float pulse = 1f + 0.05f * Mathf.Sin(Time.time * 9f);
        shieldBubble.transform.localScale = Vector3.one * pulse;
        if (shieldMaterial != null)
        {
            // 残り1.5秒からは点滅させて、切れるタイミングを伝えます。
            Color color = shieldColor;
            if (shieldTimeRemaining < 1.5f) color.a *= Mathf.PingPong(Time.time * 6f, 1f) > 0.5f ? 1f : 0.25f;
            shieldMaterial.color = color;
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        // Report only an observed wall impact during the effect, not an assumed human mistake.
        if (IsConfused && !confuseWallReported && confuseSource != null &&
            (collision.rigidbody == null || collision.rigidbody.isKinematic) &&
            (collision.rigidbody == null || collision.rigidbody.GetComponent<CarItemEffects>() == null) &&
            !TubeDriveSurface.Contains(collision.collider) &&
            collision.relativeVelocity.sqrMagnitude >= 9f)
        {
            for (int i = 0; i < collision.contactCount; i++)
                if (Mathf.Abs(Vector3.Dot(collision.GetContact(i).normal, Vector3.up)) < .2f)
                {
                    confuseWallReported = true;
                    ReportAttack(confuseSource, ItemImpactKind.ConfusedWallContact);
                    break;
                }
        }
        if (!IsShieldActive || collision.rigidbody == null || collision.rigidbody == body) return;
        CarItemEffects other = collision.rigidbody.GetComponent<CarItemEffects>();
        if (other == null || other == this) return;

        Vector3 hitDirection = other.transform.position - transform.position;
        if (other.ApplySpin(hitDirection, this) && consumeShieldOnHit)
        {
            shieldTimeRemaining = 0f;
        }
    }
}
