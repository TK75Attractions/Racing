using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// 車両の水平速度とドリフトブーストを、カメラと画面演出へ変換します。
/// 車両の物理挙動は変更しません。
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(1000)]
public sealed class RaceSpeedVisualController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Rigidbody targetRigidbody;
    [SerializeField] private DebugMover targetMover;
    [SerializeField] private Camera targetCamera;
    [SerializeField] private CinemachineCamera raceCamera;
    [SerializeField] private Transform visualEffectPivot;
    [SerializeField] private VManager volumeManager;
    [SerializeField] private ParticleSystem speedLines;
    [SerializeField] private int playerIndex;

    [Header("Speed")]
    [SerializeField, Min(0.01f)] private float maxSpeedMps = 55.56f;
    [SerializeField] private AnimationCurve speedCurve = new AnimationCurve(
        new Keyframe(0f, 0f),
        new Keyframe(0.4f, 0.15f),
        new Keyframe(0.7f, 0.5f),
        new Keyframe(1f, 1f));
    [SerializeField, Min(0f)] private float speedResponse = 4f;

    [Header("FOV")]
    [Tooltip("ONにすると、現在のCinemachineカメラのFOVを通常時FOVとして使用します。")]
    [SerializeField] private bool useCameraFovAsNormal;
    [SerializeField, Range(1f, 179f)] private float normalFov = 60f;
    [SerializeField, Range(1f, 179f)] private float highSpeedFov = 70f;
    [SerializeField, Min(0f)] private float boostFovAdd = 4f;
    [SerializeField, Min(0f)] private float boostFovKick = 4f;
    [SerializeField, Range(1f, 179f)] private float maximumFov = 82f;
    [SerializeField, Min(0f)] private float fovResponse = 7f;

    [Header("Camera Pullback")]
    [SerializeField, Min(0f)] private float highSpeedPullback = 0.25f;
    [SerializeField, Min(0f)] private float boostPullback = 0.12f;

    [Header("Camera Shake")]
    [SerializeField, Min(0f)] private float highSpeedShakePosition = 0.015f;
    [SerializeField, Min(0f)] private float boostShakePosition = 0.012f;
    [SerializeField, Min(0f)] private float highSpeedShakeRotation = 0.12f;
    [SerializeField, Min(0f)] private float minShakeFrequency = 4f;
    [SerializeField, Min(0f)] private float maxShakeFrequency = 11f;

    [Header("Speed Lines")]
    [SerializeField, Range(0f, 1f)] private float speedLinesStart = 0.45f;
    [SerializeField, Min(0f)] private float speedLinesMaxEmission = 120f;
    [SerializeField, Min(0f)] private float speedLinesBoostEmission = 80f;
    [SerializeField, Min(0f)] private float speedLinesMinSpeed = 25f;
    [SerializeField, Min(0f)] private float speedLinesMaxSpeed = 55f;
    [SerializeField, Min(0.01f)] private float speedLinesLifetime = 0.40f;
    [SerializeField, Min(0.001f)] private float speedLinesSize = 0.03f;
    [SerializeField, Min(0f)] private float speedLinesShapeWidth = 15f;
    [SerializeField, Min(0f)] private float speedLinesShapeHeight = 8f;
    [SerializeField, Min(0f)] private float speedLinesShapeDepth = 1f;
    [SerializeField, Min(0f)] private float speedLinesVelocityScale = 0.20f;
    [SerializeField, Min(0f)] private float speedLinesLengthScale = 4f;
    [SerializeField, Range(0f, 1f)] private float speedLinesAlpha = 0.24f;

    [Header("Response")]
    [SerializeField, Min(0f)] private float boostFadeInSpeed = 8f;
    [SerializeField, Min(0f)] private float boostFadeOutSpeed = 5f;
    [SerializeField, Min(0f)] private float boostKickDecay = 3.5f;

    private Vector3 basePivotPosition;
    private Quaternion basePivotRotation;
    private float baseCameraFov;
    private float visualSpeed01;
    private float boostBlend;
    private float boostKick01;
    private float externalBoostIntensity;
    private bool wasBoosting;
    private bool gameplayActive;
    private bool configured;
    private bool ownsSpeedLines;

    public float CurrentVisualSpeed01 => visualSpeed01;
    public float CurrentBoostIntensity => boostBlend;

    public void Configure(
        int index,
        Rigidbody rigidbody,
        DebugMover mover,
        Camera camera,
        CinemachineCamera cinemachineCamera,
        Transform pivot,
        VManager manager)
    {
        playerIndex = index;
        targetRigidbody = rigidbody;
        targetMover = mover;
        targetCamera = camera;
        raceCamera = cinemachineCamera;
        visualEffectPivot = pivot;
        volumeManager = manager;

        if (speedCurve == null)
        {
            speedCurve = new AnimationCurve(
                new Keyframe(0f, 0f),
                new Keyframe(0.4f, 0.15f),
                new Keyframe(0.7f, 0.5f),
                new Keyframe(1f, 1f));
        }

        if (raceCamera != null)
        {
            baseCameraFov = raceCamera.Lens.FieldOfView;
            if (useCameraFovAsNormal && baseCameraFov > 1f) normalFov = baseCameraFov;
        }
        else if (targetCamera != null)
        {
            baseCameraFov = targetCamera.fieldOfView;
            if (useCameraFovAsNormal && baseCameraFov > 1f) normalFov = baseCameraFov;
        }

        if (visualEffectPivot != null)
        {
            basePivotPosition = visualEffectPivot.localPosition;
            basePivotRotation = visualEffectPivot.localRotation;
        }

        EnsureSpeedLines();
        configured = true;
        ResetVisuals();
    }

    public void SetBoost(float intensity)
    {
        externalBoostIntensity = Mathf.Clamp01(intensity);
    }

    public void SetGameplayActive(bool active)
    {
        gameplayActive = active;
        if (!active) ResetVisuals();
    }

    private void Update()
    {
        if (!configured) return;

        bool active = gameplayActive;
        if (Gmanager.Control != null)
        {
            active &= Gmanager.Control.IsDrivingEnabled;
        }

        if (!active)
        {
            ResetVisuals();
            return;
        }

        UpdateSpeed(Time.deltaTime);
        UpdateBoost(Time.deltaTime);
        volumeManager?.SetRaceSpeedVisual(playerIndex, visualSpeed01);
    }

    private void LateUpdate()
    {
        if (!configured) return;

        bool active = gameplayActive &&
            (Gmanager.Control == null || Gmanager.Control.IsDrivingEnabled);
        if (!active)
        {
            ApplyCamera(0f, 0f, 0f);
            UpdateSpeedLines(0f, 0f);
            return;
        }

        ApplyCamera(visualSpeed01, boostBlend, boostKick01);
        UpdateSpeedLines(visualSpeed01, boostBlend);
    }

    private void UpdateSpeed(float deltaTime)
    {
        float speedMps = 0f;
        if (targetRigidbody != null)
        {
#if UNITY_6000_0_OR_NEWER
            speedMps = Vector3.ProjectOnPlane(targetRigidbody.linearVelocity, Vector3.up).magnitude;
#else
            speedMps = Vector3.ProjectOnPlane(targetRigidbody.velocity, Vector3.up).magnitude;
#endif
        }

        float rawSpeed01 = Mathf.Clamp01(speedMps / Mathf.Max(0.01f, maxSpeedMps));
        float targetSpeed = Mathf.Clamp01(speedCurve.Evaluate(rawSpeed01));
        visualSpeed01 = MoveTowardsExp(visualSpeed01, targetSpeed, speedResponse, deltaTime);
    }

    private void UpdateBoost(float deltaTime)
    {
        float targetBoost = targetMover != null
            ? targetMover.DriftBoostVisualIntensity
            : externalBoostIntensity;
        targetBoost = Mathf.Max(targetBoost, externalBoostIntensity);

        bool isBoosting = targetBoost > 0.001f;
        if (isBoosting && !wasBoosting) boostKick01 = 1f;
        wasBoosting = isBoosting;

        float response = targetBoost > boostBlend ? boostFadeInSpeed : boostFadeOutSpeed;
        boostBlend = MoveTowardsExp(boostBlend, targetBoost, response, deltaTime);
        boostKick01 = Mathf.MoveTowards(boostKick01, 0f, boostKickDecay * deltaTime);
    }

    private void ApplyCamera(float speed01, float boost01, float kick01)
    {
        float targetFov = Mathf.Lerp(normalFov, highSpeedFov, speed01)
            + boostFovAdd * boost01
            + boostFovKick * kick01;
        targetFov = Mathf.Clamp(targetFov, 1f, maximumFov);

        if (raceCamera != null)
        {
            var lens = raceCamera.Lens;
            lens.FieldOfView = Mathf.Lerp(
                lens.FieldOfView,
                targetFov,
                1f - Mathf.Exp(-fovResponse * Time.deltaTime));
            raceCamera.Lens = lens;
        }
        else if (targetCamera != null)
        {
            targetCamera.fieldOfView = Mathf.Lerp(
                targetCamera.fieldOfView,
                targetFov,
                1f - Mathf.Exp(-fovResponse * Time.deltaTime));
        }

        if (visualEffectPivot == null) return;

        float pullback = highSpeedPullback * speed01 + boostPullback * boost01;
        float shakeStrength = highSpeedShakePosition * speed01 + boostShakePosition * boost01;
        float frequency = Mathf.Lerp(minShakeFrequency, maxShakeFrequency, speed01);
        float time = Time.time * frequency;
        float noiseX = Mathf.PerlinNoise(time, 0.13f) * 2f - 1f;
        float noiseY = Mathf.PerlinNoise(0.37f, time) * 2f - 1f;
        float noiseZ = Mathf.PerlinNoise(time, 0.73f) * 2f - 1f;

        visualEffectPivot.localPosition = basePivotPosition
            + Vector3.back * pullback
            + new Vector3(noiseX, noiseY, 0f) * shakeStrength;

        float rotationStrength = highSpeedShakeRotation * speed01 + boostShakePosition * boost01;
        visualEffectPivot.localRotation = basePivotRotation * Quaternion.Euler(
            noiseY * rotationStrength,
            noiseX * rotationStrength,
            noiseZ * rotationStrength);
    }

    private void UpdateSpeedLines(float speed01, float boost01)
    {
        if (speedLines == null) return;

        float lineStrength = Mathf.InverseLerp(speedLinesStart, 1f, speed01);
        var emission = speedLines.emission;
        emission.rateOverTimeMultiplier = speedLinesMaxEmission * lineStrength
            + speedLinesBoostEmission * boost01;

        var main = speedLines.main;
        main.startSpeedMultiplier = Mathf.Lerp(speedLinesMinSpeed, speedLinesMaxSpeed, lineStrength);

        if (emission.rateOverTimeMultiplier > 0.01f)
        {
            if (!speedLines.isPlaying) speedLines.Play();
        }
        else if (speedLines.isPlaying)
        {
            speedLines.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }

    private void EnsureSpeedLines()
    {
        if (speedLines != null || visualEffectPivot == null) return;

        Transform existing = visualEffectPivot.Find("SpeedLines");
        if (existing != null) speedLines = existing.GetComponent<ParticleSystem>();
        if (speedLines == null)
        {
            GameObject lineObject = new GameObject("SpeedLines");
            lineObject.transform.SetParent(visualEffectPivot, false);
            speedLines = lineObject.AddComponent<ParticleSystem>();
            ownsSpeedLines = true;
        }

        speedLines.transform.localPosition = new Vector3(0f, 0f, 15f);
        speedLines.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

        var main = speedLines.main;
        main.loop = true;
        main.playOnAwake = false;
        main.prewarm = true;
        main.startLifetime = speedLinesLifetime;
        main.startSpeed = 35f;
        main.startSize = speedLinesSize;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = 500;
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;

        var emission = speedLines.emission;
        emission.rateOverTime = 0f;

        var shape = speedLines.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(speedLinesShapeWidth, speedLinesShapeHeight, speedLinesShapeDepth);

        var renderer = speedLines.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.velocityScale = speedLinesVelocityScale;
        renderer.lengthScale = speedLinesLengthScale;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        if (renderer.sharedMaterial == null)
            renderer.sharedMaterial = CreateSpeedLinesMaterial(speedLinesAlpha);
        else
            ApplySpeedLinesMaterialAlpha(renderer.sharedMaterial, speedLinesAlpha);
    }

    private static Material CreateSpeedLinesMaterial(float alpha)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
            ?? Shader.Find("Particles/Standard Unlit");
        if (shader == null) return null;

        Material material = new Material(shader) { name = "M_SpeedLines_Runtime" };
        Color color = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));
        material.color = color;
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Surface", 1f);
        material.SetFloat("_ZWrite", 0f);
        material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.renderQueue = 3000;
        return material;
    }

    private static void ApplySpeedLinesMaterialAlpha(Material material, float alpha)
    {
        if (material == null) return;
        Color color = material.color;
        color.a = Mathf.Clamp01(alpha);
        material.color = color;
        material.SetColor("_BaseColor", color);
    }

    private void ResetVisuals()
    {
        visualSpeed01 = 0f;
        boostBlend = 0f;
        boostKick01 = 0f;
        externalBoostIntensity = 0f;
        wasBoosting = false;
        volumeManager?.ClearRaceSpeedVisual(playerIndex);

        if (visualEffectPivot != null)
        {
            visualEffectPivot.localPosition = basePivotPosition;
            visualEffectPivot.localRotation = basePivotRotation;
        }

        if (raceCamera != null)
        {
            var lens = raceCamera.Lens;
            lens.FieldOfView = normalFov > 1f ? normalFov : baseCameraFov;
            raceCamera.Lens = lens;
        }
        else if (targetCamera != null && baseCameraFov > 1f)
        {
            targetCamera.fieldOfView = baseCameraFov;
        }

        if (speedLines != null)
            speedLines.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private static float MoveTowardsExp(float current, float target, float response, float deltaTime)
    {
        if (response <= 0f) return target;
        return Mathf.Lerp(current, target, 1f - Mathf.Exp(-response * Mathf.Max(0f, deltaTime)));
    }

    private void OnDestroy()
    {
        if (speedLines != null && ownsSpeedLines)
        {
            if (speedLines.TryGetComponent<ParticleSystemRenderer>(out var renderer) && renderer.sharedMaterial != null)
                Destroy(renderer.sharedMaterial);
            Destroy(speedLines.gameObject);
        }
    }
}
