using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>Capture the production P1 night camera stack without UI; no runtime background camera.</summary>
public static class TitleBackgroundBuilder
{
    private const string Output = "Assets/Resources/UI/Neon/TitleBackground.png";
    private const string BatchKey = "Racing.TitleBackgroundBuilder.";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static bool captureQueued;

    [MenuItem("Racing/UI/Build Title Background from P1 Start View (Play Mode)")]
    public static void Build()
    {
        Gmanager manager = Gmanager.Control;
        RaceEnvironmentController environment = RaceEnvironmentController.Active;
        if (!Application.isPlaying || manager == null || environment == null ||
            environment.CurrentTimeOfDay != RaceEnvironmentController.TimeOfDay.Night ||
            (manager.state != Gmanager.State.Countdown && manager.state != Gmanager.State.Game))
            throw new InvalidOperationException("Start a night race and pause at the start gate before capturing the title.");
        Transform car = manager.GetPlayerCarTransform(0);
        RaceCourse course = manager.course;
        if (car == null || course == null || !course.TryGetSampleAtProgress(0f, out var start) ||
            Vector3.Distance(car.position, start.position) > 100f)
            throw new InvalidOperationException("P1 must be at the start gate, with the car and lanterns visible in the game camera.");
        Camera camera = manager.GetPlayerCaptureCamera(0);
        Camera view = manager.GetPlayerViewCamera(0);
        if (camera == null || view == null || !view.GetUniversalAdditionalCameraData().renderPostProcessing)
            throw new InvalidOperationException("The production night camera stack and post processing are required.");

        var stack = new List<Camera> { camera };
        stack.AddRange(camera.GetUniversalAdditionalCameraData().cameraStack);
        var previous = new RenderTexture[stack.Count];
        Canvas[] canvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
        var enabled = new bool[canvases.Length];
        var target = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
        Texture2D photo = null;
        RenderTexture active = RenderTexture.active;
        for (int i = 0; i < stack.Count; i++) if (stack[i] != null) previous[i] = stack[i].targetTexture;
        for (int i = 0; i < canvases.Length; i++) enabled[i] = canvases[i].enabled;
        try
        {
            target.Create();
            for (int i = 0; i < canvases.Length; i++) canvases[i].enabled = false;
            for (int i = 0; i < stack.Count; i++) if (stack[i] != null) stack[i].targetTexture = target;
            var request = new RenderPipeline.StandardRequest { destination = target };
            if (!RenderPipeline.SupportsRenderRequest(camera, request))
                throw new InvalidOperationException("The production camera stack does not support StandardRequest.");
            RenderPipeline.SubmitRenderRequest(camera, request);
            RenderTexture.active = target;
            photo = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            photo.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
            photo.Apply();
            File.WriteAllBytes(Output, photo.EncodeToPNG());
            AssetDatabase.ImportAsset(Output, ImportAssetOptions.ForceUpdate);
            Debug.Log("TITLE_BACKGROUND_BUILT: " + Output + "; inspect the gate, lanterns, car and title text contrast before committing the image.");
        }
        finally
        {
            RenderTexture.active = active;
            for (int i = 0; i < stack.Count; i++) if (stack[i] != null) stack[i].targetTexture = previous[i];
            for (int i = 0; i < canvases.Length; i++) if (canvases[i] != null) canvases[i].enabled = enabled[i];
            if (photo != null) UnityEngine.Object.DestroyImmediate(photo);
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
        }
    }

    /// <summary>Starts an unattended Play Mode capture. Run Unity with graphics and without -quit.</summary>
    public static void RunBatch()
    {
        if (SessionState.GetBool(BatchKey + "Active", false) || EditorApplication.isPlayingOrWillChangePlaymode)
        {
            if (Application.isBatchMode) EditorApplication.Exit(1);
            throw new InvalidOperationException("Stop Play Mode / the current title capture before starting.");
        }
        try
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("Title background capture requires graphics. Omit -nographics.");
            SessionState.SetBool(BatchKey + "Active", true);
            SessionState.SetInt(BatchKey + "Stage", 0);
            SessionState.SetString(BatchKey + "Started", EditorApplication.timeSinceStartup.ToString("R", CultureInfo.InvariantCulture));
            SessionState.SetFloat(BatchKey + "TimeScale", Time.timeScale);
            SessionState.SetBool(BatchKey + "Background", Application.runInBackground);
            EditorApplication.update -= TickBatch;
            EditorApplication.update += TickBatch;
            EditorApplication.playModeStateChanged -= BatchPlayModeChanged;
            EditorApplication.playModeStateChanged += BatchPlayModeChanged;
            // batchmode の起動直後は別シーンが開いていることがあるため、レースシーンを開いてから再生する。
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
            EditorApplication.isPlaying = true;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            if (!SessionState.GetBool(BatchKey + "Active", false))
            {
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }
            FinishBatch(false, exception.Message);
        }
    }

    [InitializeOnLoadMethod]
    private static void ResumeBatch()
    {
        if (!SessionState.GetBool(BatchKey + "Active", false)) return;
        captureQueued = false;
        EditorApplication.update -= TickBatch;
        EditorApplication.update += TickBatch;
        EditorApplication.playModeStateChanged -= BatchPlayModeChanged;
        EditorApplication.playModeStateChanged += BatchPlayModeChanged;
    }

    private static void BatchPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingPlayMode && SessionState.GetBool(BatchKey + "Active", false) &&
            SessionState.GetInt(BatchKey + "Stage", 0) != 2)
            FinishBatch(false, "Play Mode stopped before the title background was captured.");
    }

    private static void TickBatch()
    {
        if (!SessionState.GetBool(BatchKey + "Active", false)) return;
        try
        {
            if (SessionState.GetInt(BatchKey + "Stage", 0) == 2)
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode) ExitBatch();
                return;
            }
            if (EditorApplication.timeSinceStartup - double.Parse(SessionState.GetString(BatchKey + "Started", "0"), CultureInfo.InvariantCulture) > 180d)
                throw new TimeoutException("Title background capture exceeded 180 seconds.");
            if (!EditorApplication.isPlaying || !Application.isPlaying) return;
            Time.timeScale = 1f;
            Application.runInBackground = true;
            var manager = UnityEngine.Object.FindFirstObjectByType<Gmanager>();
            if (manager == null) return;
            if (SessionState.GetInt(BatchKey + "Stage", 0) == 0)
            {
                foreach (var environment in UnityEngine.Object.FindObjectsByType<RaceEnvironmentController>(FindObjectsSortMode.None))
                    if (environment.CurrentTimeOfDay != RaceEnvironmentController.TimeOfDay.Night)
                        environment.SetSituation(environment.CurrentWeather, RaceEnvironmentController.TimeOfDay.Night);
                InvokeStart(manager, "StartGameWhenScreenCovered");
                InvokeStart(manager, "CompleteGameStart");
                // CourseVisualTour と同じく、画面遷移の演出を飛ばしてカウントダウンへ進める。
                foreach (var transition in UnityEngine.Object.FindObjectsByType<ScreenTransitionController>(FindObjectsSortMode.None))
                    transition.ApplyStateImmediate(Gmanager.State.Countdown);
                SessionState.SetInt(BatchKey + "Stage", 1);
                return;
            }
            if (manager.state != Gmanager.State.Game || !manager.IsDrivingEnabled) return;
            if (captureQueued) return;
            Transform startTransform = typeof(Gmanager).GetMethod("GetStartPoint", Private)?.Invoke(manager, null) as Transform;
            RaceCourse course = manager.course;
            if (startTransform == null || course == null || !course.TryGetSampleAtProgress(course.GetProgressDistance(startTransform.position), out var sample))
                throw new InvalidOperationException("Start gate or course sample is unavailable.");
            for (int i = 0; i < 2; i++)
            {
                Transform car = manager.GetPlayerCarTransform(i);
                if (car == null) throw new InvalidOperationException("P" + (i + 1) + " car was not created.");
                var body = car.GetComponent<Rigidbody>();
                var resetter = car.GetComponent<CarResetter>();
                if (body == null || resetter == null) throw new InvalidOperationException("P" + (i + 1) + " requires Rigidbody and CarResetter.");
                resetter.enabled = false;
                var mover = car.GetComponent<DebugMover>();
                if (mover != null) mover.enabled = false;
                var placement = new RaceCourse.CourseSample
                {
                    position = sample.position, forward = sample.forward, up = sample.up, right = sample.right,
                    width = 5.8f // CarResetter's two recovery lanes are then spaced 1.8 m apart.
                };
                // タイトル用の構図: P1 と P2 を左右に並べる（車幅が重ならない間隔）。
                car.SetPositionAndRotation(sample.position + sample.up * .05f + sample.right * (i == 0 ? -1.7f : 1.7f), Quaternion.LookRotation(sample.forward, sample.up));
                body.constraints = RigidbodyConstraints.FreezeAll;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            // 正面から撮るので、路面に落ちるヘッドライトのスポット光は消す（ランプ自体の発光は残る）。
            for (int i = 0; i < 2; i++)
                foreach (var lamp in manager.GetPlayerCarTransform(i).GetComponentsInChildren<Light>(true))
                    if (lamp.type == LightType.Spot) lamp.enabled = false;
            // CPU 車はタイトルの構図に入れない。
            foreach (var cpu in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
                if (cpu.parent == null && cpu.name.StartsWith("CPU") && cpu.name.EndsWith("_Car")) cpu.gameObject.SetActive(false);
            Physics.SyncTransforms();
            captureQueued = true;
            EditorApplication.delayCall += () =>
            {
                try
                {
                    if (!SessionState.GetBool(BatchKey + "Active", false)) return;
                    Camera capture = manager.GetPlayerCaptureCamera(0);
                    if (capture == null) throw new InvalidOperationException("P1 capture camera is unavailable.");
                    // ベースと重ねて描くカメラを同じ位置へ動かす（ベースだけ動かすと重ね描きがずれる）。
                    var stackCameras = new List<Camera> { capture };
                    stackCameras.AddRange(capture.GetUniversalAdditionalCameraData().cameraStack);
                    var oldPoses = new List<(Vector3, Quaternion)>();
                    foreach (Camera c in stackCameras) oldPoses.Add((c.transform.position, c.transform.rotation));
                    try
                    {
                        Vector3 center = sample.position + sample.up * 1.2f;
                        Vector3 eye = center + sample.forward * 8.5f + sample.right * 3.2f + sample.up * 1.4f;
                        foreach (Camera c in stackCameras)
                            if (c != null) c.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(center - eye, sample.up));
                        // Build uses the production P1 camera stack and handles render targets / PNG import.
                        Build();
                    }
                    finally
                    {
                        for (int k = 0; k < stackCameras.Count; k++)
                            if (stackCameras[k] != null) stackCameras[k].transform.SetPositionAndRotation(oldPoses[k].Item1, oldPoses[k].Item2);
                    }
                    SessionState.SetInt(BatchKey + "Stage", 2);
                    FinishBatch(true, Output);
                }
                catch (Exception exception) { Debug.LogException(exception); FinishBatch(false, exception.Message); }
            };
        }
        catch (Exception exception) { Debug.LogException(exception); FinishBatch(false, exception.Message); }
    }

    private static void InvokeStart(Gmanager manager, string methodName)
    {
        var method = typeof(Gmanager).GetMethod(methodName, Private);
        if (method == null) throw new MissingMethodException(typeof(Gmanager).Name, methodName);
        method.Invoke(manager, null);
    }

    private static void FinishBatch(bool success, string message)
    {
        if (!SessionState.GetBool(BatchKey + "Active", false)) return;
        SessionState.SetInt(BatchKey + "Stage", 2);
        SessionState.SetInt(BatchKey + "ExitCode", success ? 0 : 1);
        Time.timeScale = SessionState.GetFloat(BatchKey + "TimeScale", 1f);
        Application.runInBackground = SessionState.GetBool(BatchKey + "Background", false);
        Debug.Log((success ? "TITLE_BACKGROUND_BUILT: " : "TITLE_BACKGROUND_FAILED: ") + message);
        EditorApplication.isPlaying = false;
    }

    private static void ExitBatch()
    {
        SessionState.SetBool(BatchKey + "Active", false);
        EditorApplication.update -= TickBatch;
        EditorApplication.playModeStateChanged -= BatchPlayModeChanged;
        EditorApplication.Exit(SessionState.GetInt(BatchKey + "ExitCode", 1));
    }
}
