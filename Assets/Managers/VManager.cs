using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[RequireComponent(typeof(Volume))]
public class VManager : MonoBehaviour
{
    public const string PlayerOneVolumeLayer = "BoostVolumeP1";
    public const string PlayerTwoVolumeLayer = "BoostVolumeP2";

    [Header("Drift Boost Post Processing")]
    [SerializeField] private bool driftBoostEffectsEnabled = true;
    [Tooltip("最大演出強度に達するまでの時間（秒）。")]
    [SerializeField, Min(0f)] private float boostFadeInSeconds = 0.08f;
    [Tooltip("加速終了後に通常の画面へ戻る時間（秒）。")]
    [SerializeField, Min(0f)] private float boostFadeOutSeconds = 0.25f;
    [Tooltip("満チャージ時に通常設定へ加算する各エフェクトの強さ。")]
    [SerializeField, Min(0f)] private float boostBloom = 0.5f;
    [SerializeField, Range(0f, 1f)] private float boostMotionBlur = 0.4f;
    [SerializeField, Range(-1f, 1f)] private float boostLensDistortion = -0.12f;
    [SerializeField, Range(0f, 1f)] private float boostChromaticAberration = 0.15f;
    [SerializeField, Range(0f, 1f)] private float boostVignette = 0.12f;
    [SerializeField, Range(-2f, 2f)] private float boostPostExposure = 0.15f;
    [SerializeField, Range(-100f, 100f)] private float boostContrast = 10f;

    private sealed class PlayerEffect
    {
        public Volume volume;
        public VolumeProfile profile;
        public UniversalAdditionalCameraData cameraData;
        public LayerMask originalLayerMask;
        public bool originalPostProcessing;
        public float targetWeight;
        public Bloom bloom;
        public MotionBlur motionBlur;
        public LensDistortion lensDistortion;
        public ChromaticAberration chromaticAberration;
        public Vignette vignette;
        public ColorAdjustments colorAdjustments;
    }

    private readonly PlayerEffect[] playerEffects = new PlayerEffect[2];
    private Volume volume;
    private VolumeProfile runtimeProfile;
    private Bloom bloom;
    private MotionBlur motionBlur;
    private LensDistortion lensDistortion;
    private Vignette vignette;
    private ColorAdjustments colorAdjustments;
    private ChromaticAberration chromaticAberration;

    private void Awake()
    {
        Init();
    }

    public void Init()
    {
        if (runtimeProfile != null) return;
        volume = GetComponent<Volume>();
        if (volume == null)
        {
            Debug.LogError("VManager: Volume component not found on the GameObject.");
            return;
        }

        runtimeProfile = volume.profile;
        if (runtimeProfile == null)
        {
            Debug.LogError("VManager: Volume profile is not assigned.");
            return;
        }

        bloom = GetOrAddOverride<Bloom>();
        motionBlur = GetOrAddOverride<MotionBlur>();
        lensDistortion = GetOrAddOverride<LensDistortion>();
        vignette = GetOrAddOverride<Vignette>();
        colorAdjustments = GetOrAddOverride<ColorAdjustments>();
        chromaticAberration = GetOrAddOverride<ChromaticAberration>();
    }

    /// <summary>各メインカメラに専用の加速Volumeを割り当てます。UIカメラには適用しません。</summary>
    public void ConfigurePlayerCameras(Camera[] cameras)
    {
        DisposePlayerEffects();
        if (!EnsureInitialized() || cameras == null) return;
        int layerOne = LayerMask.NameToLayer(PlayerOneVolumeLayer);
        int layerTwo = LayerMask.NameToLayer(PlayerTwoVolumeLayer);
        if (layerOne < 0 || layerTwo < 0)
        {
            Debug.LogError("VManager: BoostVolumeP1 / BoostVolumeP2 layers are required.", this);
            return;
        }

        int effectMask = (1 << layerOne) | (1 << layerTwo);
        for (int index = 0; index < playerEffects.Length && index < cameras.Length; index++)
        {
            if (cameras[index] == null) continue;
            int layer = index == 0 ? layerOne : layerTwo;
            var cameraData = cameras[index].GetUniversalAdditionalCameraData();
            var effect = new PlayerEffect
            {
                cameraData = cameraData,
                originalLayerMask = cameraData.volumeLayerMask,
                originalPostProcessing = cameraData.renderPostProcessing,
                profile = ScriptableObject.CreateInstance<VolumeProfile>()
            };
            effect.profile.name = $"DriftBoostProfile_P{index + 1}";
            effect.profile.hideFlags = HideFlags.DontSave;
            GameObject effectObject = new GameObject($"DriftBoostVolume_P{index + 1}");
            effectObject.hideFlags = HideFlags.DontSave;
            effectObject.layer = layer;
            effectObject.transform.SetParent(transform, false);
            effect.volume = effectObject.AddComponent<Volume>();
            effect.volume.isGlobal = true;
            effect.volume.priority = volume.priority + 100f;
            effect.volume.weight = 0f;
            effect.volume.sharedProfile = effect.profile;
            effect.bloom = effect.profile.Add<Bloom>();
            effect.motionBlur = effect.profile.Add<MotionBlur>();
            effect.motionBlur.mode.Override(MotionBlurMode.CameraOnly);
            effect.lensDistortion = effect.profile.Add<LensDistortion>();
            effect.chromaticAberration = effect.profile.Add<ChromaticAberration>();
            effect.vignette = effect.profile.Add<Vignette>();
            effect.colorAdjustments = effect.profile.Add<ColorAdjustments>();
            cameraData.volumeLayerMask = (cameraData.volumeLayerMask.value & ~effectMask) | (1 << layer);
            cameraData.renderPostProcessing = true;
            playerEffects[index] = effect;
            ApplyBoostSettings(effect);
        }
    }

    public void SetDriftBoost(int playerIndex, float intensity)
    {
        if (playerIndex < 0 || playerIndex >= playerEffects.Length) return;
        PlayerEffect effect = playerEffects[playerIndex];
        if (effect != null) effect.targetWeight = Mathf.Clamp01(intensity);
    }

    public float GetDriftBoostWeight(int playerIndex) =>
        playerIndex >= 0 && playerIndex < playerEffects.Length && playerEffects[playerIndex] != null
            ? playerEffects[playerIndex].volume.weight : 0f;

    /// <summary>Gmanagerが車両状態を収集した後、描画前に一度更新します。</summary>
    public void TickDriftBoost(float deltaTime)
    {
        if (!isActiveAndEnabled || !driftBoostEffectsEnabled)
        {
            ResetDriftBoosts();
            return;
        }

        foreach (PlayerEffect effect in playerEffects)
        {
            if (effect == null) continue;
            float fadeSeconds = effect.targetWeight > effect.volume.weight ? boostFadeInSeconds : boostFadeOutSeconds;
            effect.volume.weight = fadeSeconds <= 0f ? effect.targetWeight : Mathf.MoveTowards(
                effect.volume.weight, effect.targetWeight, Mathf.Max(0f, deltaTime) / fadeSeconds);
            ApplyBoostSettings(effect);
        }
    }

    public void ResetDriftBoosts()
    {
        foreach (PlayerEffect effect in playerEffects)
        {
            if (effect == null) continue;
            effect.targetWeight = 0f;
            effect.volume.weight = 0f;
        }
    }

    private void ApplyBoostSettings(PlayerEffect effect)
    {
        // 通常設定は変更せず、専用Volumeのweightで演出のみをブレンドします。
        effect.bloom.intensity.Override(BaseValue(bloom, bloom.intensity) + boostBloom);
        effect.motionBlur.intensity.Override(BaseValue(motionBlur, motionBlur.intensity) + boostMotionBlur);
        effect.lensDistortion.intensity.Override(BaseValue(lensDistortion, lensDistortion.intensity) + boostLensDistortion);
        effect.chromaticAberration.intensity.Override(BaseValue(chromaticAberration, chromaticAberration.intensity) + boostChromaticAberration);
        effect.vignette.intensity.Override(BaseValue(vignette, vignette.intensity) + boostVignette);
        effect.colorAdjustments.postExposure.Override(BaseValue(colorAdjustments, colorAdjustments.postExposure) + boostPostExposure);
        effect.colorAdjustments.contrast.Override(BaseValue(colorAdjustments, colorAdjustments.contrast) + boostContrast);
    }

    private static float BaseValue(VolumeComponent component, VolumeParameter<float> parameter) =>
        component.active && parameter.overrideState ? parameter.value : 0f;

    private void OnDisable() => ResetDriftBoosts();

    private void OnDestroy()
    {
        DisposePlayerEffects();
        if (volume != null) volume.profile = null;
        DestroyProfile(runtimeProfile);
        runtimeProfile = null;
    }

    private void DisposePlayerEffects()
    {
        for (int index = 0; index < playerEffects.Length; index++)
        {
            PlayerEffect effect = playerEffects[index];
            if (effect == null) continue;
            if (effect.cameraData != null)
            {
                effect.cameraData.volumeLayerMask = effect.originalLayerMask;
                effect.cameraData.renderPostProcessing = effect.originalPostProcessing;
            }
            if (effect.volume != null)
            {
                effect.volume.weight = 0f;
                effect.volume.sharedProfile = null;
                CoreUtils.Destroy(effect.volume.gameObject);
            }
            DestroyProfile(effect.profile);
            playerEffects[index] = null;
        }
    }

    private static void DestroyProfile(VolumeProfile profile)
    {
        if (profile == null) return;
        foreach (VolumeComponent component in profile.components) CoreUtils.Destroy(component);
        CoreUtils.Destroy(profile);
    }

    public void SetBloom(float intensity)
    {
        if (!EnsureInitialized())
        {
            return;
        }

        bloom.active = true;
        SetOverride(bloom.intensity, intensity);
    }

    public void SetMotionBlur(float intensity)
    {
        if (!EnsureInitialized())
        {
            return;
        }

        motionBlur.active = true;
        SetOverride(motionBlur.intensity, intensity);
    }

    public void SetLensDistortion(float intensity)
    {
        if (!EnsureInitialized())
        {
            return;
        }

        lensDistortion.active = true;
        SetOverride(lensDistortion.intensity, intensity);
    }

    public void SetVignette(float intensity)
    {
        if (!EnsureInitialized())
        {
            return;
        }

        vignette.active = true;
        SetOverride(vignette.intensity, intensity);
    }

    public void SetColorAdjustments(float postExposure, float contrast, float hueShift, float saturation, Color colorFilter)
    {
        if (!EnsureInitialized())
        {
            return;
        }

        colorAdjustments.active = true;
        SetOverride(colorAdjustments.postExposure, postExposure);
        SetOverride(colorAdjustments.contrast, contrast);
        SetOverride(colorAdjustments.hueShift, hueShift);
        SetOverride(colorAdjustments.saturation, saturation);
        SetOverride(colorAdjustments.colorFilter, colorFilter);
    }

    public void SetBloomActive(bool isActive)
    {
        if (!EnsureInitialized())
        {
            return;
        }

        bloom.active = isActive;
    }

    public void SetMotionBlurActive(bool isActive)
    {
        if (!EnsureInitialized())
        {
            return;
        }

        motionBlur.active = isActive;
    }

    public void SetLensDistortionActive(bool isActive)
    {
        if (!EnsureInitialized())
        {
            return;
        }

        lensDistortion.active = isActive;
    }

    public void SetVignetteActive(bool isActive)
    {
        if (!EnsureInitialized())
        {
            return;
        }

        vignette.active = isActive;
    }

    public void SetColorAdjustmentsActive(bool isActive)
    {
        if (!EnsureInitialized())
        {
            return;
        }

        colorAdjustments.active = isActive;
    }

    private bool EnsureInitialized()
    {
        if (runtimeProfile != null)
        {
            return true;
        }

        Init();
        return runtimeProfile != null;
    }

    private T GetOrAddOverride<T>() where T : VolumeComponent
    {
        if (!runtimeProfile.TryGet(out T volumeComponent))
        {
            volumeComponent = runtimeProfile.Add<T>();
        }

        return volumeComponent;
    }

    private static void SetOverride(ClampedFloatParameter parameter, float value)
    {
        parameter.overrideState = true;
        parameter.value = value;
    }

    private static void SetOverride(FloatParameter parameter, float value)
    {
        parameter.overrideState = true;
        parameter.value = value;
    }

    private static void SetOverride(ColorParameter parameter, Color value)
    {
        parameter.overrideState = true;
        parameter.value = value;
    }
}
