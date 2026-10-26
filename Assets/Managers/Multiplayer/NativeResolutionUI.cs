using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>Upscale the world first, then draw the existing camera-space UI at output resolution.</summary>
public sealed class NativeResolutionUI : MonoBehaviour
{
    private Camera world, ui;
    private Camera[] worldCameras;
    private Canvas hud, backdrop;
    private RawImage image;
    private Material backdropMaterial;
    private RenderTexture worldTexture;
    private RenderTexture[] savedTargets;
    private RenderTexture savedUiTarget;
    private UniversalRenderPipelineAsset pipeline;
    private float savedScale;
    private bool routed, scaleOverridden, capturing;

    public void Configure(Camera worldBase, Camera main, Camera front, Camera uiCamera, Canvas canvas)
    {
        world = worldBase;
        ui = uiCamera;
        hud = canvas;
        worldCameras = new[] { worldBase, main, front };
        savedTargets = new RenderTexture[worldCameras.Length];
        world.GetUniversalAdditionalCameraData().cameraStack.Remove(ui);
        var data = ui.GetUniversalAdditionalCameraData();
        data.renderType = CameraRenderType.Base;
        data.cameraStack.Clear();
        data.renderPostProcessing = false;
        data.renderShadows = false;
        data.requiresColorOption = CameraOverrideOption.Off;
        data.requiresDepthOption = CameraOverrideOption.Off;
        data.antialiasing = AntialiasingMode.None;
        ui.allowHDR = false;
        ui.allowMSAA = false;
        ui.allowDynamicResolution = false;
        ui.clearFlags = CameraClearFlags.SolidColor;
        ui.backgroundColor = Color.black;
        ui.depth = Mathf.Max(world.depth, main != null ? main.depth : world.depth) + 10f;

        if (backdrop == null)
        {
            var root = new GameObject("Native UI world composite", typeof(RectTransform), typeof(Canvas)) { hideFlags = HideFlags.DontSave };
            root.transform.SetParent(transform, false);
            root.layer = hud.gameObject.layer;
            backdrop = root.GetComponent<Canvas>();
            backdrop.renderMode = RenderMode.ScreenSpaceCamera;
            backdrop.worldCamera = ui;
            backdrop.planeDistance = hud.planeDistance;
            backdrop.sortingOrder = short.MinValue;
            var child = new GameObject("Upscaled world", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            child.layer = root.layer;
            child.transform.SetParent(root.transform, false);
            image = child.GetComponent<RawImage>();
            image.raycastTarget = false;
            var shader = Resources.Load<Shader>("NativeUIWorld");
            if (shader == null) throw new InvalidOperationException("NativeUIWorld shader is missing.");
            backdropMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            image.material = backdropMaterial;
            var rect = image.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
        backdrop.targetDisplay = ui.targetDisplay;
    }

    private void OnEnable()
    {
        if (backdrop != null) backdrop.enabled = true;
        RenderPipelineManager.beginContextRendering += BeginContext;
        RenderPipelineManager.beginCameraRendering += BeginCamera;
        RenderPipelineManager.endCameraRendering += EndCamera;
        RenderPipelineManager.endContextRendering += EndContext;
    }

    private void BeginContext(ScriptableRenderContext context, List<Camera> cameras)
    {
        if (capturing || world == null || ui == null || !world.isActiveAndEnabled ||
            !cameras.Contains(world) || !cameras.Contains(ui)) return;
        Route(world.targetTexture, world.targetTexture != null ? world.targetTexture.width : Screen.width,
            world.targetTexture != null ? world.targetTexture.height : Screen.height);
    }

    private void Route(RenderTexture output, int width, int height)
    {
        if (routed) return;
        if (worldTexture == null || worldTexture.width != width || worldTexture.height != height)
        {
            ReleaseTexture();
            worldTexture = new RenderTexture(width, height, 24, RenderTextureFormat.Default)
            {
                name = "Race world before native UI", hideFlags = HideFlags.DontSave,
                antiAliasing = 1, useMipMap = false, filterMode = FilterMode.Bilinear
            };
            worldTexture.Create();
        }
        savedUiTarget = ui.targetTexture;
        for (int i = 0; i < worldCameras.Length; i++)
        {
            var camera = worldCameras[i];
            if (camera == null) continue;
            savedTargets[i] = camera.targetTexture;
            camera.targetTexture = worldTexture;
        }
        ui.targetTexture = output;
        image.texture = worldTexture;
        // The texture includes letterbox bars; map only its viewport into the UI viewport.
        image.uvRect = world.rect;
        backdrop.targetDisplay = hud.targetDisplay;
        routed = true;
    }

    private void BeginCamera(ScriptableRenderContext context, Camera camera)
    {
        if (camera != ui || !routed) return;
        OverrideScale();
    }

    private void OverrideScale()
    {
        if (scaleOverridden) return;
        pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (pipeline == null) return;
        // URP 17.3 creates Base camera data AFTER beginCameraRendering. An Overlay
        // inherits the world's descriptor, so it cannot override resolution here.
        savedScale = pipeline.renderScale;
        pipeline.renderScale = 1f;
        scaleOverridden = true;
    }

    private void EndCamera(ScriptableRenderContext context, Camera camera)
    {
        if (camera != ui) return;
        RestoreScale();
        if (!capturing) RestoreTargets(); // Before RenderGraphDisplayOutput presents P2.
    }

    private void EndContext(ScriptableRenderContext context, List<Camera> cameras)
    {
        if (capturing) return;
        RestoreScale();
        RestoreTargets();
    }

    /// <summary>Both requests are necessary: a world stack request no longer includes the native UI.</summary>
    public void RenderToTarget(RenderTexture destination)
    {
        if (world == null || ui == null) throw new InvalidOperationException("Native UI rig is not configured.");
        if (routed) throw new InvalidOperationException("Cannot capture inside an active display render.");
        capturing = true;
        try
        {
            Route(destination, destination.width, destination.height);
            Canvas.ForceUpdateCanvases();
            Submit(world, worldTexture);
            // Emit both canvases for synchronous Editor/batch captures. Restore the
            // exact camera-space layout afterwards, including the separate backdrop.
            var canvases = new[] { backdrop, hud };
            var modes = new RenderMode[2];
            var positions = new Vector3[2];
            var rotations = new Quaternion[2];
            var scales = new Vector3[2];
            var sizes = new Vector2[2];
            try
            {
                for (int i = 0; i < canvases.Length; i++)
                {
                    var canvas = canvases[i];
                    var rect = (RectTransform)canvas.transform;
                    modes[i] = canvas.renderMode;
                    positions[i] = rect.position; rotations[i] = rect.rotation;
                    scales[i] = rect.localScale; sizes[i] = rect.sizeDelta;
                }
                for (int i = 0; i < canvases.Length; i++)
                {
                    var canvas = canvases[i];
                    var rect = (RectTransform)canvas.transform;
                    Vector2 size = rect.rect.size;
                    foreach (var label in canvas.GetComponentsInChildren<TMPro.TMP_Text>()) label.ForceMeshUpdate();
                    canvas.renderMode = RenderMode.WorldSpace;
                    rect.SetPositionAndRotation(positions[i], rotations[i]);
                    rect.localScale = scales[i]; rect.sizeDelta = size;
                    foreach (var graphic in canvas.GetComponentsInChildren<Graphic>()) graphic.SetVerticesDirty();
                }
                Canvas.ForceUpdateCanvases();
#if UNITY_EDITOR
                ScriptableRenderContext.EmitGeometryForCamera(ui);
#endif
                // Edit-mode validation may not have run MonoBehaviour.OnEnable.
                OverrideScale();
                Submit(ui, destination);
            }
            finally
            {
                for (int i = 0; i < canvases.Length; i++)
                {
                    canvases[i].renderMode = modes[i];
                    var rect = (RectTransform)canvases[i].transform;
                    rect.SetPositionAndRotation(positions[i], rotations[i]);
                    rect.localScale = scales[i]; rect.sizeDelta = sizes[i];
                }
            }
        }
        finally
        {
            RestoreScale(); RestoreTargets(); capturing = false;
            Canvas.ForceUpdateCanvases();
        }
    }

    private static void Submit(Camera camera, RenderTexture destination)
    {
        var request = new RenderPipeline.StandardRequest { destination = destination };
        if (!RenderPipeline.SupportsRenderRequest(camera, request))
            throw new InvalidOperationException("Native UI capture requires URP StandardRequest support.");
        RenderPipeline.SubmitRenderRequest(camera, request);
    }

    private void RestoreScale()
    {
        if (scaleOverridden && pipeline != null) pipeline.renderScale = savedScale;
        scaleOverridden = false;
    }

    private void RestoreTargets()
    {
        if (!routed) return;
        for (int i = 0; i < worldCameras.Length; i++)
            if (worldCameras[i] != null) worldCameras[i].targetTexture = savedTargets[i];
        ui.targetTexture = savedUiTarget;
        routed = false;
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginContextRendering -= BeginContext;
        RenderPipelineManager.beginCameraRendering -= BeginCamera;
        RenderPipelineManager.endCameraRendering -= EndCamera;
        RenderPipelineManager.endContextRendering -= EndContext;
        RestoreScale(); RestoreTargets(); ReleaseTexture();
        if (backdrop != null) backdrop.enabled = false;
    }

    private void ReleaseTexture()
    {
        if (worldTexture == null) return;
        worldTexture.Release();
        if (Application.isPlaying) Destroy(worldTexture); else DestroyImmediate(worldTexture);
        worldTexture = null;
    }

    private void OnDestroy()
    {
        if (backdropMaterial == null) return;
        if (Application.isPlaying) Destroy(backdropMaterial); else DestroyImmediate(backdropMaterial);
    }
}
