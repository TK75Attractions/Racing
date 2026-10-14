using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>Apply ambient occlusion to world geometry, once per player view.</summary>
public sealed class RaceAmbientOcclusion : ScreenSpaceAmbientOcclusion
{
    // Layers 3 and 5 contain the backdrop and UI. Empty clear/foreground cameras
    // also share this renderer, but none of these views need depth-based AO.
    private const int WorldLayers = ~((1 << 3) | (1 << 5));

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if ((renderingData.cameraData.camera.cullingMask & WorldLayers) == 0) return;
        base.AddRenderPasses(renderer, ref renderingData);
    }
}
