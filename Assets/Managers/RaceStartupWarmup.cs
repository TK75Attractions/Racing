using Unity.Profiling;
using UnityEngine;

/// <summary>Run bounded preparation before the title, outside the countdown and race.</summary>
public static class RaceStartupWarmup
{
    private static readonly ProfilerMarker Marker = new ProfilerMarker("Racing.Startup.Warmup");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Prepare()
    {
        using (Marker.Auto())
        {
            // A referenced asset also keeps runtime-only shader variants in the build.
            // This is a D3D11 baseline; DX12 needs captured graphics states as well.
            var variants = Resources.Load<ShaderVariantCollection>("RaceStartupVariants");
            // The saved collection includes D3D GBuffer variants that Metal rejects.
            // Keep it referenced for stripping, but let other backends compile their used passes.
            var backend = SystemInfo.graphicsDeviceType;
            bool d3d = backend == UnityEngine.Rendering.GraphicsDeviceType.Direct3D11 ||
                backend == UnityEngine.Rendering.GraphicsDeviceType.Direct3D12;
            if (variants != null && d3d) variants.WarmUp();
            else if (variants == null) Debug.LogWarning("Race startup shader collection is missing.");
            CarNightGlow.PrepareSharedResources();
            if (Debug.isDebugBuild && variants != null)
                Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null,
                    "RACE_STARTUP_WARMUP: shaders={0}, variants={1}, warmed={2}, realtime={3:F3}s",
                    variants.shaderCount, variants.variantCount, variants.isWarmedUp, Time.realtimeSinceStartupAsDouble);
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void PrepareScene()
    {
        if (Gmanager.Control == null) return;
        using (Marker.Auto())
        {
            TireMarkRenderer.GetOrCreate();
            // The public API initializes both procedural clips even at zero volume,
            // and does not play or schedule either source in that case.
            LightningEffects.PlayThunder(0f, 0f, 0f);
        }
    }
}
