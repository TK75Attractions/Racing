using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 1人分の物理カメラ、Cinemachineカメラ、UIをまとめた実行時参照です。
/// </summary>
public sealed class PlayerDisplayRig
{
    public int PlayerIndex { get; }
    public int DisplayIndex { get; }
    public GameObject CameraRoot { get; }
    public GameObject CanvasRoot { get; }
    public GameObject BackCameraRoot { get; }
    public CinemachineCamera RaceCamera { get; }
    public Camera MainCamera { get; }
    public Transform VisualEffectPivot { get; }
    public RaceSpeedVisualController RaceVisuals { get; }
    public Camera UiCamera { get; }
    public Camera FrontCamera { get; }
    public Camera BackImageCamera { get; }
    public Camera BackCamera { get; }
    public Canvas Canvas { get; }
    public ScreenTransitionController Transition { get; }
    public bool OwnsRuntimeObjects { get; }

    public PlayerDisplayRig(
        int playerIndex,
        GameObject cameraRoot,
        GameObject canvasRoot,
        GameObject backCameraRoot,
        CinemachineCamera raceCamera,
        bool ownsRuntimeObjects,
        int displayIndex = -1)
    {
        PlayerIndex = playerIndex;
        DisplayIndex = displayIndex >= 0 ? displayIndex : playerIndex;
        CameraRoot = cameraRoot;
        CanvasRoot = canvasRoot;
        BackCameraRoot = backCameraRoot;
        RaceCamera = raceCamera;
        OwnsRuntimeObjects = ownsRuntimeObjects;

        MainCamera = FindCamera(cameraRoot, "MainCamera");
        VisualEffectPivot = EnsureVisualEffectPivot(cameraRoot, MainCamera);
        RaceVisuals = cameraRoot != null
            ? cameraRoot.GetComponent<RaceSpeedVisualController>() ??
                cameraRoot.AddComponent<RaceSpeedVisualController>()
            : null;
        UiCamera = FindCamera(cameraRoot, "UICamera");
        FrontCamera = FindCamera(cameraRoot, "FrontCamera");
        BackImageCamera = FindCamera(cameraRoot, "BackImageCamera");
        BackCamera = backCameraRoot != null ? backCameraRoot.GetComponent<Camera>() : null;
        Canvas = canvasRoot != null ? canvasRoot.GetComponent<Canvas>() : null;
        Transition = canvasRoot != null ? canvasRoot.GetComponent<ScreenTransitionController>() : null;
    }

    public void Configure(float cameraBlendSeconds)
    {
        OutputChannels outputChannel = (OutputChannels)(1 << PlayerIndex);

        if (RaceCamera != null)
        {
            RaceCamera.OutputChannel = outputChannel;
        }

        Camera[] cameras = CameraRoot != null
            ? CameraRoot.GetComponentsInChildren<Camera>(true)
            : new Camera[0];

        foreach (Camera camera in cameras)
        {
            camera.targetDisplay = DisplayIndex;
        }

        if (BackCamera != null)
        {
            BackCamera.targetDisplay = DisplayIndex;
        }

        CinemachineBrain brain = MainCamera != null ? MainCamera.GetComponent<CinemachineBrain>() : null;
        if (brain != null)
        {
            brain.ChannelMask = outputChannel;
            brain.DefaultBlend = new CinemachineBlendDefinition(
                CinemachineBlendDefinition.Styles.EaseInOut,
                Mathf.Max(0f, cameraBlendSeconds));
        }

        if (PlayerIndex > 0 && MainCamera != null)
        {
            AudioListener listener = MainCamera.GetComponent<AudioListener>();
            if (listener != null)
            {
                listener.enabled = false;
            }
        }

        if (Canvas != null)
        {
            Canvas.worldCamera = UiCamera;
            Canvas.targetDisplay = DisplayIndex;
        }

        RebuildCameraStack();

        if (MainCamera != null)
        {
            FreezeAspectRate aspectController = MainCamera.GetComponent<FreezeAspectRate>();
            aspectController?.ConfigureForDisplay(DisplayIndex, BackCamera);
            aspectController?.ConfigureCanvas(Canvas);
        }

        // URP 17.3 imports every native backbuffer using Screen.width/height.
        // Additional displays can have a different size, so render their whole
        // stack to an explicit texture before presenting to the native buffers.
        if (Application.isPlaying && !Application.isEditor && DisplayIndex > 0 && CameraRoot != null)
        {
            var output = CameraRoot.GetComponent<RenderGraphDisplayOutput>() ??
                CameraRoot.AddComponent<RenderGraphDisplayOutput>();
            var outputCameras = new[] { BackCamera, BackImageCamera, MainCamera, FrontCamera, UiCamera };
            if (RacePlayerWindows.UsesNativeWindow) output.ConfigureWindow(outputCameras, DisplayIndex);
            else if (DisplayIndex < Display.displays.Length) output.Configure(outputCameras, DisplayIndex);
        }
    }

    public void Dispose()
    {
        if (!OwnsRuntimeObjects)
        {
            return;
        }

        DestroyObject(RaceCamera != null ? RaceCamera.gameObject : null);
        DestroyObject(CanvasRoot);
        DestroyObject(CameraRoot);
        DestroyObject(BackCameraRoot);
    }

    private void RebuildCameraStack()
    {
        if (BackImageCamera == null)
        {
            return;
        }

        UniversalAdditionalCameraData cameraData = BackImageCamera.GetUniversalAdditionalCameraData();
        if (cameraData == null || cameraData.renderType != CameraRenderType.Base)
        {
            return;
        }

        cameraData.cameraStack.Clear();
        AddOverlayCamera(cameraData, MainCamera);
        AddOverlayCamera(cameraData, FrontCamera);
        AddOverlayCamera(cameraData, UiCamera);
    }

    private static void AddOverlayCamera(UniversalAdditionalCameraData cameraData, Camera camera)
    {
        if (camera != null)
        {
            cameraData.cameraStack.Add(camera);
        }
    }

    private static Camera FindCamera(GameObject root, string childName)
    {
        if (root == null)
        {
            return null;
        }

        Transform child = root.transform.Find(childName);
        if (child != null)
        {
            Camera directCamera = child.GetComponent<Camera>();
            if (directCamera != null) return directCamera;
        }

        Transform[] children = root.GetComponentsInChildren<Transform>(true);
        foreach (Transform candidate in children)
        {
            if (candidate.name != childName) continue;
            Camera camera = candidate.GetComponent<Camera>();
            if (camera != null) return camera;
        }

        return null;
    }

    private static Transform EnsureVisualEffectPivot(GameObject root, Camera mainCamera)
    {
        if (root == null || mainCamera == null) return null;

        Transform pivot = root.transform.Find("VisualEffectPivot");
        if (pivot == null)
        {
            GameObject pivotObject = new GameObject("VisualEffectPivot");
            pivot = pivotObject.transform;
            pivot.SetParent(root.transform, false);
        }

        if (mainCamera.transform.parent != pivot)
        {
            Vector3 localPosition = mainCamera.transform.localPosition;
            Quaternion localRotation = mainCamera.transform.localRotation;
            Vector3 localScale = mainCamera.transform.localScale;
            mainCamera.transform.SetParent(pivot, false);
            mainCamera.transform.localPosition = localPosition;
            mainCamera.transform.localRotation = localRotation;
            mainCamera.transform.localScale = localScale;
        }

        return pivot;
    }

    private static void DestroyObject(GameObject target)
    {
        if (target != null)
        {
            Object.Destroy(target);
        }
    }
}

/// <summary>
/// シーンにある1画面目の表示構成から、2画面目の構成を実行時に生成します。
/// </summary>
public static class TwoPlayerDisplayFactory
{
    public const int PlayerCount = 2;

    public static PlayerDisplayRig[] Create(Transform managerRoot, float cameraBlendSeconds, int playerTwoDisplayIndex = 1)
    {
        if (managerRoot == null)
        {
            Debug.LogError("Two-player display setup requires the GameManagers root.");
            return new PlayerDisplayRig[0];
        }

        Transform cameraRoot = managerRoot.Find("CManager");
        Transform canvasRoot = managerRoot.Find("MainCanvas");
        Transform backCameraRoot = managerRoot.Find("BackCamera");
        Transform raceCameraRoot = managerRoot.Find("VCamera");

        if (cameraRoot == null || canvasRoot == null || backCameraRoot == null || raceCameraRoot == null)
        {
            Debug.LogError("CManager, MainCanvas, BackCamera, or VCamera was not found under GameManagers.");
            return new PlayerDisplayRig[0];
        }

        playerTwoDisplayIndex = Mathf.Clamp(playerTwoDisplayIndex, 1, 7);
        // Editor Game views preview virtual displays even with one physical monitor.
        if (Application.isPlaying && !Application.isEditor && !RacePlayerWindows.UsesNativeWindow)
        {
            if (Display.displays.Length <= playerTwoDisplayIndex)
            {
                Debug.LogError($"P2 Display {playerTwoDisplayIndex + 1} is unavailable; {Display.displays.Length} displays connected. Check GManager > Displays > Player Two Display.");
            }
            else
            {
#if UNITY_STANDALONE_WIN
                managerRoot.gameObject.AddComponent<WindowsPlayerWindow>().Activate(playerTwoDisplayIndex);
#else
                Display.displays[playerTwoDisplayIndex].Activate();
#endif
                Debug.Log($"Race display outputs: P1=Display 1 ({Screen.width}x{Screen.height}), P2=Display {playerTwoDisplayIndex + 1} ({Display.displays[playerTwoDisplayIndex].systemWidth}x{Display.displays[playerTwoDisplayIndex].systemHeight}).");
            }
        }

        PlayerDisplayRig playerOne = new PlayerDisplayRig(
            0,
            cameraRoot.gameObject,
            canvasRoot.gameObject,
            backCameraRoot.gameObject,
            raceCameraRoot.GetComponent<CinemachineCamera>(),
            false);

        GameObject cameraRootTwo = Object.Instantiate(cameraRoot.gameObject, managerRoot);
        cameraRootTwo.name = "CManager_P2";

        GameObject canvasRootTwo = Object.Instantiate(canvasRoot.gameObject, managerRoot);
        canvasRootTwo.name = "MainCanvas_P2";

        GameObject backCameraRootTwo = Object.Instantiate(backCameraRoot.gameObject, managerRoot);
        backCameraRootTwo.name = "BackCamera_P2";

        CinemachineCamera raceCameraTwo = Object.Instantiate(
            raceCameraRoot.GetComponent<CinemachineCamera>(),
            managerRoot);
        raceCameraTwo.name = "VCamera_P2";

        PlayerDisplayRig playerTwo = new PlayerDisplayRig(
            1,
            cameraRootTwo,
            canvasRootTwo,
            backCameraRootTwo,
            raceCameraTwo,
            true,
            playerTwoDisplayIndex);

        PlayerDisplayRig[] rigs = { playerOne, playerTwo };
        foreach (PlayerDisplayRig rig in rigs)
        {
            rig.Configure(cameraBlendSeconds);
        }

        return rigs;
    }
}
