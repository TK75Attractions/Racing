using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>Opt-in standalone benchmark: set RACING_PERFORMANCE_OUTPUT before launching a Development Build.</summary>
public sealed class RacingStandalonePerformanceProbe : MonoBehaviour
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    [Serializable] private sealed class Metric { public string name; public double median, p95; }
    [Serializable] private sealed class Sample { public string phase; public int frames, focusedFrames; public float seconds; public List<Metric> metrics = new List<Metric>(); }
    [Serializable] private sealed class Report
    {
        public string unity, gpu, device, renderGraph, outputMode;
        public int physicalDisplays, windowWidth, windowHeight;
        public float meshLodThreshold;
        public float[] drivenMetres;
        public List<Sample> samples = new List<Sample>();
        public List<string> errors = new List<string>();
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
    private sealed class Counter { public string Name; public ProfilerRecorder Recorder; public List<double> Values = new List<double>(); public double Scale; }
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

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
#if DEVELOPMENT_BUILD
        if (Application.isEditor || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RACING_PERFORMANCE_OUTPUT"))) return;
        var root = new GameObject("Standalone performance probe");
        DontDestroyOnLoad(root);
        root.AddComponent<RacingStandalonePerformanceProbe>();
#endif
    }
    private void Awake()
    {
        installedAt = Time.realtimeSinceStartupAsDouble;
        output = Environment.GetEnvironmentVariable("RACING_PERFORMANCE_OUTPUT");
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
        Application.runInBackground = true; Application.targetFrameRate = -1; QualitySettings.vSyncCount = 0;
        report.unity = Application.unityVersion; report.gpu = SystemInfo.graphicsDeviceName; report.device = SystemInfo.processorType;
        var budget = FindFirstObjectByType<RaceRenderBudget>();
        if (budget != null) budget.enabled = false;
        report.physicalDisplays = Display.displays.Length;
        report.windowWidth = Screen.width; report.windowHeight = Screen.height;
        report.meshLodThreshold = QualitySettings.meshLodThreshold;
        Debug.developerConsoleVisible = false;
        report.renderGraph = (!GraphicsSettings.GetRenderPipelineSettings<RenderGraphSettings>().enableRenderCompatibilityMode).ToString();
        rigs = (PlayerDisplayRig[])typeof(Gmanager).GetField("displayRigs",Private).GetValue(manager);
        if (Display.displays.Length == 1)
        {
            report.outputMode = "Single physical display; P1 native window, P2 1920x1080 offscreen stack presented to Display 1";
            var cameras = Cameras(rigs[1]); foreach (Camera camera in cameras) camera.targetDisplay = 0;
            rigs[1].Canvas.targetDisplay = 0;
            presenter = rigs[1].CameraRoot.AddComponent<RenderGraphDisplayOutput>(); presenter.Configure(cameras,0);
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
        var available = new List<ProfilerRecorderHandle>(); ProfilerRecorderHandle.GetAvailable(available);
        foreach(var handle in available)
        {
            var description=ProfilerRecorderHandle.GetDescription(handle);
            if (description.Name=="Main Thread" || description.Name=="Render Thread" || description.Name=="GPU Frame Time" ||
                description.Name=="CPU Main Thread Frame Time" || description.Name=="CPU Render Thread Frame Time" ||
                description.Name=="Gfx.WaitForPresentOnGfxThread" || description.Name=="Gfx.WaitForRenderThread" ||
                description.Name=="Draw Calls Count" || description.Name=="Triangles Count" || description.Name=="GC Allocated In Frame" ||
                description.Name=="Racing.Display.Present")
            {
                var recorder=ProfilerRecorder.StartNew(description.Category,description.Name,1);
                counters.Add(new Counter { Name=description.Name, Recorder=recorder,
                    Scale=description.UnitType==ProfilerMarkerDataUnit.TimeNanoseconds?1e-6:1 });
            }
        }
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
    private void Finish()
    {
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
