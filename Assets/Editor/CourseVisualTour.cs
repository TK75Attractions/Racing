using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>Run with graphics enabled and without -quit; this asynchronous job exits the Editor itself.</summary>
public static class CourseVisualTour
{
    private const string Key = "Racing.CourseVisualTour.";
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const float Spacing = 120f, Speed = 25f, SettleSeconds = .6f;
    private const double TimeoutSeconds = 240d;
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [Serializable] private sealed class Frame
    {
        public string file;
        public float distanceMeters, actualDistanceMeters;
        public Vector3 placedPosition, position, forward;
        public Quaternion placedRotation, rotation;
        public Vector3 cameraPosition;
        public Quaternion cameraRotation;
        public float speedMetersPerSecond;
    }

    [Serializable] private sealed class Manifest
    {
        public string scene = ScenePath;
        public float courseLengthMeters, spacingMeters, directionSign;
        public bool complete;
        public string error;
        public List<Frame> frames = new List<Frame>();
    }

    private static RenderTexture target;
    private static int renderedFrame = -1, placedFrame;
    private static float placedTime;
    private static Frame pending;
    private static Manifest manifest;

    [MenuItem("Racing/Capture Course Visual Tour (Play Mode)")]
    public static void RunBatch()
    {
        if (SessionState.GetBool(Key + "Active", false) || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play Mode / the current tour before starting a visual tour.");
        try
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("Course photos require graphics. Omit -nographics.");
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != ScenePath)
            {
                if (!Application.isBatchMode)
                    throw new InvalidOperationException("Open SampleScene before starting the visual tour.");
                EditorSceneManager.OpenScene(ScenePath);
            }
            string output = Environment.GetEnvironmentVariable("RACING_VISUAL_TOUR_OUTPUT");
            if (string.IsNullOrWhiteSpace(output))
                output = Path.Combine(Path.GetTempPath(), "racing-visual-tour-" + Guid.NewGuid().ToString("N"));
            output = Path.GetFullPath(output);
            Directory.CreateDirectory(output);
            SessionState.SetString(Key + "Output", output);
            SessionState.SetString(Key + "Started", EditorApplication.timeSinceStartup.ToString("R", CultureInfo.InvariantCulture));
            SessionState.SetInt(Key + "Stage", 0);
            SessionState.SetInt(Key + "ExitCode", 1);
            SessionState.SetFloat(Key + "TimeScale", Time.timeScale);
            SessionState.SetBool(Key + "Background", Application.runInBackground);
            manifest = new Manifest();
            PersistManifest();
            pending = null;
            renderedFrame = -1;
            SessionState.SetBool(Key + "Active", true);
            Subscribe();
            Debug.Log("COURSE_VISUAL_TOUR: output=" + output);
            EditorApplication.isPlaying = true;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            if (SessionState.GetBool(Key + "Active", false)) Finish(false, exception.Message);
            else if (Application.isBatchMode) EditorApplication.Exit(1);
        }
    }

    [InitializeOnLoadMethod]
    private static void Resume()
    {
        if (!SessionState.GetBool(Key + "Active", false)) return;
        manifest = null;
        pending = null;
        renderedFrame = -1;
        Subscribe();
    }

    private static void Subscribe()
    {
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged -= StateChanged;
        EditorApplication.playModeStateChanged += StateChanged;
    }

    private static void StateChanged(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key + "Active", false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            // Disable Awake-created publishers before their Start coroutines, as in the existing validator.
            foreach (var publisher in UnityEngine.Object.FindObjectsByType<RaceRemoteFirebasePublisher>(FindObjectsSortMode.None))
                publisher.enabled = false;
        }
        if (state == PlayModeStateChange.ExitingPlayMode)
        {
            ReleaseTarget();
            if (SessionState.GetInt(Key + "Stage", 0) != 3)
                Finish(false, "Play Mode stopped before the visual tour completed.");
        }
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(Key + "Active", false)) return;
        try
        {
            if (SessionState.GetInt(Key + "Stage", 0) == 3)
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode) Exit();
                else if (EditorApplication.timeSinceStartup - double.Parse(SessionState.GetString(Key + "Stopping", "0"), CultureInfo.InvariantCulture) > 15d)
                    EditorApplication.Exit(1);
                return;
            }
            double started = double.Parse(SessionState.GetString(Key + "Started", "0"), CultureInfo.InvariantCulture);
            if (EditorApplication.timeSinceStartup - started > TimeoutSeconds)
                throw new TimeoutException("Course visual tour exceeded 240 seconds.");
            if (!EditorApplication.isPlaying || !Application.isPlaying) return;
            Time.timeScale = 1f;
            Application.runInBackground = true;
            var manager = UnityEngine.Object.FindFirstObjectByType<Gmanager>();
            if (manager == null) return;
            int stage = SessionState.GetInt(Key + "Stage", 0);
            if (stage == 0)
            {
                // Match CourseJumpPlaythroughProbe without starting its drivers or its exit handler.
                InvokeStart(manager, "StartGameWhenScreenCovered");
                InvokeStart(manager, "CompleteGameStart");
                foreach (var transition in UnityEngine.Object.FindObjectsByType<ScreenTransitionController>(FindObjectsSortMode.None))
                    transition.ApplyStateImmediate(Gmanager.State.Countdown);
                SessionState.SetInt(Key + "Stage", 1);
                return;
            }
            if (stage == 1 && (manager.state != Gmanager.State.Game || !manager.IsDrivingEnabled)) return;
            if (manager.state != Gmanager.State.Game)
                throw new InvalidOperationException("Race left Game state during capture.");

            Transform car = manager.GetPlayerCarTransform(0);
            var course = manager.course != null ? manager.course : UnityEngine.Object.FindFirstObjectByType<RaceCourse>();
            if (car == null || course == null || !course.HasValidPath)
                throw new InvalidOperationException("P1 or a valid RaceCourse was not created.");
            var body = car.GetComponent<Rigidbody>();
            var resetter = car.GetComponent<CarResetter>();
            if (body == null || resetter == null) throw new InvalidOperationException("P1 requires Rigidbody and CarResetter.");
            if (stage == 1 || pending == null) SuppressInterference(manager, car);
            if (manifest == null)
                manifest = JsonUtility.FromJson<Manifest>(SessionState.GetString(Key + "Manifest", ""));
            if (manifest == null) throw new InvalidOperationException("Visual tour manifest was lost.");
            manifest.courseLengthMeters = course.TotalLength;
            int count = Mathf.CeilToInt(course.TotalLength / Spacing);
            manifest.spacingMeters = course.TotalLength / count;
            var laps = UnityEngine.Object.FindFirstObjectByType<LapManager>();
            manifest.directionSign = laps != null && laps.IsProgressReversed ? -1f : 1f;

            if (pending == null)
            {
                // Redistribute the remainder, including the closing interval, over the full lap.
                float distance = manifest.frames.Count * manifest.spacingMeters;
                float progress = Mathf.Repeat(manifest.directionSign * distance, course.TotalLength);
                if (!course.TryGetSampleAtProgress(progress, out var sample))
                    throw new InvalidOperationException("Cannot sample course at " + progress);
                if (!resetter.TryResetToRoadSample(sample))
                    throw new InvalidOperationException("Cannot place P1 at " + progress);
                body.linearVelocity = car.forward * Speed;
                body.angularVelocity = Vector3.zero;
                body.WakeUp();
                pending = new Frame
                {
                    file = (manifest.frames.Count + 1).ToString("D2") + "-" + Mathf.RoundToInt(distance).ToString(CultureInfo.InvariantCulture) + ".png",
                    distanceMeters = distance, placedPosition = body.position, placedRotation = body.rotation
                };
                placedTime = Time.time;
                placedFrame = Time.frameCount;
                SessionState.SetInt(Key + "Stage", 2);
                return;
            }
            if (renderedFrame == Time.frameCount) return;
            Camera camera = manager.GetPlayerCaptureCamera(0);
            Camera view = manager.GetPlayerViewCamera(0);
            if (camera == null || view == null) throw new InvalidOperationException("P1 camera stack is unavailable.");
            // Preserve all existing night volumes, renderer effects and camera settings. Fail rather than silently omit post FX.
            if (!view.GetUniversalAdditionalCameraData().renderPostProcessing)
                throw new InvalidOperationException("P1 view camera has post processing disabled.");
            RenderPlayer(camera);
            renderedFrame = Time.frameCount;
            if (Time.time - placedTime < SettleSeconds || Time.frameCount - placedFrame < 6) return;
            SavePng(Path.Combine(SessionState.GetString(Key + "Output", ""), pending.file));
            pending.actualDistanceMeters = course.GetProgressDistance(body.position);
            pending.position = body.position;
            pending.rotation = body.rotation;
            pending.forward = car.forward;
            pending.speedMetersPerSecond = body.linearVelocity.magnitude;
            pending.cameraPosition = view.transform.position;
            pending.cameraRotation = view.transform.rotation;
            manifest.frames.Add(pending);
            Debug.Log("COURSE_VISUAL_TOUR_FRAME: " + pending.file);
            pending = null;
            PersistManifest();
            if (manifest.frames.Count >= count) Finish(true, "Captured " + count + " P1 course views.");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Finish(false, exception.Message);
        }
    }

    private static void InvokeStart(Gmanager manager, string name)
    {
        var method = typeof(Gmanager).GetMethod(name, Private);
        if (method == null) throw new MissingMethodException(typeof(Gmanager).Name, name);
        method.Invoke(manager, null);
    }

    private static void SuppressInterference(Gmanager manager, Transform player)
    {
        foreach (var resetter in UnityEngine.Object.FindObjectsByType<CarResetter>(FindObjectsSortMode.None)) resetter.enabled = false;
        foreach (var participant in manager.Participants)
            if (participant.Body != null && participant.Body.transform != player) participant.Body.gameObject.SetActive(false);
        // Keep physics and the real follow camera running; suppress steering/pedal input only.
        var mover = player.GetComponent<DebugMover>();
        if (mover != null) mover.enabled = false;
    }

    private static void RenderPlayer(Camera camera)
    {
        if (target == null)
        {
            target = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGBHalf) { hideFlags = HideFlags.DontSave };
            target.Create();
        }
        var request = new RenderPipeline.StandardRequest { destination = target };
        if (!RenderPipeline.SupportsRenderRequest(camera, request))
            throw new InvalidOperationException("The P1 camera stack does not support StandardRequest.");
        // Match the runtime preview: give base and overlays the same output size, then restore them.
        var stack = new List<Camera> { camera };
        var data = camera.GetUniversalAdditionalCameraData();
        if (data.renderType == CameraRenderType.Base && data.cameraStack != null) stack.AddRange(data.cameraStack);
        var previous = new RenderTexture[stack.Count];
        for (int i = 0; i < stack.Count; i++) if (stack[i] != null) previous[i] = stack[i].targetTexture;
        try
        {
            for (int i = 0; i < stack.Count; i++) if (stack[i] != null) stack[i].targetTexture = target;
            Canvas.ForceUpdateCanvases();
            RenderPipeline.SubmitRenderRequest(camera, request);
        }
        finally
        {
            for (int i = 0; i < stack.Count; i++) if (stack[i] != null) stack[i].targetTexture = previous[i];
            Canvas.ForceUpdateCanvases();
        }
    }

    private static void SavePng(string path)
    {
        var pixels = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        try
        {
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            pixels.Apply();
            File.WriteAllBytes(path, pixels.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            UnityEngine.Object.DestroyImmediate(pixels);
        }
    }

    private static void PersistManifest()
    {
        string json = JsonUtility.ToJson(manifest, true);
        SessionState.SetString(Key + "Manifest", json);
        File.WriteAllText(Path.Combine(SessionState.GetString(Key + "Output", ""), "manifest.json"), json);
    }

    private static void Finish(bool success, string message)
    {
        SessionState.SetInt(Key + "Stage", 3);
        SessionState.SetInt(Key + "ExitCode", success ? 0 : 1);
        SessionState.SetString(Key + "Stopping", EditorApplication.timeSinceStartup.ToString("R", CultureInfo.InvariantCulture));
        if (success) Debug.Log("COURSE_VISUAL_TOUR_PASS: " + message);
        else Debug.LogError("COURSE_VISUAL_TOUR_FAIL: " + message);
        try
        {
            if (manifest == null) manifest = JsonUtility.FromJson<Manifest>(SessionState.GetString(Key + "Manifest", ""));
            if (manifest != null) { manifest.complete = success; manifest.error = success ? null : message; PersistManifest(); }
        }
        catch (Exception exception) { Debug.LogException(exception); SessionState.SetInt(Key + "ExitCode", 1); }
        ReleaseTarget();
        Time.timeScale = SessionState.GetFloat(Key + "TimeScale", 1f);
        Application.runInBackground = SessionState.GetBool(Key + "Background", false);
        EditorApplication.isPlaying = false;
    }

    private static void ReleaseTarget()
    {
        if (target == null) return;
        target.Release();
        UnityEngine.Object.DestroyImmediate(target);
        target = null;
    }

    private static void Exit()
    {
        SessionState.SetBool(Key + "Active", false);
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= StateChanged;
        EditorApplication.Exit(SessionState.GetInt(Key + "ExitCode", 1));
    }
}
