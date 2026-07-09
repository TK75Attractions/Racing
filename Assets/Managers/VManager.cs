using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

public class VManager : MonoBehaviour
{
    private Volume volume;
    private VolumeProfile runtimeProfile;
    private Bloom bloom;
    private MotionBlur motionBlur;
    private LensDistortion lensDistortion;
    private Vignette vignette;
    private ColorAdjustments colorAdjustments;

    private void Awake()
    {
        Init();
    }

    public void Init()
    {
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
            volumeComponent = runtimeProfile.Add<T>(true);
        }

        volumeComponent.active = true;
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
