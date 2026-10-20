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
        public string file, location, state;
        public float requestedSpeedMetersPerSecond;
        public float distanceMeters, actualDistanceMeters;
        public Vector3 placedPosition, position, forward;
        public Quaternion placedRotation, rotation;
        public Vector3 cameraPosition;
        public Quaternion cameraRotation;
        public float speedMetersPerSecond;
        public List<CourseResolutionComparison.Image> images = new List<CourseResolutionComparison.Image>();
    }

    [Serializable] private sealed class Manifest
    {
        public string scene = ScenePath;
        public string mode = "tour", variant = "full", contactSheet;
        public List<FixedPoint> fixedPoints = new List<FixedPoint>();
        public List<string> skipped = new List<string>();
        public float courseLengthMeters, spacingMeters, directionSign;
        public bool complete;
        public string error;
        public List<Vector2Int> resolutions = new List<Vector2Int>();
        public float captureRenderScale;
        public string resolutionSheet;
        public List<Frame> frames = new List<Frame>();
    }

    [Serializable] private sealed class FixedPoint
    {
        public string name;
        // Raw RaceCourse progress in metres (not race-direction distance).
        public float distanceMeters;
        public FixedPoint(string name, float distance) { this.name = name; distanceMeters = distance; }
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
            manifest = new Manifest
            {
                mode = ReadOption("RACING_VISUAL_TOUR_MODE", "tour", "tour", "fixed"),
                variant = ReadOption("RACING_VISUAL_VARIANT", "full", "day", "night-base", "full")
            };
            manifest.resolutions = CourseResolutionComparison.ReadResolutions();
            if (manifest.resolutions.Count > 1 && manifest.mode != "fixed")
                throw new ArgumentException("Multiple RACING_VISUAL_RESOLUTIONS require fixed mode.");
            manifest.captureRenderScale = CourseResolutionComparison.ReadScale();
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
            if (manifest == null)
                manifest = JsonUtility.FromJson<Manifest>(SessionState.GetString(Key + "Manifest", ""));
            if (manifest == null) throw new InvalidOperationException("Visual tour manifest was lost.");
            ApplyVariant();
            if (stage >= 4) { TickExtra(manager, stage); return; }
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
            manifest.courseLengthMeters = course.TotalLength;
            int count = Mathf.CeilToInt(course.TotalLength / Spacing);
            manifest.spacingMeters = course.TotalLength / count;
            var laps = UnityEngine.Object.FindFirstObjectByType<LapManager>();
            manifest.directionSign = laps != null && laps.IsProgressReversed ? -1f : 1f;
            bool fixedMode = manifest.mode == "fixed";
            if (fixedMode && manifest.fixedPoints.Count == 0)
            {
                BuildFixedPoints(manager, course);
                PersistManifest();
            }
            if (fixedMode) count = manifest.fixedPoints.Count * 2;
            if (pending == null && manifest.frames.Count >= count)
            {
                if (fixedMode) BeginExtra(4);
                else Finish(true, "Captured " + count + " P1 course views.");
                return;
            }

            if (pending == null)
            {
                // Redistribute the remainder, including the closing interval, over the full lap.
                float distance = manifest.frames.Count * manifest.spacingMeters;
                var point = fixedMode ? manifest.fixedPoints[manifest.frames.Count / 2] : null;
                bool moving = !fixedMode || manifest.frames.Count % 2 == 1;
                float progress = fixedMode ? point.distanceMeters : Mathf.Repeat(manifest.directionSign * distance, course.TotalLength);
                float placement = fixedMode && moving
                    ? Mathf.Repeat(progress - manifest.directionSign * Speed * SettleSeconds, course.TotalLength) : progress;
                if (!course.TryGetSampleAtProgress(placement, out var sample))
                    throw new InvalidOperationException("Cannot sample course at " + progress);
                if (!resetter.TryResetToRoadSample(sample))
                    throw new InvalidOperationException("Cannot place P1 at " + progress);
                if (fixedMode) body.constraints = moving ? RigidbodyConstraints.None : RigidbodyConstraints.FreezeAll;
                body.linearVelocity = car.forward * (moving ? Speed : 0f);
                body.angularVelocity = Vector3.zero;
                body.WakeUp();
                pending = new Frame
                {
                    file = fixedMode ? point.name + (moving ? "-moving.png" : "-still.png") :
                        (manifest.frames.Count + 1).ToString("D2") + "-" + Mathf.RoundToInt(distance).ToString(CultureInfo.InvariantCulture) + ".png",
                    location = fixedMode ? point.name : "tour", state = manager.state.ToString(),
                    requestedSpeedMetersPerSecond = moving ? Speed : 0f,
                    distanceMeters = fixedMode ? progress : distance, placedPosition = body.position, placedRotation = body.rotation
                };
                placedTime = Time.time;
                placedFrame = Time.frameCount;
                SessionState.SetInt(Key + "Stage", 2);
                return;
            }
            if (renderedFrame == Time.frameCount) return;
            if (fixedMode && pending.requestedSpeedMetersPerSecond > 0f)
            {
                // A continuous centre-line approach ends at the named point at shutter time.
                // Do not respawn each tick: the real follow camera and motion history must run.
                float remaining = Mathf.Max(0f, SettleSeconds - (Time.time - placedTime));
                float progress = Mathf.Repeat(pending.distanceMeters - manifest.directionSign * Speed * remaining, course.TotalLength);
                if (!course.TryGetSampleAtProgress(progress, out var sample))
                    throw new InvalidOperationException("Cannot sample fixed moving approach.");
                float clearance = Vector3.Distance(pending.placedPosition,
                    course.GetNearestPointOnCenterLineWorld(pending.placedPosition));
                body.rotation = Quaternion.LookRotation(sample.forward * manifest.directionSign, sample.up);
                body.position = sample.position + sample.up * clearance;
                body.linearVelocity = body.rotation * Vector3.forward * Speed;
                body.angularVelocity = Vector3.zero;
                Physics.SyncTransforms();
            }
            Camera camera = manager.GetPlayerCaptureCamera(0);
            Camera view = manager.GetPlayerViewCamera(0);
            if (camera == null || view == null) throw new InvalidOperationException("P1 camera stack is unavailable.");
            // Preserve all existing night volumes, renderer effects and camera settings. Fail rather than silently omit post FX.
            if (!view.GetUniversalAdditionalCameraData().renderPostProcessing)
                throw new InvalidOperationException("P1 view camera has post processing disabled.");
            RenderPlayer(manager, camera);
            renderedFrame = Time.frameCount;
            if (Time.time - placedTime < SettleSeconds || Time.frameCount - placedFrame < 6) return;
            SaveFrame(manager, pending);
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
            if (manifest.frames.Count >= count)
            {
                if (fixedMode) BeginExtra(4);
                else Finish(true, "Captured " + count + " P1 course views.");
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Finish(false, exception.Message);
        }
    }

    private static string ReadOption(string variable, string fallback, params string[] allowed)
    {
        string value = Environment.GetEnvironmentVariable(variable);
        value = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().ToLowerInvariant();
        if (Array.IndexOf(allowed, value) < 0)
            throw new ArgumentException(variable + " must be one of: " + string.Join(", ", allowed));
        return value;
    }

    private static void ApplyVariant()
    {
        if (manifest.variant == "full") return;
        foreach (var environment in UnityEngine.Object.FindObjectsByType<RaceEnvironmentController>(FindObjectsSortMode.None))
        {
            var time = manifest.variant == "day" ? RaceEnvironmentController.TimeOfDay.Day : RaceEnvironmentController.TimeOfDay.Night;
            if (environment.CurrentTimeOfDay != time) environment.SetSituation(environment.CurrentWeather, time);
        }
        foreach (var glow in UnityEngine.Object.FindObjectsByType<CarNightGlow>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            glow.enabled = false;
        // Repeat for late-created practice cars and regenerated skyline objects.
        foreach (var transform in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if ((transform.name == "Lanterns" && transform.parent != null && transform.parent.name == "NightEnnichi") ||
                (manifest.variant == "night-base" && transform.name == "Night Skyline (generated)"))
                transform.gameObject.SetActive(false);
    }

    private static void BuildFixedPoints(Gmanager manager, RaceCourse course)
    {
        float length = course.TotalLength, sign = manifest.directionSign;
        var startMethod = typeof(Gmanager).GetMethod("GetStartPoint", Private);
        var start = startMethod?.Invoke(manager, null) as Transform;
        if (start == null) throw new InvalidOperationException("Start checkpoint is unavailable.");
        float origin = course.GetProgressDistance(start.position);
        Transform school = null;
        foreach (var candidate in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            if (candidate.name.StartsWith("1016こうしゃ", StringComparison.Ordinal)) { school = candidate; break; }
        if (school == null) throw new InvalidOperationException("School model 1016こうしゃ is unavailable.");
        var renderers = school.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) throw new InvalidOperationException("School model has no renderer bounds.");
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

        // Sample in race direction from an outside point, so a tube crossing raw progress zero
        // remains one section. Use the longest contiguous interior section, not its AABB.
        int steps = Mathf.CeilToInt(length / .5f);
        float step = length / steps, outside = origin;
        bool Inside(float distance)
        {
            return course.TryGetSampleAtProgress(Mathf.Repeat(distance, length), out var sample) &&
                course.IsPointInsideTube(sample.position + sample.up);
        }
        int search = 0;
        while (search < steps && Inside(outside)) { outside += sign * step; search++; }
        if (search == steps) throw new InvalidOperationException("Cannot find a point outside the tube.");
        float entry = 0f, bestEntry = 0f, bestExit = 0f, bestLength = 0f;
        bool wasInside = false;
        for (int i = 1; i <= steps; i++)
        {
            float distance = i * step;
            bool inside = Inside(outside + sign * distance);
            if (inside && !wasInside) entry = distance - step * .5f;
            if (!inside && wasInside && distance - step * .5f - entry > bestLength)
            {
                bestLength = distance - step * .5f - entry;
                bestEntry = outside + sign * entry;
                bestExit = outside + sign * (distance - step * .5f);
            }
            wasInside = inside;
        }
        if (bestLength <= 0f) throw new InvalidOperationException("No tube section found on the centre line.");
        var jumps = new List<RaceCourse.JumpSection>();
        course.CopyJumpSections(jumps, sign);
        if (jumps.Count == 0) throw new InvalidOperationException("No jump section found.");
        jumps.Sort((a, b) => Mathf.Repeat(sign * (a.takeoff - origin), length)
            .CompareTo(Mathf.Repeat(sign * (b.takeoff - origin), length)));
        float sharpest = 0f, maxCurvature = -1f;
        for (int i = 0; i < steps; i++)
        {
            float distance = i * step;
            if (!course.TryGetSampleAtProgress(Mathf.Repeat(distance - 2f, length), out var before) ||
                !course.TryGetSampleAtProgress(Mathf.Repeat(distance + 2f, length), out var after)) continue;
            float curvature = Vector3.Angle(Vector3.ProjectOnPlane(before.forward, Vector3.up),
                Vector3.ProjectOnPlane(after.forward, Vector3.up)) / 4f;
            if (curvature > maxCurvature) { maxCurvature = curvature; sharpest = distance; }
        }
        // Named distance table. Every offset is in race direction; stored values are raw metres.
        manifest.fixedPoints = new List<FixedPoint>
        {
            new FixedPoint("01-start-grid", origin + sign * 10f),
            new FixedPoint("02-school-front", course.GetProgressDistance(bounds.center)),
            new FixedPoint("03-tube-entry", bestEntry - sign * 20f),
            new FixedPoint("04-tube-inner", bestEntry + sign * bestLength * .5f),
            new FixedPoint("05-tube-exit", bestExit + sign * 10f),
            new FixedPoint("06-jump-approach", jumps[0].takeoff - sign * 25f),
            new FixedPoint("07-sharp-curve", sharpest - sign * 20f)
        };
        foreach (var point in manifest.fixedPoints) point.distanceMeters = Mathf.Repeat(point.distanceMeters, length);
    }

    private static void BeginExtra(int stage)
    {
        SessionState.SetInt(Key + "Stage", stage);
        SessionState.SetString(Key + "ExtraStarted", EditorApplication.timeSinceStartup.ToString("R", CultureInfo.InvariantCulture));
        SessionState.SetInt(Key + "ExtraFrame", Time.frameCount);
    }

    private static void TickExtra(Gmanager manager, int stage)
    {
        string file = stage <= 5 ? "08-practice.png" : "09-result.png";
        try
        {
            double elapsed = EditorApplication.timeSinceStartup - double.Parse(SessionState.GetString(Key + "ExtraStarted", "0"), CultureInfo.InvariantCulture);
            if (elapsed > 30d) throw new TimeoutException(file + ": state/camera did not become ready within 30 seconds.");
            if (stage == 4)
            {
                // Re-enter the real preparation flow after the race shots, without scene edits.
                InvokeStart(manager, "ResetGameWhenScreenCovered");
                foreach (var transition in UnityEngine.Object.FindObjectsByType<ScreenTransitionController>(FindObjectsSortMode.None))
                    transition.ApplyStateImmediate(Gmanager.State.Title);
                manager.StartGame();
                BeginExtra(5);
                return;
            }
            if (stage == 6)
            {
                if (!manager.CanUseDashboardControls) return;
                // End practice (including its independently-created HUD) and restore race cameras
                // before using the same result preview API as ResultUIPreviewMenu.
                InvokeStart(manager, "ClearCurrentRaceObjects");
                InvokeStart(manager, "StartGameWhenScreenCovered");
                InvokeStart(manager, "CompleteGameStart");
                foreach (var transition in UnityEngine.Object.FindObjectsByType<ScreenTransitionController>(FindObjectsSortMode.None))
                    transition.ApplyStateImmediate(Gmanager.State.Countdown);
                manager.DebugPreviewResult();
                BeginExtra(7);
                return;
            }
            if (stage == 5)
            {
                manager.AdvanceDashboardToTutorial();
                var tutorial = manager.GetComponent<DrivingTutorialController>();
                if (manager.GetPlayerPreparationState(0) != Gmanager.State.Tutorial || tutorial == null || !tutorial.HasStarted(0)) return;
            }
            else if (manager.state != Gmanager.State.Result) return;
            var rigs = (PlayerDisplayRig[])typeof(Gmanager).GetField("displayRigs", Private)?.GetValue(manager);
            var rig = rigs != null && rigs.Length > 0 ? rigs[0] : null;
            if (rig == null || rig.Transition == null || rig.Transition.IsTransitioning || elapsed < 2d ||
                Time.frameCount - SessionState.GetInt(Key + "ExtraFrame", Time.frameCount) < 6) return;
            Camera camera = manager.GetPlayerCaptureCamera(0);
            if (camera == null || renderedFrame == Time.frameCount) return;
            RenderPlayer(manager, camera);
            renderedFrame = Time.frameCount;

            var photographedCar = stage == 5 ? manager.GetComponent<DrivingTutorialController>()?.GetPlayerCar(0)?.transform : manager.GetPlayerCarTransform(0);
            var photographedBody = photographedCar != null ? photographedCar.GetComponent<Rigidbody>() : null;
            var view = manager.GetPlayerViewCamera(0);
            var extra = new Frame
            {
                file = file, location = stage == 5 ? "practice" : "result", state = manager.state.ToString(),
                position = photographedCar != null ? photographedCar.position : Vector3.zero,
                rotation = photographedCar != null ? photographedCar.rotation : Quaternion.identity,
                speedMetersPerSecond = photographedBody != null ? photographedBody.linearVelocity.magnitude : 0f,
                cameraPosition = view != null ? view.transform.position : Vector3.zero,
                cameraRotation = view != null ? view.transform.rotation : Quaternion.identity
            };
            SaveFrame(manager, extra);
            manifest.frames.Add(extra);
            PersistManifest();
        }
        catch (Exception exception)
        {
            manifest.skipped.Add(file + ": " + exception.Message);
            Debug.LogWarning("COURSE_VISUAL_TOUR_SKIP: " + file + ": " + exception.Message);
            PersistManifest();
        }
        if (stage <= 5) BeginExtra(6);
        else Finish(true, "Fixed views completed; optional screen failures are recorded in skipped.");
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

    private static PlayerDisplayRig GetRig(Gmanager manager)
    {
        var rigs = (PlayerDisplayRig[])typeof(Gmanager).GetField("displayRigs", Private)?.GetValue(manager);
        if (rigs == null || rigs.Length == 0) throw new InvalidOperationException("P1 display rig is unavailable.");
        return rigs[0];
    }

    private static void RenderPlayer(Gmanager manager, Camera camera)
    {
        RenderAtSize(manager, manifest.resolutions[0]);
    }

    private static void RenderAtSize(Gmanager manager, Vector2Int size)
    {
        if (target == null || target.width != size.x || target.height != size.y)
        {
            ReleaseTarget();
            target = new RenderTexture(size.x, size.y, 24, RenderTextureFormat.ARGB32) { hideFlags = HideFlags.DontSave };
            target.Create();
        }
        var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        float scale = pipeline != null ? pipeline.renderScale : 1f;
        try
        {
            if (pipeline != null && manifest.captureRenderScale > 0f) pipeline.renderScale = manifest.captureRenderScale;
            GetRig(manager).RenderToTarget(target);
        }
        finally { if (pipeline != null) pipeline.renderScale = scale; }
    }

    private static void SaveFrame(Gmanager manager, Frame frame)
    {
        // No player/physics/UI update between these synchronous renders: pose and HUD values match.
        for (int i = 0; i < manifest.resolutions.Count; i++)
        {
            Vector2Int size = manifest.resolutions[i];
            RenderAtSize(manager, size);
            string file = i == 0 ? frame.file : Path.GetFileNameWithoutExtension(frame.file) + "-" + size.x + "x" + size.y + ".png";
            SavePng(Path.Combine(SessionState.GetString(Key + "Output", ""), file));
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            frame.images.Add(new CourseResolutionComparison.Image
            {
                file = file, width = size.x, height = size.y,
                worldRenderScale = manifest.captureRenderScale > 0f ? manifest.captureRenderScale : pipeline != null ? pipeline.renderScale : 1f
            });
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

    private static void CreateContactSheet()
    {
        if (manifest.frames.Count == 0) return;
        const int width = 400, height = 225;
        int columns = Mathf.CeilToInt(Mathf.Sqrt(manifest.frames.Count));
        int rows = Mathf.CeilToInt((float)manifest.frames.Count / columns);
        var sheet = new Texture2D(columns * width, rows * height, TextureFormat.RGB24, false);
        var thumbnail = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var previous = RenderTexture.active;
        try
        {
            sheet.SetPixels32(new Color32[sheet.width * sheet.height]);
            string output = SessionState.GetString(Key + "Output", "");
            for (int i = 0; i < manifest.frames.Count; i++)
            {
                var source = new Texture2D(2, 2, TextureFormat.RGB24, false);
                try
                {
                    if (!ImageConversion.LoadImage(source, File.ReadAllBytes(Path.Combine(output, manifest.frames[i].file))))
                        throw new IOException("Cannot load " + manifest.frames[i].file);
                    source.filterMode = FilterMode.Bilinear;
                    Graphics.Blit(source, thumbnail);
                    RenderTexture.active = thumbnail;
                    sheet.ReadPixels(new Rect(0, 0, width, height), i % columns * width, (rows - 1 - i / columns) * height);
                }
                finally { UnityEngine.Object.DestroyImmediate(source); }
            }
            sheet.Apply();
            File.WriteAllBytes(Path.Combine(output, "contact-sheet.png"), sheet.EncodeToPNG());
            manifest.contactSheet = "contact-sheet.png";
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(thumbnail);
            UnityEngine.Object.DestroyImmediate(sheet);
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
        try
        {
            if (manifest == null) manifest = JsonUtility.FromJson<Manifest>(SessionState.GetString(Key + "Manifest", ""));
            if (manifest != null)
            {
                try
                {
                    CreateContactSheet();
                    if (manifest.resolutions.Count > 1)
                    {
                        var rows = new List<List<CourseResolutionComparison.Image>>();
                        foreach (var frame in manifest.frames) rows.Add(frame.images);
                        CourseResolutionComparison.CreateSheet(SessionState.GetString(Key + "Output", ""), manifest.resolutions, rows);
                        manifest.resolutionSheet = "resolution-sheet.png";
                    }
                }
                catch (Exception exception) { success = false; message = "Contact sheet: " + exception.Message; }
                manifest.complete = success;
                manifest.error = success ? null : message;
                SessionState.SetInt(Key + "ExitCode", success ? 0 : 1);
                PersistManifest();
            }
        }
        catch (Exception exception) { Debug.LogException(exception); SessionState.SetInt(Key + "ExitCode", 1); }
        if (SessionState.GetInt(Key + "ExitCode", 1) == 0) Debug.Log("COURSE_VISUAL_TOUR_PASS: " + message);
        else Debug.LogError("COURSE_VISUAL_TOUR_FAIL: " + message);
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
