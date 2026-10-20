using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Reflection;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>Opt-in Development Build benchmark: RACING_PERFORMANCE_OUTPUT or RACING_PERFORMANCE_AUTORUN=1.</summary>
public sealed class RacingStandalonePerformanceProbe : MonoBehaviour
{
    public static bool AutorunEnabled { get; } =
#if DEVELOPMENT_BUILD
        !Application.isEditor && Environment.GetEnvironmentVariable("RACING_PERFORMANCE_AUTORUN") == "1";
#else
        false;
#endif
    [Serializable] private sealed class RaceSummary
    {
        public int frames, focusedFrames, over33_3ms, over50ms;
        public double seconds, meanMs, medianMs, p95Ms, p99Ms, maxMs, first10SecondsMaxMs;
        public string endReason;
        public List<Spike> spikes = new List<Spike>();
    }
    [Serializable] private sealed class Spike
    {
        public double elapsedSeconds, frameMs;
        public float p1SpeedMps, p1CourseDistanceMetres;
        public int gc0Delta, gc1Delta, gc2Delta;
        public List<CounterReading> counters = new List<CounterReading>();
        public List<CounterReading> previousFrameCounters = new List<CounterReading>();
    }
    [Serializable] private sealed class CounterReading { public string name; public double value; }
    [Serializable] private sealed class CycleSnapshot
    {
        public int cycle, gameObjects, audioSources, textures, renderTextures, residualNotifications, settingsResidue;
        public long totalUsedMemoryBytes, monoHeapBytes;
        public long totalUsedMemoryDeltaBytes, monoHeapDeltaBytes;
        public int gameObjectsDelta, audioSourcesDelta, texturesDelta, renderTexturesDelta;
        public string settings;
    }
    [Serializable] private sealed class CycleReport
    {
        public int requestedCycles;
        public List<CycleSnapshot> cycles = new List<CycleSnapshot>();
        public List<string> warnings = new List<string>();
    }
    private struct RaceFrame
    {
        public double elapsed, ms;
        public float speed, distance;
        public int gc0, gc1, gc2;
        public long allocatedBytes;
    }
    // Keep disk I/O/CSV formatting out of the measured race. Preallocate for >500 fps over 240 s.
    private readonly List<RaceFrame> raceFrames = new List<RaceFrame>();
    private bool raceStarted, previousFrameRacing, finished;
    private double raceStart;
    private int previousGc0, previousGc1, previousGc2;
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    [Serializable] private sealed class Metric { public string name; public double median, p95; }
    [Serializable] private sealed class Sample { public string phase; public int frames, focusedFrames; public float seconds; public List<Metric> metrics = new List<Metric>(); }
    [Serializable] private sealed class Report
    {
        public string unity, gpu, device, renderGraph, outputMode;
        public int physicalDisplays, windowWidth, windowHeight;
        public int vSyncCount, targetFrameRate;
        public float meshLodThreshold;
        public float[] drivenMetres;
        public List<Sample> samples = new List<Sample>();
        public List<string> errors = new List<string>();
        public RaceSummary race;
        public List<string> availableCounters = new List<string>();
    }
    private sealed class Input : IDriveInputSource
    {
        public int PlayerIndex { get; set; }
        public string DeviceId => "standalone-performance";
        public bool IsConnected => true;
        public DriveInputState CurrentState { get; set; }
        public void UpdateInput(float deltaTime) { }
        public void Dispose() { }
    }
    private sealed class Driver
    {
        public Rigidbody Body;
        public Input Input;
        public float PreviousProgress, Distance;
    }
    private readonly List<Driver> drivers = new List<Driver>();
    private readonly Dictionary<Rigidbody, RigidbodyConstraints> constraints = new Dictionary<Rigidbody, RigidbodyConstraints>();
    private bool driving;
    private sealed class Counter { public string Name; public ProfilerRecorder Recorder; public List<double> Values = new List<double>(); public double Scale, PreviousValue; }
    private readonly List<Counter> counters = new List<Counter>();
    private readonly List<double> focusedTimes = new List<double>();
    private readonly List<double> scales = new List<double>();
    private readonly List<double> frames = new List<double>();
    private readonly Report report = new Report();
    private string output;
    private Gmanager manager;
    private PlayerDisplayRig[] rigs;
    private RenderGraphDisplayOutput presenter;
    private Action<ScriptableRenderContext,List<Camera>> presentationCallback;
    private double phaseStart, samplingStart, installedAt;
    private string phase;
    private bool sampling;
    private int focusedFrames;
    private int requestedCycles = 1;
    private readonly CycleReport cycleReport = new CycleReport();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
#if DEVELOPMENT_BUILD
        if (Application.isEditor || (!AutorunEnabled && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RACING_PERFORMANCE_OUTPUT")))) return;
        var root = new GameObject("Standalone performance probe");
        DontDestroyOnLoad(root);
        root.AddComponent<RacingStandalonePerformanceProbe>();
#endif
    }
    private void Awake()
    {
        installedAt = Time.realtimeSinceStartupAsDouble;
        output = Environment.GetEnvironmentVariable("RACING_PERFORMANCE_OUTPUT");
        if (AutorunEnabled && string.IsNullOrEmpty(output))
            output = Path.Combine(Application.temporaryCachePath, "RacingPerformance", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
        if (AutorunEnabled)
        {
            int parsed;
            if (int.TryParse(Environment.GetEnvironmentVariable("RACING_PERFORMANCE_CYCLES"), out parsed))
                requestedCycles = Mathf.Clamp(parsed, 1, 100);
        }
        cycleReport.requestedCycles = requestedCycles;
        if (string.IsNullOrEmpty(output)) { enabled = false; return; }
        Directory.CreateDirectory(output);
        Application.runInBackground = true;
        File.WriteAllText(Path.Combine(output,"status.txt"),"Awake");
        Application.logMessageReceived += Log;
    }
    private void Log(string message, string stack, LogType type)
    {
        if ((type == LogType.Error || type == LogType.Exception || type == LogType.Assert) &&
            !message.StartsWith("P2 Display ")) report.errors.Add(message);
    }
    [UnityEngine.Scripting.Preserve]
    private IEnumerator Start()
    {
        File.WriteAllText(Path.Combine(output,"status.txt"),"Start");
        yield return null;
        File.WriteAllText(Path.Combine(output,"status.txt"),"Scene ready");
        manager = Gmanager.Control;
        foreach (var publisher in UnityEngine.Object.FindObjectsByType<RaceRemoteFirebasePublisher>(FindObjectsSortMode.None)) publisher.enabled = false;
        Application.runInBackground = true;
        if (!AutorunEnabled) { Application.targetFrameRate = -1; QualitySettings.vSyncCount = 0; }
        report.unity = Application.unityVersion; report.gpu = SystemInfo.graphicsDeviceName; report.device = SystemInfo.processorType;
        var budget = FindFirstObjectByType<RaceRenderBudget>();
        if (budget != null && !AutorunEnabled) budget.enabled = false;
        report.physicalDisplays = Display.displays.Length;
        report.windowWidth = Screen.width; report.windowHeight = Screen.height;
        report.vSyncCount = QualitySettings.vSyncCount; report.targetFrameRate = Application.targetFrameRate;
        report.meshLodThreshold = QualitySettings.meshLodThreshold;
        Debug.developerConsoleVisible = false;
        report.renderGraph = (!GraphicsSettings.GetRenderPipelineSettings<RenderGraphSettings>().enableRenderCompatibilityMode).ToString();
        rigs = (PlayerDisplayRig[])typeof(Gmanager).GetField("displayRigs",Private).GetValue(manager);
        if (RacePlayerWindows.UsesNativeWindow)
        {
            report.outputMode = "P1 and P2 independent desktop windows";
            presenter = rigs[1].CameraRoot.GetComponent<RenderGraphDisplayOutput>();
        }
        else if (Display.displays.Length == 1)
        {
            report.outputMode = "Single physical display; P1 native window, P2 1920x1080 offscreen stack presented to Display 1";
            var cameras = Cameras(rigs[1]); foreach (Camera camera in cameras) camera.targetDisplay = 0;
            rigs[1].Canvas.targetDisplay = 0;
            presenter = rigs[1].CameraRoot.AddComponent<RenderGraphDisplayOutput>(); presenter.Configure(cameras,0,rigs[1].BackImageCamera);
            typeof(RenderGraphDisplayOutput).GetMethod("ResizeTarget",Private).Invoke(presenter,new object[]{1920,1080});
            rigs[1].MainCamera.GetComponent<FreezeAspectRate>().ConfigureForDisplay(0,rigs[1].BackCamera);
            rigs[1].MainCamera.GetComponent<FreezeAspectRate>().ConfigureCanvas(rigs[1].Canvas);
        }
        else
        {
            report.outputMode = "Physical display outputs from SampleScene";
            presenter = UnityEngine.Object.FindFirstObjectByType<RenderGraphDisplayOutput>();
        }
        if (presenter != null) presentationCallback = (Action<ScriptableRenderContext,List<Camera>>)Delegate.CreateDelegate(
            typeof(Action<ScriptableRenderContext,List<Camera>>),presenter,typeof(RenderGraphDisplayOutput).GetMethod("Present",Private));
        if (AutorunEnabled)
        {
            StartCounters();
            report.race = new RaceSummary();
            raceFrames.Capacity = 131072;
            yield return AutorunCycles();
            yield break;
        }
        typeof(Gmanager).GetMethod("StartGameWhenScreenCovered",Private).Invoke(manager,null);
        typeof(Gmanager).GetMethod("CompleteGameStart",Private).Invoke(manager,null);
        typeof(Gmanager).GetMethod("StartRaceAfterCountdown",Private).Invoke(manager,null);
        typeof(Gmanager).GetMethod("ApplyStateImmediate",Private).Invoke(manager,new object[]{Gmanager.State.Game});
        foreach(var transition in UnityEngine.Object.FindObjectsByType<ScreenTransitionController>(FindObjectsSortMode.None)) transition.ClearRaceStatus();
        float raw = manager.course.GetProgressDistance(manager.GetParticipant(0).Body.position);
        foreach(var entry in manager.Participants)
        {
            manager.course.TryGetSampleAtProgress(raw+(entry.IsCpu?manager.CourseProgressSign*14:0),out var sample);
            constraints.Add(entry.Body, entry.Body.constraints);
            entry.Body.constraints = RigidbodyConstraints.FreezeAll; entry.Body.interpolation = RigidbodyInterpolation.None;
            Vector3 position = sample.position + sample.up*.5f + sample.right*(entry.Index%2==0?-2:2);
            entry.Car.transform.SetPositionAndRotation(position,Quaternion.LookRotation(sample.forward*manager.CourseProgressSign,sample.up));
            entry.Body.position=position; entry.Body.rotation=entry.Car.transform.rotation;
        }
        Physics.SyncTransforms();
        StartCounters();
        yield return new WaitForSecondsRealtime(2f);
        bool baseline = Environment.GetEnvironmentVariable("RACING_PERFORMANCE_BASELINE") == "1";
        bool single = Environment.GetEnvironmentVariable("RACING_PERFORMANCE_SINGLE_SAMPLE") == "1";
        if (baseline)
        {
            ((UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline).renderScale = 1f;
            foreach (var renderer in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)) renderer.forceMeshLod = 0;
        }
        var inventory = new List<string>();
        foreach (var filter in FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
        {
            var mesh = filter.sharedMesh; var renderer = filter.GetComponent<Renderer>();
            if (mesh == null || renderer == null || !renderer.enabled) continue;
            ulong triangles = 0; for (int sub = 0; sub < mesh.subMeshCount; sub++) triangles += (mesh.lodCount > 0 ? mesh.GetLod(sub, 0).indexCount : mesh.GetIndexCount(sub)) / 3;
            bool visible = false; foreach (var rig in rigs) visible |= GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(rig.MainCamera), renderer.bounds);
            string path = filter.name; for (var parent = filter.transform.parent; parent != null; parent = parent.parent) path = parent.name + "/" + path;
            inventory.Add(triangles + "\t" + mesh.lodCount + "\t" + visible + "\t" + mesh.name + "\t" + path);
        }
        File.WriteAllLines(Path.Combine(output,"meshes.tsv"),inventory);
        yield return Measure(baseline ? "baseline" : "optimized", single ? 8f : 4f);
        if (single) { Finish(); yield break; }
        var renderers = FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
        foreach (var renderer in renderers) renderer.forceMeshLod = 0;
        yield return Measure("without-mesh-lods");
        foreach (var renderer in renderers) renderer.forceMeshLod = -1;
        foreach (var renderer in renderers)
        {
            var filter = renderer.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null && filter.sharedMesh.lodCount > 1) renderer.forceMeshLod = 1;
        }
        yield return Measure("force-lod1");
        foreach (var renderer in renderers) renderer.forceMeshLod = -1;
        foreach (var renderer in renderers)
        {
            var filter = renderer.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null && filter.sharedMesh.name.StartsWith("tree") && filter.sharedMesh.lodCount > 3) renderer.forceMeshLod = 3;
        }
        yield return Measure("tree-lod3");
        foreach (var renderer in renderers) renderer.forceMeshLod = -1;
        var pipeline = (UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;
        float scale = pipeline.renderScale; pipeline.renderScale = .8f;
        yield return Measure("scale80"); pipeline.renderScale = .65f;
        yield return Measure("scale65"); pipeline.renderScale = scale;
        yield return Measure("optimized-repeat");
        if (budget != null)
        {
            budget.enabled = true;
            yield return Measure("adaptive-static", 12f);
        }
        if (Environment.GetEnvironmentVariable("RACING_PERFORMANCE_DRIVE") == "1")
        {
            foreach (var entry in manager.Participants)
            {
                entry.Body.constraints = constraints[entry.Body];
                entry.Body.interpolation = RigidbodyInterpolation.Interpolate;
                if (entry.IsCpu) continue;
                var input = new Input { PlayerIndex = entry.Index };
                entry.Car.GetComponent<DebugMover>().SetInputSource(input);
                drivers.Add(new Driver { Body = entry.Body, Input = input,
                    PreviousProgress = manager.course.GetProgressDistance(entry.Body.position) });
            }
            driving = true;
            yield return Measure("driving", 60f);
            driving = false;
            report.drivenMetres = new float[drivers.Count];
            for (int i = 0; i < drivers.Count; i++)
            {
                report.drivenMetres[i] = drivers[i].Distance;
                if (drivers[i].Distance < 600f) report.errors.Add("Player " + i + " did not drive 600 metres.");
                drivers[i].Input.CurrentState = DriveInputState.Neutral;
            }
        }
        Finish();
    }
    private void StartCounters()
    {
        var available = new List<ProfilerRecorderHandle>(); ProfilerRecorderHandle.GetAvailable(available);
        foreach(var handle in available)
        {
            var description=ProfilerRecorderHandle.GetDescription(handle);
            if (description.Name=="Main Thread" || description.Name=="Render Thread" || description.Name=="GPU Frame Time" ||
                description.Name=="CPU Main Thread Frame Time" || description.Name=="CPU Render Thread Frame Time" ||
                description.Name=="Gfx.WaitForPresentOnGfxThread" || description.Name=="Gfx.WaitForRenderThread" ||
                description.Name=="Draw Calls Count" || description.Name=="Triangles Count" || description.Name=="GC Allocated In Frame" ||
                description.Name=="Racing.Display.Present" || description.Name=="Game Object Count" ||
                description.Name=="Object Count" || description.Name=="Total Used Memory")
            {
                var recorder=ProfilerRecorder.StartNew(description.Category,description.Name,1);
                counters.Add(new Counter { Name=description.Name, Recorder=recorder,
                    Scale=description.UnitType==ProfilerMarkerDataUnit.TimeNanoseconds?1e-6:1 });
                if (recorder.Valid) report.availableCounters.Add(description.Name);
            }
        }
    }
    private IEnumerator AutorunCycles()
    {
        for (int cycle = 1; cycle <= requestedCycles && !finished; cycle++)
        {
            double cycleStart = Time.realtimeSinceStartupAsDouble;
            bool raceSummaryWritten = false;
            Gmanager.State observedState = manager != null ? manager.state : Gmanager.State.Title;
            double stateSince = Time.realtimeSinceStartupAsDouble;
            raceStarted = false;
            previousFrameRacing = false;
            raceFrames.Clear();
            report.race = new RaceSummary();
            while (!finished && Time.realtimeSinceStartupAsDouble - cycleStart < 600)
            {
                if (manager == null) { yield return null; continue; }
                if (manager.state != observedState)
                {
                    observedState = manager.state;
                    stateSince = Time.realtimeSinceStartupAsDouble;
                }
                File.WriteAllText(Path.Combine(output, "status.txt"), "Autorun cycle " + cycle + ": " + manager.state);
                if (manager.CanUseDashboardControls)
                {
                    if (manager.state == Gmanager.State.Title && raceSummaryWritten)
                    {
                        // Reset completes after the result transition; sample only after deferred destroys settle.
                        yield return null;
                        yield return null;
                        CaptureCycle(cycle);
                        break;
                    }
                    else if (Time.realtimeSinceStartupAsDouble - stateSince >= 3 && manager.state == Gmanager.State.Title) manager.StartGame();
                    else if (Time.realtimeSinceStartupAsDouble - stateSince >= 3 && manager.state == Gmanager.State.CarSelection) manager.AdvanceDashboardToTutorial();
                    else if (Time.realtimeSinceStartupAsDouble - stateSince >= 3 && manager.state == Gmanager.State.Tutorial) manager.AdvanceDashboardToCountdown();
                    else if (manager.state == Gmanager.State.Result)
                    {
                        if (!raceSummaryWritten)
                        {
                            FinishRace(GetRaceEndReason(), false);
                            raceSummaryWritten = true;
                            yield return new WaitForSecondsRealtime(3f);
                        }
                        else
                        {
                            manager.SelectResultOption(0, 1);
                            manager.SelectResultOption(1, 1);
                        }
                    }
                }
                yield return new WaitForSecondsRealtime(1f);
            }
            if (!raceSummaryWritten && manager != null && manager.state == Gmanager.State.Result)
            {
                FinishRace(GetRaceEndReason(), false);
                raceSummaryWritten = true;
            }
            if (!raceSummaryWritten)
            {
                report.errors.Add("Autorun cycle " + cycle + " did not reach results within 600 seconds.");
                break;
            }
            if (cycle < requestedCycles)
            {
                // Allow title transitions to settle before beginning the next group.
                while (manager != null && manager.state != Gmanager.State.Title && Time.realtimeSinceStartupAsDouble - cycleStart < 600)
                    yield return new WaitForSecondsRealtime(.25f);
            }
        }
        WriteCycleReport();
        Finish();
    }

    private void CaptureCycle(int cycle)
    {
        long totalMemory = Profiler.GetTotalAllocatedMemoryLong();
        foreach (var counter in counters)
            if (counter.Name == "Total Used Memory" && counter.Recorder.Valid)
                totalMemory = counter.Recorder.LastValue;
        var snapshot = new CycleSnapshot {
            cycle = cycle, totalUsedMemoryBytes = totalMemory, monoHeapBytes = Profiler.GetMonoUsedSizeLong(),
            gameObjects = FindObjectsByType<GameObject>(FindObjectsSortMode.None).Length,
            audioSources = FindObjectsByType<AudioSource>(FindObjectsSortMode.None).Length,
            textures = Resources.FindObjectsOfTypeAll<Texture>().Length,
            renderTextures = Resources.FindObjectsOfTypeAll<RenderTexture>().Length,
            residualNotifications = CountResidualNotifications(),
            settingsResidue = manager.Features != null && (manager.Features.Settings.RulesLocked || manager.Features.Settings.HasQueuedPreset) ? 1 : 0,
            settings = manager.Features == null ? "unavailable" : manager.Features.Settings.Enabled + "/" + manager.Features.Settings.SelectedPreset + "/" + manager.Features.Settings.ActivePreset
        };
        if (cycleReport.cycles.Count > 0)
        {
            CycleSnapshot previous = cycleReport.cycles[cycleReport.cycles.Count - 1];
            snapshot.totalUsedMemoryDeltaBytes = snapshot.totalUsedMemoryBytes - previous.totalUsedMemoryBytes;
            snapshot.monoHeapDeltaBytes = snapshot.monoHeapBytes - previous.monoHeapBytes;
            snapshot.gameObjectsDelta = snapshot.gameObjects - previous.gameObjects;
            snapshot.audioSourcesDelta = snapshot.audioSources - previous.audioSources;
            snapshot.texturesDelta = snapshot.textures - previous.textures;
            snapshot.renderTexturesDelta = snapshot.renderTextures - previous.renderTextures;
        }
        cycleReport.cycles.Add(snapshot);
        WriteCycleReport(true);
    }

    private string GetRaceEndReason()
    {
        bool allFinished = manager != null && manager.Participants.Count > 0;
        if (manager != null) foreach (var entry in manager.Participants) allFinished &= entry.Result?.didFinish == true;
        return allFinished ? "all-finished" : "race-timeout-or-dnf";
    }

    private int CountResidualNotifications()
    {
        int count = 0;
        var field = typeof(Gmanager).GetField("onPlayUIManagers", Private);
        var uis = field != null ? field.GetValue(manager) as OnPlayUIManager[] : null;
        if (uis != null) foreach (var ui in uis) if (ui != null && !string.IsNullOrEmpty(ui.DriverNotice)) count++;
        return count;
    }

    private void WriteCycleReport(bool logWarnings = false)
    {
        cycleReport.warnings.Clear();
        var cycles = cycleReport.cycles;
        if (cycles.Count >= 3)
        {
            for (int i = 2; i < cycles.Count; i++)
            {
                WarnIfIncreasing(cycles, i, "totalUsedMemoryBytes", x => x.totalUsedMemoryBytes);
                WarnIfIncreasing(cycles, i, "monoHeapBytes", x => x.monoHeapBytes);
                WarnIfIncreasing(cycles, i, "gameObjects", x => x.gameObjects);
                WarnIfIncreasing(cycles, i, "audioSources", x => x.audioSources);
                WarnIfIncreasing(cycles, i, "textures", x => x.textures);
                WarnIfIncreasing(cycles, i, "renderTextures", x => x.renderTextures);
            }
        }
        foreach (CycleSnapshot snapshot in cycles)
        {
            if (snapshot.residualNotifications > 0) cycleReport.warnings.Add("Cycle " + snapshot.cycle + ": " + snapshot.residualNotifications + " driver notification(s) remained at title.");
            if (snapshot.settingsResidue > 0) cycleReport.warnings.Add("Cycle " + snapshot.cycle + ": race settings remained locked or queued at title.");
        }
        File.WriteAllText(Path.Combine(output, "cycles.json"), JsonUtility.ToJson(cycleReport, true));
        if (logWarnings)
            foreach (string warning in cycleReport.warnings) Debug.LogWarning("PERFORMANCE_CYCLE_LEAK_SUSPECT: " + warning);
    }

    private void WarnIfIncreasing(List<CycleSnapshot> cycles, int index, string metric, Func<CycleSnapshot, long> read)
    {
        if (index < 2) return;
        long a = read(cycles[index - 2]), b = read(cycles[index - 1]), c = read(cycles[index]);
        if (a < b && b < c) cycleReport.warnings.Add("Cycle " + cycles[index - 2].cycle + " to " + cycles[index].cycle + ": " + metric + " increased monotonically (" + a + " -> " + b + " -> " + c + ").");
    }

    private void WarnIfIncreasing(List<CycleSnapshot> cycles, int index, string metric, Func<CycleSnapshot, int> read)
    {
        if (index < 2) return;
        int a = read(cycles[index - 2]), b = read(cycles[index - 1]), c = read(cycles[index]);
        if (a < b && b < c) cycleReport.warnings.Add("Cycle " + cycles[index - 2].cycle + " to " + cycles[index].cycle + ": " + metric + " increased monotonically (" + a + " -> " + b + " -> " + c + ").");
    }

    private void LateUpdate()
    {
        if (!AutorunEnabled || finished || manager == null || report.race == null) return;
        double now = Time.realtimeSinceStartupAsDouble;
        // deltaTime and recorder LastValue describe the frame preceding this observation.
        // Keep the last racing frame even if Gmanager transitioned to Goal in this Update.
        if (previousFrameRacing)
        {
            var p1 = manager.GetParticipant(0);
            int gc0 = GC.CollectionCount(0), gc1 = GC.CollectionCount(1), gc2 = GC.CollectionCount(2);
            long allocated = -1;
            foreach (var counter in counters)
                if (counter.Name == "GC Allocated In Frame" && counter.Recorder.Valid) allocated = counter.Recorder.LastValue;
            var frame = new RaceFrame {
                elapsed = now - raceStart, ms = Time.unscaledDeltaTime * 1000.0,
                speed = p1?.Body != null ? p1.Body.linearVelocity.magnitude : 0f,
                distance = p1?.Body != null && manager.course != null ? manager.course.GetProgressDistance(p1.Body.position) : 0f,
                gc0 = gc0 - previousGc0, gc1 = gc1 - previousGc1, gc2 = gc2 - previousGc2,
                allocatedBytes = allocated
            };
            raceFrames.Add(frame);
            previousGc0 = gc0; previousGc1 = gc1; previousGc2 = gc2;
            if (Application.isFocused) report.race.focusedFrames++;
            if (frame.ms > 50)
            {
                var spike = new Spike { elapsedSeconds = frame.elapsed, frameMs = frame.ms,
                    p1SpeedMps = frame.speed, p1CourseDistanceMetres = frame.distance,
                    gc0Delta = frame.gc0, gc1Delta = frame.gc1, gc2Delta = frame.gc2 };
                foreach (var counter in counters)
                    if (counter.Recorder.Valid)
                    {
                        spike.counters.Add(new CounterReading {
                            name = counter.Name, value = counter.Recorder.LastValue * counter.Scale });
                        spike.previousFrameCounters.Add(new CounterReading {
                            name = counter.Name, value = counter.PreviousValue });
                    }
                report.race.spikes.Add(spike);
            }
        }
        bool racing = manager.state == Gmanager.State.Game;
        if (racing && !raceStarted)
        {
            raceStarted = true; raceStart = now;
            previousGc0 = GC.CollectionCount(0); previousGc1 = GC.CollectionCount(1); previousGc2 = GC.CollectionCount(2);
        }
        previousFrameRacing = racing;
        foreach (var counter in counters)
            if (counter.Recorder.Valid) counter.PreviousValue = counter.Recorder.LastValue * counter.Scale;
        if (raceStarted && !racing && !AutorunEnabled)
        {
            bool allFinished = manager.Participants.Count > 0;
            foreach (var entry in manager.Participants) allFinished &= entry.Result?.didFinish == true;
            FinishRace(allFinished ? "all-finished" : "race-timeout-or-dnf");
        }
        else if (raceStarted && !AutorunEnabled && now - raceStart >= 240) FinishRace("240-second-limit");
    }

    private void FinishRace(string reason, bool finalize = true)
    {
        if (finished) return;
        var summary = report.race ?? (report.race = new RaceSummary());
        summary.endReason = reason; summary.frames = raceFrames.Count;
        summary.seconds = raceStarted ? Time.realtimeSinceStartupAsDouble - raceStart : 0;
        var times = new List<double>(raceFrames.Count);
        double total = 0;
        foreach (var frame in raceFrames)
        {
            times.Add(frame.ms); total += frame.ms;
            summary.maxMs = Math.Max(summary.maxMs, frame.ms);
            if (frame.elapsed - frame.ms / 1000 < 10) summary.first10SecondsMaxMs = Math.Max(summary.first10SecondsMaxMs, frame.ms);
            if (frame.ms > 33.3) summary.over33_3ms++;
            if (frame.ms > 50) summary.over50ms++;
        }
        times.Sort();
        if (times.Count > 0)
        {
            summary.meanMs = total / times.Count;
            summary.medianMs = (times[(times.Count - 1) / 2] + times[times.Count / 2]) / 2;
            summary.p95Ms = times[Math.Max(0, (int)Math.Ceiling(times.Count * .95) - 1)];
            summary.p99Ms = times[Math.Max(0, (int)Math.Ceiling(times.Count * .99) - 1)];
        }
        else report.errors.Add("No racing frames captured.");
        using (var writer = new StreamWriter(Path.Combine(output, "frames.csv")))
        {
            writer.WriteLine("elapsed_seconds,frame_ms,p1_speed_mps,p1_course_distance_metres,gc0_delta,gc1_delta,gc2_delta,gc_allocated_bytes");
            foreach (var frame in raceFrames)
                writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0:F6},{1:F6},{2:F4},{3:F4},{4},{5},{6},{7}",
                    frame.elapsed, frame.ms, frame.speed, frame.distance, frame.gc0, frame.gc1, frame.gc2, frame.allocatedBytes));
        }
        File.WriteAllText(Path.Combine(output, "status.txt"), "Finished: " + reason);
        if (finalize) Finish();
    }

    private void Finish()
    {
        if (finished) return;
        finished = true;
        foreach(var counter in counters)counter.Recorder.Dispose(); counters.Clear();
        File.WriteAllText(Path.Combine(output,"report.json"),JsonUtility.ToJson(report,true));
        Debug.Log("STANDALONE_PERFORMANCE_PASS: "+output);
        Application.Quit(report.errors.Count==0?0:1);
    }
    private static Camera[] Cameras(PlayerDisplayRig rig)=>new[]{rig.BackCamera,rig.BackImageCamera,rig.MainCamera,rig.FrontCamera,rig.UiCamera};
    private IEnumerator Measure(string name, float duration = 4f)
    {
        phase=name; phaseStart=Time.realtimeSinceStartupAsDouble; sampling=false;
        yield return new WaitForSecondsRealtime(2f);
        frames.Clear(); scales.Clear(); focusedTimes.Clear(); focusedFrames = 0; foreach(var counter in counters)counter.Values.Clear();
        samplingStart=Time.realtimeSinceStartupAsDouble; sampling=true;
        File.WriteAllText(Path.Combine(output,"status.txt"),"Sampling "+name);
        while(Time.realtimeSinceStartupAsDouble-samplingStart<duration || frames.Count<40) yield return null;
        sampling=false;
        ScreenCapture.CaptureScreenshot(Path.Combine(output,name+".png"));
        var sample=new Sample{phase=name,frames=frames.Count,focusedFrames=focusedFrames,seconds=(float)(Time.realtimeSinceStartupAsDouble-samplingStart)};
        sample.metrics.Add(Summarize("frame-ms",frames));
        sample.metrics.Add(Summarize("focused-frame-ms",focusedTimes));
        sample.metrics.Add(Summarize("render-scale",scales));
        foreach(var counter in counters)sample.metrics.Add(Summarize(counter.Name,counter.Values));
        report.samples.Add(sample);
        File.WriteAllText(Path.Combine(output,"progress.json"),JsonUtility.ToJson(report,true));
        Debug.Log("PERFORMANCE_SAMPLE: "+name+" "+sample.metrics[0].median.ToString("F2")+" ms");
    }
    private void Update()
    {
        if (AutorunEnabled)
        {
            if (!finished && Time.realtimeSinceStartupAsDouble - installedAt >= requestedCycles * 600.0)
            {
                report.errors.Add("Autorun exceeded the overall cycle deadline.");
                WriteCycleReport(true);
                Finish();
            }
            return;
        }
        if (Time.realtimeSinceStartupAsDouble - installedAt > 180)
        { File.WriteAllText(Path.Combine(output,"timeout.txt"),"Preparation/run timeout: " + phase); Application.Quit(1); }
        if (Time.realtimeSinceStartupAsDouble-phaseStart>90 && !string.IsNullOrEmpty(phase))
        { File.WriteAllText(Path.Combine(output,"timeout.txt"),phase); Application.Quit(1); }
        if (!sampling) return;
        if (Application.isFocused) { focusedFrames++; focusedTimes.Add(Time.unscaledDeltaTime * 1000); }
        frames.Add(Time.unscaledDeltaTime*1000);
        scales.Add(((UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline).renderScale);
        foreach(var counter in counters)counter.Values.Add(counter.Recorder.LastValue*counter.Scale);
    }
    private void FixedUpdate()
    {
        if (!driving) return;
        foreach (var driver in drivers)
        {
            var body = driver.Body;
            var course = manager.course;
            float sign = manager.CourseProgressSign;
            float progress = course.GetProgressDistance(body.position);
            driver.Distance += Mathf.Repeat(sign * (progress - driver.PreviousProgress) + course.TotalLength * .5f, course.TotalLength) - course.TotalLength * .5f;
            driver.PreviousProgress = progress;
            float speed = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up).magnitude;
            float lookAhead = Mathf.Clamp(5f + speed * .4f, 8f, 17f);
            course.TryGetSampleAtProgress(progress + sign * lookAhead, out var target);
            course.TryGetSampleAtProgress(progress + sign * 25f, out var far);
            float targetSpeed = Vector3.Angle(target.forward, far.forward) > 15f ? 14f : 25f;
            Vector3 aim = target.position + target.right * (driver.Input.PlayerIndex == 0 ? -2f : 2f) - body.position;
            Vector3 local = Quaternion.Inverse(body.rotation) * aim;
            float angle = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            driver.Input.CurrentState = new DriveInputState {
                steering = Mathf.Clamp(angle * 1.5f - body.angularVelocity.y * 8f, -30f, 30f),
                pedal = Mathf.Clamp((targetSpeed - speed) * .12f, -1f, 1f)
            };
        }
    }
    private static Metric Summarize(string name,List<double> values)
    {
        values.Sort(); return new Metric{name=name,median=values.Count>0?values[values.Count/2]:0,
            p95=values.Count>0?values[Math.Min(values.Count-1,(int)(values.Count*.95))]:0};
    }
    private void OnDestroy(){Application.logMessageReceived-=Log;foreach(var counter in counters)counter.Recorder.Dispose();}
}
