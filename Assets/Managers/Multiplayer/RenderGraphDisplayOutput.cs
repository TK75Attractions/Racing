using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Profiling;

/// <summary>
/// Renders an additional display's camera stack into a correctly sized target.
/// URP uses Render Graph for the stack; only presentation writes the native buffer.
/// </summary>
[ExecuteAlways]
public sealed class RenderGraphDisplayOutput : MonoBehaviour
{
    private Camera[] cameras;
    private int displayIndex;
    private RenderTexture target;
    private Material presentation;
    private static readonly int TextureId = Shader.PropertyToID("_BlitTexture");
    private static readonly int ScaleBiasId = Shader.PropertyToID("_BlitScaleBias");
    private static readonly int EncodeId = Shader.PropertyToID("_EncodeSRGB");
    private static readonly ProfilerMarker PresentMarker = new ProfilerMarker("Racing.Display.Present");

    public RenderTexture Target => target;
    public int PresentedFrames { get; private set; }
    public bool CapturingPreview { get; set; }

    public void Configure(Camera[] outputCameras, int outputDisplay)
    {
        ReleaseTarget();
        cameras = outputCameras;
        displayIndex = outputDisplay;
        RefreshTarget();
    }

    private void OnEnable()
    {
        if (!Application.isPlaying) return;
        RenderPipelineManager.beginContextRendering += BeginRendering;
        RenderPipelineManager.endContextRendering += Present;
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginContextRendering -= BeginRendering;
        RenderPipelineManager.endContextRendering -= Present;
        ReleaseTarget();
        if (presentation != null) DestroyOwned(presentation);
        presentation = null;
    }

    private void LateUpdate() => RefreshTarget();
    private void BeginRendering(ScriptableRenderContext context, List<Camera> rendered)
    {
        if (!CapturingPreview) RefreshTarget();
    }

    private void RefreshTarget()
    {
        if (cameras == null || Application.isEditor || displayIndex <= 0 || displayIndex >= Display.displays.Length) return;
        Display display = Display.displays[displayIndex];
        ResizeTarget(display.renderingWidth, display.renderingHeight);
    }

    private void ResizeTarget(int width, int height)
    {
        if (width <= 0 || height <= 0) return;
        if (target != null && target.width == width && target.height == height && target.IsCreated()) return;
        ReleaseTarget();
        target = new RenderTexture(width, height, 24, RenderTextureFormat.Default)
        {
            name = $"Race Display {displayIndex + 1} Render Graph Output",
            antiAliasing = 1,
            useMipMap = false,
            filterMode = FilterMode.Bilinear
        };
        target.Create();
        foreach (Camera camera in cameras)
            if (camera != null) camera.targetTexture = target;
    }

    private void Present(ScriptableRenderContext context, List<Camera> rendered)
    {
        using var timing = PresentMarker.Auto();
        if (CapturingPreview || target == null || Application.isEditor || displayIndex >= Display.displays.Length) return;
        // Render requests for thumbnails/captures also raise this event. Only
        // present after this display's normal stack has been rendered.
        bool hasOutput = false;
        foreach (Camera camera in cameras)
            if (camera != null && camera.targetTexture == target && rendered.Contains(camera))
            { hasOutput = true; break; }
        if (!hasOutput) return;
        if (presentation == null)
        {
            Shader shader = Resources.Load<Shader>("DisplayOutput");
            if (shader == null) { Debug.LogError("Display presentation shader is missing."); enabled = false; return; }
            presentation = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }
        Display display = Display.displays[displayIndex];
        presentation.SetTexture(TextureId, target);
        // URP exposes RenderTextures with bottom-left UV origin; native Metal
        // backbuffers use top-left, just as URP's Render Graph final blit does.
        presentation.SetVector(ScaleBiasId, SystemInfo.graphicsUVStartsAtTop
            ? new Vector4(1f, -1f, 0f, 1f) : new Vector4(1f, 1f, 0f, 0f));
        presentation.SetFloat(EncodeId, QualitySettings.activeColorSpace == ColorSpace.Linear &&
            display.requiresSrgbBlitToBackbuffer ? 1f : 0f);
        RenderTexture previous = RenderTexture.active;
        try
        {
            Graphics.SetRenderTarget(display.colorBuffer, display.depthBuffer);
            GL.Viewport(new Rect(0, 0,
                displayIndex == 0 ? Screen.width : display.renderingWidth,
                displayIndex == 0 ? Screen.height : display.renderingHeight));
            if (presentation.SetPass(0))
            {
                Graphics.DrawProceduralNow(MeshTopology.Triangles, 3);
                PresentedFrames++;
            }
        }
        finally { RenderTexture.active = previous; }
    }

    private void ReleaseTarget()
    {
        if (target == null) return;
        if (cameras != null)
            foreach (Camera camera in cameras)
                if (camera != null && camera.targetTexture == target) camera.targetTexture = null;
        target.Release();
        DestroyOwned(target);
        target = null;
    }

    private static void DestroyOwned(Object value)
    {
        if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
    }
}
