using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>Adjust the shared world render scale. NativeResolutionUI scopes UI rendering to 1x.</summary>
public sealed class RaceRenderBudget : MonoBehaviour
{
    private const float MinimumScale = .5f;
    private const string PresetKey = "Racing.QualityPreset";
    private const string ManualKey = "Racing.QualityPresetManual";
    public enum QualityPreset { Low, Medium, High }
    public static RaceRenderBudget Instance { get; private set; }
    public QualityPreset CurrentPreset { get; private set; }
    private UniversalRenderPipelineAsset pipeline;
    private Gmanager manager;
    private float originalScale;
    private float averageMilliseconds;
    private float nextAdjustment;
    private float fastSeconds;

    public float AverageFrameMilliseconds => averageMilliseconds;
    public float CurrentScale => pipeline != null ? pipeline.renderScale : 1f;
    public bool IsAutomatic => automatic;
    private bool automatic = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (Application.isEditor || FindFirstObjectByType<Gmanager>() == null) return;
        var owner = new GameObject("Race render budget");
        DontDestroyOnLoad(owner);
        owner.AddComponent<RaceRenderBudget>();
    }

    private void Awake()
    {
        Instance = this;
        pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (pipeline == null) { enabled = false; return; }
        originalScale = pipeline.renderScale;
        CurrentPreset = (QualityPreset)Mathf.Clamp(PlayerPrefs.GetInt(PresetKey, (int)QualityPreset.Medium), 0, 2);
        automatic = PlayerPrefs.GetInt(ManualKey, 0) == 0;
    }

    private void OnEnable()
    {
        if (pipeline == null) return;
        manager = FindFirstObjectByType<Gmanager>();
        averageMilliseconds = 0f;
        fastSeconds = 0f;
        nextAdjustment = Time.unscaledTime + 3f;
        // Start with headroom for the expensive tree-lined starting grid.
        if (automatic) pipeline.renderScale = Mathf.Min(.8f, MaximumScale());
        else ApplyPresetScale();
    }

    public void SelectPreset(QualityPreset preset)
    {
        CurrentPreset = preset;
        PlayerPrefs.SetInt(PresetKey, (int)preset);
        PlayerPrefs.SetInt(ManualKey, 1);
        PlayerPrefs.Save();
        automatic = false;
        ApplyPresetScale();
    }

    public void EnableAutomaticScale()
    {
        if (pipeline == null) return;
        automatic = true;
        PlayerPrefs.SetInt(ManualKey, 0);
        PlayerPrefs.Save();
        pipeline.renderScale = Mathf.Min(.8f, MaximumScale());
        nextAdjustment = Time.unscaledTime + 1f;
    }

    private float PresetScale => CurrentPreset == QualityPreset.Low ? .55f :
        CurrentPreset == QualityPreset.High ? .85f : .70f;

    private void ApplyPresetScale()
    {
        if (pipeline != null) pipeline.renderScale = Mathf.Min(PresetScale, MaximumScale());
    }

    private float Minimum => Mathf.Min(MinimumScale, originalScale);

    private float MaximumScale()
    {
        int width = 0, height = 0;
        if (manager != null)
        {
            for (int player = 0; player < 2; player++)
            {
                Camera camera = manager.GetPlayerCaptureCamera(player);
                if (camera == null) continue;
                width = Mathf.Max(width, camera.pixelWidth);
                height = Mathf.Max(height, camera.pixelHeight);
            }
        }
        if (width == 0 || height == 0) { width = Screen.width; height = Screen.height; }
        // Avoid accidentally rendering two Retina/4K views at desktop resolution.
        // URP upscales the world; NativeResolutionUI composites the HUD afterwards at 1x.
        return Mathf.Clamp(Mathf.Min(originalScale, 1920f / Mathf.Max(1, width),
            1080f / Mathf.Max(1, height)), Minimum, 1f);
    }

    private void Update()
    {
        if (pipeline == null) return;
        if (manager == null) manager = FindFirstObjectByType<Gmanager>();
        if (!automatic || Time.unscaledTime < nextAdjustment) return;
        float maximum = MaximumScale();
        if (pipeline.renderScale > maximum)
        {
            pipeline.renderScale = maximum;
            nextAdjustment = Time.unscaledTime + 1f;
            return;
        }
        if (!Application.isFocused || manager == null || manager.state != Gmanager.State.Game) return;
        float milliseconds = Time.unscaledDeltaTime * 1000f;
        // Ignore scene loads, app switching and debugger pauses.
        if (milliseconds <= 0f || milliseconds > 100f) return;
        float weight = 1f - Mathf.Exp(-Time.unscaledDeltaTime / .75f);
        averageMilliseconds = averageMilliseconds == 0f ? milliseconds :
            Mathf.Lerp(averageMilliseconds, milliseconds, weight);
        float limit = 15.5f;
        if (QualitySettings.vSyncCount > 0)
        {
            double refresh = Screen.currentResolution.refreshRateRatio.value;
            if (refresh > 0) limit = Mathf.Max(limit, (float)(1000 * QualitySettings.vSyncCount / refresh) + 1f);
        }
        if (Application.targetFrameRate > 0) limit = Mathf.Max(limit, 1000f / Application.targetFrameRate + 1f);
        if (averageMilliseconds > limit && pipeline.renderScale > Minimum + .01f)
        {
            pipeline.renderScale = Mathf.Max(Minimum, pipeline.renderScale - .05f);
            nextAdjustment = Time.unscaledTime + 1f;
            averageMilliseconds = 0f;
            fastSeconds = 0f;
        }
        else if (averageMilliseconds < 12.5f && pipeline.renderScale < maximum - .01f)
        {
            fastSeconds += Time.unscaledDeltaTime;
            if (fastSeconds < 5f) return;
            pipeline.renderScale = Mathf.Min(maximum, pipeline.renderScale + .05f);
            nextAdjustment = Time.unscaledTime + 1f;
            averageMilliseconds = 0f;
            fastSeconds = 0f;
        }
        else fastSeconds = 0f;
    }

    private void OnDestroy()
    {
        if (pipeline != null) pipeline.renderScale = originalScale;
        if (Instance == this) Instance = null;
    }
}
