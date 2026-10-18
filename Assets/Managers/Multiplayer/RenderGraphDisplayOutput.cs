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
    private bool nativeWindow;
    private System.IntPtr nativeTexture;
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

    public void ConfigureWindow(Camera[] outputCameras, int preferredScreen)
    {
        if (!RacePlayerWindows.Open(preferredScreen))
        {
            Debug.LogError("Could not create the P2 window. Check the RaceWindows plugin and Metal graphics API.");
            return;
        }
        nativeWindow = true;
        cameras = outputCameras;
        displayIndex = preferredScreen;
        RefreshTarget();
        Debug.Log("Race output windows: P1=Unity window, P2=native window (move either by its title bar).");
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
        if (nativeWindow) RacePlayerWindows.Close();
        nativeWindow = false;
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
        if (cameras == null || Application.isEditor) return;
        if (nativeWindow)
        {
            if (RacePlayerWindows.CloseRequested) { Application.Quit(); return; }
            Vector2Int size = RacePlayerWindows.Size;
            ResizeTarget(size.x, size.y);
            return;
        }
        if (displayIndex <= 0 || displayIndex >= Display.displays.Length) return;
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
        if (nativeWindow) nativeTexture = target.GetNativeTexturePtr();
        foreach (Camera camera in cameras)
            if (camera != null) camera.targetTexture = target;
    }

    private void Present(ScriptableRenderContext context, List<Camera> rendered)
    {
        using var timing = PresentMarker.Auto();
        if (CapturingPreview || target == null || Application.isEditor || (!nativeWindow && displayIndex >= Display.displays.Length)) return;
