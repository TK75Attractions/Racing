using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>Keep the two player views within a 60 fps rendering budget.</summary>
public sealed class RaceRenderBudget : MonoBehaviour
{
    private const float MinimumScale = .5f;
    private UniversalRenderPipelineAsset pipeline;
    private Gmanager manager;
    private float originalScale;
    private float averageMilliseconds;
    private float nextAdjustment;
    private float fastSeconds;

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
        pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (pipeline == null) { enabled = false; return; }
        originalScale = pipeline.renderScale;
    }

    private void OnEnable()
    {
        if (pipeline == null) return;
        manager = FindFirstObjectByType<Gmanager>();
        averageMilliseconds = 0f;
        fastSeconds = 0f;
        nextAdjustment = Time.unscaledTime + 3f;
        // Start with headroom for the expensive tree-lined starting grid.
        pipeline.renderScale = Mathf.Min(.8f, MaximumScale());
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
        // URP keeps the native output dimensions and upscales the completed stack.
        return Mathf.Clamp(Mathf.Min(originalScale, 1920f / Mathf.Max(1, width),
            1080f / Mathf.Max(1, height)), Minimum, 1f);
    }

    private void Update()
    {
        if (pipeline == null) return;
        if (manager == null) manager = FindFirstObjectByType<Gmanager>();
        if (Time.unscaledTime < nextAdjustment) return;
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
    }
}
