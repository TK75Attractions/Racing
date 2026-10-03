using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// タイヤの滑りを、路面に残るタイヤ痕と白煙で示します。
/// ドリフト中の後輪、急ブレーキ、大きな横滑りで発生し、車体の物理挙動は変更しません。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(DebugMover))]
public sealed class TireEffectsVisual : MonoBehaviour
{
    [Header("滑りの判定")]
    [Tooltip("この速度（m/s）未満では痕も煙も出しません。")]
    [SerializeField, Min(0f)] private float minimumSpeed = 3f;
    [Tooltip("タイヤの横滑りの速さ（m/s）。この値から滑り始めとみなします。")]
    [SerializeField, Min(0f)] private float lateralSlipStart = 3f;
    [Tooltip("タイヤの横滑りの速さ（m/s）。この値で滑りが最大になります。")]
    [SerializeField, Min(0f)] private float lateralSlipFull = 9f;
    [Tooltip("ドリフト中の後輪に保証する最低の滑りの強さ。")]
    [SerializeField, Range(0f, 1f)] private float driftRearSlip = 0.65f;
    [Tooltip("ブレーキと判定するペダル入力（負方向）のしきい値。")]
    [SerializeField, Range(0.01f, 1f)] private float brakePedalThreshold = 0.5f;
    [Tooltip("前進の速さ（m/s）がこの値以上のときだけ、ブレーキで痕を残します。")]
    [SerializeField, Min(0f)] private float brakeMinimumSpeed = 8f;
    [Tooltip("ペダルを最後まで踏み込んだときのブレーキの滑りの強さ。")]
    [SerializeField, Range(0f, 1f)] private float brakeSlip = 0.8f;

    [Header("タイヤ痕")]
    [SerializeField] private bool tireMarksEnabled = true;
    [SerializeField, Min(0.01f)] private float markWidth = 0.28f;
    [Tooltip("痕を1区間伸ばす距離（m）。短いほど曲線が滑らかですが、区間を多く使います。")]
    [SerializeField, Min(0.05f)] private float markSegmentLength = 0.4f;
    [SerializeField, Min(0f)] private float markSurfaceOffset = 0.02f;
    [Tooltip("滑りの強さがこの値未満では痕を残しません。")]
    [SerializeField, Range(0f, 1f)] private float markThreshold = 0.15f;

    [Header("タイヤスモーク")]
    [SerializeField] private bool smokeEnabled = true;
    [Tooltip("タイヤ1本あたり、滑りが最大のときに1秒間に出す煙の数。")]
    [SerializeField, Min(0f)] private float smokePerSecondPerWheel = 26f;
    [Tooltip("滑りの強さがこの値未満では煙を出しません。")]
    [SerializeField, Range(0f, 1f)] private float smokeThreshold = 0.3f;
    [SerializeField] private Color smokeColor = new Color(0.86f, 0.86f, 0.88f, 1f);
    [SerializeField, Range(0f, 1f)] private float smokeOpacity = 0.38f;
    [SerializeField, Min(0.05f)] private float smokeStartSize = 0.7f;
    [Tooltip("消える直前の煙の大きさ（開始時の何倍か）。")]
    [SerializeField, Min(1f)] private float smokeGrowth = 4f;
    [SerializeField, Min(0.1f)] private float smokeLifetime = 1.3f;

    private const float TeleportDistance = 3f;

    private sealed class WheelState
    {
        public TireForce tire;
        public bool hasMark;
        public Vector3 lastPoint;
        public Vector3 lastLeft;
        public Vector3 lastRight;
        public float lastAlpha;
        public float smokeAccumulator;
        public float slip;
    }

    private DebugMover mover;
    private Rigidbody body;
    private WheelState[] wheels = new WheelState[0];
    private ParticleSystem smoke;
    private Material smokeMaterial;

    private void Awake()
    {
        mover = GetComponent<DebugMover>();
        body = GetComponent<Rigidbody>();
        CacheWheels();
    }

    /// <summary>タイヤ1本の滑りの強さ（0〜1）を求めます。検証からも呼び出します。</summary>
    public float EvaluateSlip(bool grounded, float planarSpeed, float lateralSlipSpeed,
        bool isFrontWheel, bool drifting, float pedalInput, float forwardSpeed)
    {
        if (!grounded || planarSpeed < minimumSpeed) return 0f;

        float slip = Mathf.InverseLerp(lateralSlipStart, Mathf.Max(lateralSlipStart + 0.01f, lateralSlipFull),
            lateralSlipSpeed);
        if (drifting && !isFrontWheel) slip = Mathf.Max(slip, driftRearSlip);

        if (pedalInput < -brakePedalThreshold && forwardSpeed >= brakeMinimumSpeed)
        {
            float brake = Mathf.InverseLerp(brakePedalThreshold, 1f, -pedalInput);
            slip = Mathf.Max(slip, brakeSlip * Mathf.Lerp(0.5f, 1f, brake));
        }

        return Mathf.Clamp01(slip);
    }

    private void Update()
    {
        if (mover == null || Gmanager.Control == null || !Gmanager.Control.IsDrivingEnabled)
        {
            BreakMarks();
            return;
        }

        Vector3 velocity = body != null ? body.linearVelocity : Vector3.zero;
        float planarSpeed = Vector3.ProjectOnPlane(velocity, Vector3.up).magnitude;
        float forwardSpeed = Vector3.Dot(velocity, transform.forward);
        float deltaTime = Time.deltaTime;

        foreach (WheelState wheel in wheels)
        {
            if (wheel.tire == null) continue;
            wheel.slip = EvaluateSlip(wheel.tire.IsGrounded, planarSpeed, wheel.tire.LateralSlipSpeed,
                wheel.tire.IsFrontWheel, mover.IsDrifting, mover.PedalInput, forwardSpeed);
            UpdateMark(wheel, velocity);
            UpdateSmoke(wheel, velocity, deltaTime);
        }
    }

    private void UpdateMark(WheelState wheel, Vector3 velocity)
    {
        if (!tireMarksEnabled || wheel.slip < markThreshold || !wheel.tire.IsGrounded)
        {
            wheel.hasMark = false;
            return;
        }

        RaycastHit hit = wheel.tire.GroundHit;
        Vector3 normal = hit.normal.sqrMagnitude > 0.0001f ? hit.normal : Vector3.up;
        // 物理の接地点ではなく、見た目のタイヤ位置を路面へ落として描きます。
        Vector3 point = Vector3.ProjectOnPlane(wheel.tire.transform.position - hit.point, normal) + hit.point
            + normal * markSurfaceOffset;
        float alpha = Mathf.Lerp(0.35f, 1f, Mathf.InverseLerp(markThreshold, 1f, wheel.slip));

        if (wheel.hasMark && (point - wheel.lastPoint).sqrMagnitude > TeleportDistance * TeleportDistance)
        {
            // リスポーンなどで瞬間移動したときは、離れた点どうしを繋ぎません。
            wheel.hasMark = false;
        }

        if (!wheel.hasMark)
        {
            Vector3 direction = Vector3.ProjectOnPlane(velocity, normal);
            if (direction.sqrMagnitude < 0.0001f) direction = transform.forward;
            SetMarkEdge(wheel, point, direction, normal, alpha);
            wheel.hasMark = true;
            return;
        }

        Vector3 step = point - wheel.lastPoint;
        if (step.sqrMagnitude < markSegmentLength * markSegmentLength) return;

        Vector3 previousLeft = wheel.lastLeft;
        Vector3 previousRight = wheel.lastRight;
        float previousAlpha = wheel.lastAlpha;
        SetMarkEdge(wheel, point, step, normal, alpha);
        TireMarkRenderer.GetOrCreate().AddSegment(
            previousLeft, previousRight, previousAlpha,
            wheel.lastLeft, wheel.lastRight, wheel.lastAlpha);
    }

    private void SetMarkEdge(WheelState wheel, Vector3 point, Vector3 direction, Vector3 normal, float alpha)
    {
        Vector3 side = Vector3.Cross(normal, direction).normalized * (markWidth * 0.5f);
        wheel.lastPoint = point;
        wheel.lastLeft = point - side;
        wheel.lastRight = point + side;
        wheel.lastAlpha = alpha;
    }

    private void UpdateSmoke(WheelState wheel, Vector3 velocity, float deltaTime)
    {
        if (!smokeEnabled || wheel.slip < smokeThreshold)
        {
            wheel.smokeAccumulator = 0f;
            return;
        }

        float strength = Mathf.InverseLerp(smokeThreshold, 1f, wheel.slip);
        wheel.smokeAccumulator += smokePerSecondPerWheel * Mathf.Lerp(0.35f, 1f, strength) * Mathf.Max(0f, deltaTime);
        int count = Mathf.FloorToInt(wheel.smokeAccumulator);
        if (count <= 0) return;
        wheel.smokeAccumulator -= count;
        if (smoke == null && !CreateSmoke()) return;

        Vector3 origin = wheel.tire.transform.position + Vector3.up * 0.15f;
        Color color = smokeColor;
        color.a = smokeOpacity * Mathf.Lerp(0.5f, 1f, strength);
        for (int index = 0; index < count; index++)
        {
            Vector3 drift = Random.insideUnitSphere * 0.8f;
            drift.y = Mathf.Abs(drift.y) * 0.5f;
            ParticleSystem.EmitParams particle = new ParticleSystem.EmitParams
            {
                position = origin + Random.insideUnitSphere * 0.15f,
                velocity = velocity * 0.12f + Vector3.up * Random.Range(0.4f, 1.1f) + drift,
                startLifetime = smokeLifetime * Random.Range(0.8f, 1.2f),
                startSize = smokeStartSize * Random.Range(0.8f, 1.2f),
                rotation = Random.Range(0f, 360f),
                angularVelocity = Random.Range(-45f, 45f),
                startColor = color
            };
            smoke.Emit(particle, 1);
        }
    }

    private void BreakMarks()
    {
        foreach (WheelState wheel in wheels)
        {
            wheel.hasMark = false;
            wheel.smokeAccumulator = 0f;
            wheel.slip = 0f;
        }
    }

    private void CacheWheels()
    {
        TireForce[] tires = GetComponentsInChildren<TireForce>(true);
        wheels = new WheelState[tires.Length];
        for (int index = 0; index < tires.Length; index++)
        {
            wheels[index] = new WheelState { tire = tires[index] };
        }
    }

    private bool CreateSmoke()
    {
        // Resources参照により、ビルド時にも専用シェーダーが含まれます。
        Shader shader = Resources.Load<Shader>("TireSmoke");
        if (shader == null)
        {
            Debug.LogError("TireEffectsVisual requires the TireSmoke shader.", this);
            smokeEnabled = false;
            return false;
        }

        GameObject effect = new GameObject("Tire Smoke");
        effect.layer = gameObject.layer;
        effect.transform.SetParent(transform, false);
        smoke = effect.AddComponent<ParticleSystem>();
        smoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = smoke.main;
        main.playOnAwake = false;
        main.loop = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Shape;
        main.maxParticles = 600;
        main.gravityModifier = -0.02f;
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
        var emission = smoke.emission;
        emission.enabled = false;
        var shape = smoke.shape;
        shape.enabled = false;

        var sizeOverLifetime = smoke.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        float growth = Mathf.Max(1f, smokeGrowth);
        // 出た直前は小さく、すぐに膨らんでからゆっくり広がる形にします。
        AnimationCurve sizeCurve = new AnimationCurve(
            new Keyframe(0f, 1f / growth, 0f, 2.2f),
            new Keyframe(1f, 1f, 0.3f, 0f));
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(growth, sizeCurve);

        var colorOverLifetime = smoke.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, 0.1f),
                new GradientAlphaKey(0.55f, 0.5f),
                new GradientAlphaKey(0f, 1f)
            });
        colorOverLifetime.color = gradient;

        var limitVelocity = smoke.limitVelocityOverLifetime;
        limitVelocity.enabled = true;
        limitVelocity.limit = 100f;
        limitVelocity.drag = 1.6f;
        limitVelocity.multiplyDragByParticleSize = false;
        limitVelocity.multiplyDragByParticleVelocity = false;

        var noise = smoke.noise;
        noise.enabled = true;
        noise.strength = 0.5f;
        noise.frequency = 0.45f;
        noise.scrollSpeed = 0.3f;
        noise.quality = ParticleSystemNoiseQuality.Low;

        smokeMaterial = new Material(shader);
        var renderer = smoke.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = smokeMaterial;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sortMode = ParticleSystemSortMode.Distance;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.maxParticleSize = 1f;
        smoke.Play();
        return true;
    }

    private void OnDisable()
    {
        BreakMarks();
        if (smoke != null) smoke.Clear();
    }

    private void OnDestroy()
    {
        if (smokeMaterial != null) Destroy(smokeMaterial);
    }
}
