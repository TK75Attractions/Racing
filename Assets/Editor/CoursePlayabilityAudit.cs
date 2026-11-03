// Graphics-enabled batchmode (do NOT pass -nographics or -quit):
// Unity.exe -batchmode -projectPath <project> -executeMethod CoursePlayabilityAudit.Run -logFile <log>
// The audit exits Unity itself after static measurement and Play Mode driving.
// COURSE_AUDIT_LANES="-0.75,0,0.75": fractions of road half-width along sample.right.
// COURSE_AUDIT_LAPS="1": full circuits per lane after acquisition; P1/P2 share a queue.
// COURSE_AUDIT_LANE_SECONDS="180": optional simulation deadline per lane (all requested laps).
// Default deadline: max(180, courseLength/8+60)*laps. Targets keep the vehicle 1m inside edges.
// Output: Artifacts/CoursePlayabilityAudit/. -nographics is known to crash entering Play Mode.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.LowLevel;
using Object = UnityEngine.Object;

/// <summary>Read-only scene audit. All generated geometry and driving instrumentation are transient.</summary>
public static class CoursePlayabilityAudit
{
    const string Output = "Artifacts/CoursePlayabilityAudit";
    const string Active = "CoursePlayabilityAudit.Active";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static RaceCourse course;
    static MeshCollider tube;
    static readonly List<Finding> findings = new List<Finding>();
    static readonly Dictionary<string, Finding> aggregates = new Dictionary<string, Finding>();
    static readonly Dictionary<int, Shape> shapes = new Dictionary<int, Shape>();
    static readonly List<string> limitations = new List<string>();
    static StreamWriter samples;
    static float length;
    static int sampleCount, colliderCount, rendererCount, excludedCount;
    static float maxWidthRate, maxCurvature, maxStep;
    static int missingBarrierSamples;
    static double began;
    static bool startedRace, bound, finishing;
    static float elapsed, direction = -1, startProgress = 58.5f;
    static string startReference = "fallback progress=58.5";
    static int lapsPerLane = 1, nextLane;
    static float laneSeconds, simulationLimit;
    static double wallLimit = 420;
    static readonly List<LaneRun> runs = new List<LaneRun>();
    sealed class LaneRun
    {
        public float lane, distance, onTargetDistance, began, maxSpeed, maxStall, maxError;
        public int collisions, barriers, excursions, decelerations, stops;
        public bool acquired;
        public string car = "", status = "未開始";
    }
    static readonly List<Driver> drivers = new List<Driver>();
    static StreamWriter trace;
    static StreamWriter contactTrace;

    [Serializable] sealed class Finding
    {
        public string kind, path, component, evidence, impact, lane = "";
        public int severity, count;
        public float distance, first, last, value, height, covered;
        public Vector3 position;
        public bool carBand;
    }
    sealed class Shape { public Vector3[] vertices; public int[] indices; public Bounds bounds; public bool fallback; public BoxCollider box; public bool boundsVolume; }
    [Serializable] sealed class AuditState
    {
        public List<Finding> rows;
        public List<string> notes;
        public float courseLength, startProgress, direction;
        public string startReference;
        public int samples, colliders, renderers, excluded;
    }
    sealed class AuditInput : IDriveInputSource
    {
        public int PlayerIndex { get; set; }
        public string DeviceId => "course-audit";
        public bool IsConnected => true;
        public DriveInputState CurrentState { get; set; }
        public void UpdateInput(float dt) { }
        public void Dispose() { }
    }
    sealed class Driver
    {
        public Rigidbody body;
        public DebugMover mover;
        public Collider[] colliders;
        public TireForce[] tires;
        public AuditInput input;
        public RoadObservationBot bot = new RoadObservationBot();
        public float previous, stalled, offTime, previousSpeed, halfWidth;
        public bool off, stuck, done;
        public LaneRun run;
        public Vector3 previousVelocity, previousAngularVelocity, previousCenter, previousPosition;
    }

    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode before starting the audit.");
        sampleCount = colliderCount = rendererCount = excludedCount = missingBarrierSamples = 0;
        maxWidthRate = maxCurvature = maxStep = elapsed = 0;
        startedRace = bound = finishing = false; drivers.Clear(); runs.Clear(); nextLane = 0;
        direction = -1; startProgress = 58.5f; startReference = "fallback progress=58.5";
        Directory.CreateDirectory(Output);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
        course = Object.FindFirstObjectByType<RaceCourse>();
        if (course == null) throw new InvalidOperationException("RaceCourse is absent.");
        course.RebuildCache(); // Uses the rounded production path, never authored waypoint chords.
        Physics.SyncTransforms();
        tube = new SerializedObject(course).FindProperty("road.tubeInterior").objectReferenceValue as MeshCollider;
        length = course.TotalLength;
        ConfigureLanes(); ResolveStart();
        findings.Clear(); aggregates.Clear(); shapes.Clear(); limitations.Clear();
        StaticAudit();
        File.WriteAllText(Path.Combine(Output, "static-state.json"), JsonUtility.ToJson(new AuditState
        { rows = findings, notes = limitations, courseLength = length, startProgress = startProgress, direction = direction, startReference = startReference, samples = sampleCount, colliders = colliderCount, renderers = rendererCount, excluded = excludedCount }, true));
        WriteReport("実走開始前（静的計測完了）");
        // -quit exits as soon as executeMethod returns, so the full audit self-terminates instead.
        if (Environment.GetCommandLineArgs().Any(a => a == "-quit"))
        {
            limitations.Add("-quit 指定のため非同期 Play Mode は未実行。完全監査は -quit を外して同じ Run を呼ぶ（完了時に自動終了）。");
            WriteReport("実走未実行: -quit");
            return;
        }
        SessionState.SetBool(Active, true);
        Resume();
        EditorApplication.isPlaying = true;
    }

    static string Hierarchy(Transform t)
    {
        var parts = new List<string>();
        while (t != null) { parts.Add(t.name + "[" + t.GetSiblingIndex() + "]"); t = t.parent; }
        parts.Reverse(); return string.Join("/", parts);
    }
    static bool Vehicle(Component c) => c.GetComponentInParent<DebugMover>() != null || c.GetComponentInParent<TireForce>() != null;
    // FindObjectsByType omits DontSave objects, including the generated road hierarchy.
    static T[] SceneComponents<T>() where T : Component =>
        Enumerable.Range(0, UnityEngine.SceneManagement.SceneManager.sceneCount)
            .Select(UnityEngine.SceneManagement.SceneManager.GetSceneAt).Where(s => s.isLoaded)
            .SelectMany(s => s.GetRootGameObjects()).SelectMany(r => r.GetComponentsInChildren<T>(true)).ToArray();
    static bool RoadOrBarrier(Component c) => c.GetComponentInParent<RaceBarrierPath>() != null ||
        c.transform == tube?.transform || AncestorNamed(c.transform, RaceCourseRoad.RootName);
    static bool AncestorNamed(Transform t, string name)
    { for (; t != null; t = t.parent) if (t.name == name) return true; return false; }
    static bool Excluded(Component c) => Vehicle(c) || RoadOrBarrier(c);
    static bool Solid(Collider c) => c != null && c.enabled && !c.isTrigger && c.gameObject.activeInHierarchy &&
        c.gameObject.layer != CoursePhysicsLayers.OcclusionLayer;

    static void StaticAudit()
    {
        var colliders = SceneComponents<Collider>().Where(Solid).ToArray();
        var renderers = SceneComponents<Renderer>()
            .Where(r => r.enabled && r.gameObject.activeInHierarchy && !Excluded(r)).ToArray();
        colliderCount = colliders.Length; rendererCount = renderers.Length;
        excludedCount = colliders.Count(Excluded);
        var walls = colliders.Where(c => c.GetComponentInParent<RaceBarrierPath>() != null).ToArray();
        var roadColliders = colliders.Where(RoadOrBarrier).Where(c => c.GetComponentInParent<RaceBarrierPath>() == null).ToArray();
        limitations.Add("路面ray対象Collider=" + roadColliders.Length + "（生成道路・チューブ、DontSaveを含むシーン階層から取得）");
        using (samples = new StreamWriter(Path.Combine(Output, "samples.csv"), false, new UTF8Encoding(true)))
        using (var geometry = new StreamWriter(Path.Combine(Output, "geometry.csv"), false, new UTF8Encoding(true)))
        {
            samples.WriteLine("kind,path,component,progress_m,x,y,z,penetration_or_clearance_m,min_height_m,max_height_m,car_band,method");
            geometry.WriteLine("progress_m,x,y,z,width_m,width_rate_m_per_m,curvature_rad_per_m,tube,road,road_hit_height_m,left_wall_clearance_m,right_wall_clearance_m");
            for (float p = 0; p < length; p += .5f)
            {
                sampleCount++;
                course.TryGetSampleAtProgress(p, out var s);
                course.TryGetSampleAtProgress(p - 2.5f, out var before);
                course.TryGetSampleAtProgress(p + 2.5f, out var after);
                float width = Vector3.Distance(s.leftEdge, s.rightEdge);
                float rate = Mathf.Abs(after.width - before.width) / 5f;
                float curvature = Vector3.Angle(before.forward, after.forward) * Mathf.Deg2Rad / 5f;
                maxWidthRate = Mathf.Max(maxWidthRate, rate); maxCurvature = Mathf.Max(maxCurvature, curvature);
                if (rate >= .2f) Add("幅の急変", 2, course, p, s.position, rate, 0, true, "5m窓: " + F(before.width) + " → " + F(after.width) + "m", "車線余裕が短距離で変化する");
                if (curvature >= .06f) Add("高曲率", 2, course, p, s.position, curvature, 0, true, "5m窓; 半径相当=" + F(1f / curvature) + "m", "高速時に大きい操舵・減速を要する");
                bool inside = RaceCourseBarriers.IsInsideTube(s.position, s.right, tube);
                float roadHeight = float.NaN;
                if (SupportHeight(roadColliders, s.position, out float support)) roadHeight = support - s.position.y;
                float[] wallDistances = { float.NaN, float.NaN };
                if (!inside)
                for (int side = 0; side < 2; side++)
                {
                    float nearest = float.PositiveInfinity;
                    foreach (var wall in walls)
                        if (wall.Raycast(new Ray(s.position + Vector3.up, s.right * (side == 0 ? -1 : 1)), out var hit, width * .5f + 16)) nearest = Mathf.Min(nearest, hit.distance);
                    wallDistances[side] = float.IsPositiveInfinity(nearest) ? float.NaN : nearest - width * .5f;
                    if (float.IsNaN(wallDistances[side]))
                    { missingBarrierSamples++; Add("バリア検出なし", 2, course, p, s.position, .5f, 1, true, "側=" + (side == 0 ? "左" : "右") + "; 高さ1mの側方ray", "側方保護が途切れる可能性（チューブ入口付近を含む）"); }
                }
                geometry.WriteLine(string.Join(",", new[] { F(p), F(s.position.x), F(s.position.y), F(s.position.z), F(width), F(rate), F(curvature), inside.ToString(), course.HasRoadAtProgress(p).ToString(), F(roadHeight), F(wallDistances[0]), F(wallDistances[1]) }));
                // Physics broad phase uses actual collider shapes, then exact box/mesh triangle clipping.
                Vector3 fwd = Vector3.ProjectOnPlane(s.forward, Vector3.up).normalized;
                Quaternion rot = Quaternion.LookRotation(fwd, Vector3.up);
                var candidates = Physics.OverlapBox(s.position + Vector3.up * 2, new Vector3(width * .5f + 1.5f, 2, .4f), rot, ~0, QueryTriggerInteraction.Ignore);
                foreach (var c in candidates)
                {
                    if (Excluded(c) || !Solid(c)) continue;
                    Shape shape = GetShape(c);
                    Measure(shape, c, s, p, false, width);
                }
                foreach (var r in renderers)
                {
                    Bounds b = r.bounds;
                    if (Mathf.Abs(b.center.x - s.position.x) > b.extents.x + width + 2 ||
                        Mathf.Abs(b.center.z - s.position.z) > b.extents.z + width + 2 ||
                        b.max.y < s.position.y || b.min.y > s.position.y + 5) continue;
                    // A renderer is collider-less only if no enabled collider is attached to its
                    // object, ancestors or subtree. Other sibling colliders do not cover this mesh.
                    bool hasCollider = r.GetComponentsInParent<Collider>().Any(Solid) || r.GetComponentsInChildren<Collider>().Any(Solid);
                    if (hasCollider) continue;
                    Measure(GetShape(r), r, s, p, true, width);
                }
                if (sampleCount % 1000 == 0) Debug.Log("COURSE_AUDIT_STATIC " + F(p) + "/" + F(length));
            }
            GeometrySeams(walls, roadColliders);
        }
        samples = null;
        var jumps = new List<RaceCourse.JumpSection>();
        var lap = Object.FindFirstObjectByType<LapManager>();
        course.CopyJumpSections(jumps, lap != null && lap.IsProgressReversed ? -1 : 1);
        foreach (var j in jumps)
        {
            course.TryGetSampleAtProgress(j.landing, out var s);
            Add("ジャンプ区間", 3, course, j.landing, s.position, j.length, 0, false,
                "離陸=" + F(j.takeoff) + "m; 着地=" + F(j.landing) + "m", "路面のない区間。長さは丸め後中心線の距離");
            foreach (var f in findings.Where(f => f.kind == "走路侵入" || f.kind == "かすめる").ToArray())
            {
                float delta = Mathf.Abs(Mathf.DeltaAngle(f.distance / length * 360, j.landing / length * 360)) / 360 * length;
                if (delta <= 15) { f.evidence += "; 着地地点から" + F(delta) + "m"; f.severity = Math.Min(f.severity, 1); }
            }
        }
        File.WriteAllText(Path.Combine(Output, "static-summary.json"), JsonUtility.ToJson(new StaticSummary { courseLength = length, samples = sampleCount, colliders = colliderCount, excluded = excludedCount, renderers = rendererCount, maximumWidthRate = maxWidthRate, maximumCurvature = maxCurvature, maximumAdjacentSurfaceStep = maxStep, missingBarrierSideSamples = missingBarrierSamples }, true));
    }
    [Serializable] sealed class StaticSummary
    { public float courseLength, maximumWidthRate, maximumCurvature, maximumAdjacentSurfaceStep; public int samples, colliders, excluded, renderers, missingBarrierSideSamples; }

    static void Measure(Shape shape, Component component, RaceCourse.CourseSample s, float p, bool visual, float width)
    {
        if (Slice(shape, s, width, .02f, 2.5f, out float lo, out float hi, out float hmin, out float hmax))
        {
            float half = width * .5f;
            float depth = Mathf.Min(half - lo, hi + half);
            bool intrusion = depth > .001f;
            float value = intrusion ? Mathf.Min(width, depth) : Mathf.Max(lo - half, -half - hi);
            if (intrusion || value <= 1.5f)
            {
                string kind = visual ? (intrusion ? "見た目の侵入" : "見た目のかすめ") : shape.fallback ? (intrusion ? "Collider侵入候補" : "Colliderかすめ候補") : (intrusion ? "走路侵入" : "かすめる");
                string method = visual ? "Renderer world AABB（保守的な候補）" : shape.fallback ? "未対応形状: boundsまたはconvex元mesh（候補）" : "実Collider三角形クリップ";
                Add(kind, visual ? 3 : intrusion ? (value >= 1 ? 1 : 2) : 3, component, p, s.position, value, hmin, true,
                    method + "; 高さ=" + F(hmin) + "〜" + F(hmax) + "m", visual ? "視界・走路境界の誤認候補。実メッシュの占有は未確定" : intrusion ? "車高帯の物理障害。接触・引っ掛かりの原因" : "境界外1.5m以内。車体の張り出し時に接触し得る");
                samples.WriteLine(Csv(kind, Hierarchy(component.transform), component.GetType().Name, F(p), F(s.position.x), F(s.position.y), F(s.position.z), F(value), F(hmin), F(hmax), "true", method));
            }
        }
        if (Slice(shape, s, width - .002f, 2.5f, 4, out lo, out hi, out hmin, out hmax, false))
        {
            string kind = visual ? "頭上Renderer候補" : shape.fallback ? "頭上Collider候補" : "頭上Collider";
            Add(kind, visual ? 3 : 2, component, p, s.position, hmin, hmin, false,
                "高さ2.5〜4m部分; " + (visual ? "Renderer AABB" : shape.fallback ? "bounds候補" : "実形状"), "ジャンプ・車体上部または視界への影響候補");
            samples.WriteLine(Csv(kind, Hierarchy(component.transform), component.GetType().Name, F(p), F(s.position.x), F(s.position.y), F(s.position.z), F(hmin), F(hmin), F(hmax), "false", visual ? "Renderer AABB" : "Collider"));
        }
    }

    static readonly int[] BoxTriangles = { 0,2,1,1,2,3,4,5,6,5,7,6,0,1,4,1,5,4,2,6,3,3,6,7,0,4,2,2,4,6,1,3,5,3,7,5 };
    static Shape GetShape(Component c)
    {
        if (shapes.TryGetValue(c.GetInstanceID(), out var cached)) return cached;
        var shape = new Shape();
        if (c is MeshCollider mesh && mesh.sharedMesh != null)
        {
            // Read-only MeshData also supports imported non-readable meshes in the Editor.
            try
            {
                using (var data = Mesh.AcquireReadOnlyMeshData(mesh.sharedMesh))
                {
                    var vertices = new Unity.Collections.NativeArray<Vector3>(data[0].vertexCount, Unity.Collections.Allocator.Temp);
                    data[0].GetVertices(vertices); shape.vertices = vertices.ToArray(); vertices.Dispose();
                }
                shape.indices = mesh.sharedMesh.triangles;
                for (int i = 0; i < shape.vertices.Length; i++) shape.vertices[i] = mesh.transform.TransformPoint(shape.vertices[i]);
                // Convex cooking uses a hull, not the source triangles. Mark as conservative.
                shape.fallback = mesh.convex;
            }
            catch (Exception e) { limitations.Add(Hierarchy(c.transform) + ": Mesh読み取り失敗 " + e.Message); }
        }
        if (c is BoxCollider box)
        {
            shape.box = box;
            shape.vertices = BoxVertices(new Bounds(box.center, box.size));
            for (int i = 0; i < 8; i++) shape.vertices[i] = box.transform.TransformPoint(shape.vertices[i]);
            shape.indices = BoxTriangles;
        }
        if (shape.vertices == null)
        {
            Bounds b = c is Collider col ? col.bounds : ((Renderer)c).bounds;
            shape.vertices = BoxVertices(b); shape.indices = BoxTriangles;
            shape.boundsVolume = true;
            shape.fallback = c is Collider;
            if (shape.fallback) limitations.Add(Hierarchy(c.transform) + ": " + c.GetType().Name + " はbounds候補としてのみ計測");
        }
        shape.bounds = c is Collider co ? co.bounds : ((Renderer)c).bounds;
        shapes[c.GetInstanceID()] = shape; return shape;
    }
    static Vector3[] BoxVertices(Bounds b)
    {
        var v = new Vector3[8];
        for (int i = 0; i < 8; i++) v[i] = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
        return v;
    }
    static readonly List<Vector3> polyA = new List<Vector3>(12), polyB = new List<Vector3>(12);
    static bool Slice(Shape shape, RaceCourse.CourseSample s, float width, float lowHeight, float highHeight,
        out float lo, out float hi, out float minHeight, out float maxHeight, bool near = true)
    {
        lo = minHeight = float.PositiveInfinity; hi = maxHeight = float.NegativeInfinity;
        Vector3 forward = Vector3.ProjectOnPlane(s.forward, Vector3.up).normalized;
        float horizontal = Mathf.Max(.001f, Vector3.ProjectOnPlane(s.forward, Vector3.up).magnitude);
        float slope = s.forward.y / horizontal;
        float half = width * .5f + (near ? 1.5f : 0);
        float slab = .25f * horizontal;
        Vector3 Local(Vector3 w)
        { var d = w - s.position; float z = Vector3.Dot(d, forward); return new Vector3(Vector3.Dot(d, s.right), d.y - z * slope, z); }
        // Clip every real triangle to a .5m course slab, the height band, and lateral limits.
        for (int i = 0; i < shape.indices.Length; i += 3)
        {
            Vector3 a = Local(shape.vertices[shape.indices[i]]), b = Local(shape.vertices[shape.indices[i + 1]]), c = Local(shape.vertices[shape.indices[i + 2]]);
            if (Mathf.Min(a.z, Mathf.Min(b.z,c.z)) > slab || Mathf.Max(a.z,Mathf.Max(b.z,c.z)) < -slab ||
                Mathf.Min(a.y, Mathf.Min(b.y,c.y)) > highHeight || Mathf.Max(a.y,Mathf.Max(b.y,c.y)) < lowHeight ||
                Mathf.Min(a.x, Mathf.Min(b.x,c.x)) > half || Mathf.Max(a.x,Mathf.Max(b.x,c.x)) < -half) continue;
            polyA.Clear(); polyA.Add(a); polyA.Add(b); polyA.Add(c);
            Clip(polyA, polyB, 2, slab, true); Clip(polyB, polyA, 2, -slab, false);
            Clip(polyA, polyB, 1, highHeight, true); Clip(polyB, polyA, 1, lowHeight, false);
            Clip(polyA, polyB, 0, half, true); Clip(polyB, polyA, 0, -half, false);
            foreach (var v in polyA) { lo = Mathf.Min(lo,v.x); hi = Mathf.Max(hi,v.x); minHeight = Mathf.Min(minHeight,v.y); maxHeight = Mathf.Max(maxHeight,v.y); }
        }
        // Add query-prism vertices contained by a solid OBB/AABB. Together with clipped
        // box faces these cover intersections even when the query is entirely enclosed.
        if (shape.box != null || shape.boundsVolume)
        {
            for (int i = 0; i < 8; i++)
            {
                Vector3 v = new Vector3((i & 1) == 0 ? -half : half, (i & 2) == 0 ? lowHeight : highHeight, (i & 4) == 0 ? -slab : slab);
                Vector3 w = s.position + s.right * v.x + forward * v.z + Vector3.up * (v.y + v.z * slope);
                bool contains = shape.box != null
                    ? new Bounds(shape.box.center, shape.box.size).Contains(shape.box.transform.InverseTransformPoint(w))
                    : shape.bounds.Contains(w);
                if (!contains) continue;
                lo = Mathf.Min(lo, v.x); hi = Mathf.Max(hi, v.x); minHeight = Mathf.Min(minHeight, v.y); maxHeight = Mathf.Max(maxHeight, v.y);
            }
        }
        return !float.IsPositiveInfinity(lo);
    }
    static void Clip(List<Vector3> input, List<Vector3> output, int axis, float bound, bool less)
    {
        output.Clear(); if (input.Count == 0) return;
        Vector3 a = input[input.Count - 1]; bool ain = less ? a[axis] <= bound : a[axis] >= bound;
        foreach (Vector3 b in input)
        {
            bool bin = less ? b[axis] <= bound : b[axis] >= bound;
            if (ain != bin) output.Add(Vector3.LerpUnclamped(a, b, (bound - a[axis]) / (b[axis] - a[axis])));
            if (bin) output.Add(b); a = b; ain = bin;
        }
    }

    static bool SupportHeight(Collider[] roads, Vector3 pos, out float height)
    {
        height = float.NegativeInfinity;
        // Start at car height inside the tube, not above its roof. Accept only nearby support,
        // so an unrelated lower deck cannot hide a missing surface.
        foreach (var road in roads)
            if (road.Raycast(new Ray(pos + Vector3.up * 1.5f, Vector3.down), out var hit, 5.5f) &&
                Mathf.Abs(hit.point.y - pos.y) <= 1.5f)
                height = Mathf.Max(height, hit.point.y);
        return !float.IsInfinity(height);
    }
    static void ConfigureLanes()
    {
        runs.Clear(); nextLane = 0;
        string raw = Environment.GetEnvironmentVariable("COURSE_AUDIT_LANES") ?? "-0.75,0,0.75";
        foreach (string token in raw.Split(','))
        {
            if (!float.TryParse(token.Trim(), NumberStyles.Float, Inv, out float lane) ||
                float.IsNaN(lane) || float.IsInfinity(lane) || Mathf.Abs(lane) > 1)
                throw new ArgumentException("COURSE_AUDIT_LANES must contain fractions in [-1,1].");
            if (!runs.Any(r => r.lane == lane)) runs.Add(new LaneRun { lane = lane });
        }
        string laps = Environment.GetEnvironmentVariable("COURSE_AUDIT_LAPS") ?? "1";
        if (!int.TryParse(laps, NumberStyles.Integer, Inv, out lapsPerLane) || lapsPerLane < 1 || lapsPerLane > 1000)
            throw new ArgumentException("COURSE_AUDIT_LAPS must be 1..1000.");
        laneSeconds = Mathf.Max(180, length / 8 + 60) * lapsPerLane;
        string seconds = Environment.GetEnvironmentVariable("COURSE_AUDIT_LANE_SECONDS");
        if (seconds != null && (!float.TryParse(seconds, NumberStyles.Float, Inv, out laneSeconds) ||
            float.IsNaN(laneSeconds) || float.IsInfinity(laneSeconds) || laneSeconds <= 0))
            throw new ArgumentException("COURSE_AUDIT_LANE_SECONDS must be a positive finite number.");
        simulationLimit = laneSeconds * runs.Count + 30;
        wallLimit = Math.Max(420, simulationLimit * 3 + 120);
    }
    static void ResolveStart()
    {
        var lap = Object.FindFirstObjectByType<LapManager>();
        if (lap != null && (bool)typeof(LapManager).GetField("hasProgressReference", Private).GetValue(lap))
        {
            direction = lap.IsProgressReversed ? -1 : 1;
            float directed = (float)typeof(LapManager).GetField("progressStartDistance", Private).GetValue(lap);
            startProgress = Mathf.Repeat(direction * directed, length);
            startReference = "LapManager progress reference (goal)";
            return;
        }
        // Edit Mode has not run LapManager.Awake: use the configured grid checkpoint.
        var manager = Object.FindFirstObjectByType<Gmanager>();
        var start = manager != null ? typeof(Gmanager).GetField("startCheckpoint", Private).GetValue(manager) as CheckpointSensor : null;
        if (start == null) return;
        startProgress = course.GetProgressDistance(start.transform.position);
        course.TryGetSampleAtProgress(startProgress, out var sample);
        direction = Vector3.Dot(start.transform.forward, sample.forward) < 0 ? -1 : 1;
        startReference = "Gmanager.startCheckpoint: " + Hierarchy(start.transform);
    }
    static float FromStart(float progress) => Mathf.Repeat(direction * (progress - startProgress), length);

    static void GeometrySeams(Collider[] walls, Collider[] roads)
    {
        // Densely sample actual portal support heights across each road/tube transition.
        bool previousInside = false;
        course.TryGetSampleAtProgress(length - .5f, out var prev);
        previousInside = RaceCourseBarriers.IsInsideTube(prev.position, prev.right, tube);
        for (float p = 0; p < length; p += .5f)
        {
            course.TryGetSampleAtProgress(p, out var s);
            bool inside = RaceCourseBarriers.IsInsideTube(s.position, s.right, tube);
            if (inside != previousInside)
            {
                for (int lane = -1; lane <= 1; lane++)
                {
                    float previousHeight = float.NaN;
                    for (float offset = -2; offset <= 2; offset += .1f)
                    {
                        course.TryGetSampleAtProgress(p + offset, out var a);
                        Vector3 pos = a.position + a.right * lane * a.width * .35f;
                        SupportHeight(roads, pos, out float height);
                        if (!float.IsInfinity(height) && !float.IsNaN(previousHeight))
                        {
                            float step = Mathf.Abs(height - previousHeight); maxStep = Mathf.Max(maxStep, step);
                            if (step > .08f) Add("チューブ出入口の高低差", step > .2f ? 1 : 2, tube, p + offset, pos, step, 0, true, "0.1m間隔の路面高差（勾配成分を含む）; lane=" + lane, "段差または急勾配。タイヤが衝撃を受ける候補");
                        }
                        if (float.IsInfinity(height)) Add("チューブ出入口の路面未検出", 2, course, p + offset, pos, .1f, 0, true, "中心/左右35%車線の下向きray; lane=" + lane, "支持面の隙間候補（rayの取り逃しを含む）");
                        previousHeight = float.IsInfinity(height) ? float.NaN : height;
                    }
                }
            }
            previousInside = inside;
        }
        // Compare actual barrier base polylines at every path join, including closed seams.
        var endpoints = new List<(RaceBarrierPath path, Vector3 point)>();
        foreach (var path in SceneComponents<RaceBarrierPath>().Where(p => p.isActiveAndEnabled))
        {
            var points = new List<Vector3>(); path.CopyPreviewPathWorld(points);
            if (points.Count < 2) continue;
            if (!path.ClosedLoop) { endpoints.Add((path, points[0])); endpoints.Add((path, points[points.Count - 1])); }
            for (int i = 1; i < points.Count; i++)
            {
                Vector3 d = points[i] - points[i - 1];
                if (Mathf.Abs(d.y) > .08f && Vector3.ProjectOnPlane(d, Vector3.up).magnitude < .2f)
                    Add("バリア基部の段差", 2, path, course.GetProgressDistance(points[i]), points[i], Mathf.Abs(d.y), 0, true, "隣接基部ノード（水平距離<0.2m）", "短距離の基部高低差による引っ掛かり候補");
            }
        }
        foreach (var e in endpoints)
        {
            float nearest = float.PositiveInfinity;
            foreach (var other in endpoints)
                if (other.path != e.path) nearest = Mathf.Min(nearest, Vector3.Distance(e.point, other.point));
            float p = course.GetProgressDistance(e.point); course.TryGetSampleAtProgress(p, out var s);
            bool portal = false;
            for (float offset = -3; offset <= 3; offset += .5f)
            { course.TryGetSampleAtProgress(p + offset, out var a); portal |= RaceCourseBarriers.IsInsideTube(a.position, a.right, tube); }
            if (!portal && nearest > .1f && nearest < 5)
                Add("バリア端点間の隙間候補", 2, e.path, p, e.point, nearest, 0, true, "他経路の最寄り基部端点までの3D距離", "基部が連続しない候補。実壁厚・張り出しによる閉鎖は未確定");
        }
    }

    static Finding Add(string kind, int severity, Component c, float p, Vector3 position, float value, float height, bool band, string evidence, string impact, string lane = "")
    {
        p = Mathf.Repeat(p, length);
        string path = Hierarchy(c.transform), key = kind + ":" + c.GetInstanceID() + ":" + lane;
        if (!aggregates.TryGetValue(key, out var f))
        {
            f = new Finding { kind = kind, lane = lane, severity = severity, path = path, component = c.GetType().Name, distance = p, first = p, last = p, value = value, height = height, position = position, carBand = band, evidence = evidence, impact = impact };
            findings.Add(f); aggregates[key] = f;
        }
        f.count++; f.covered += .5f; f.first = Mathf.Min(f.first, p); f.last = Mathf.Max(f.last, p); f.severity = Math.Min(f.severity, severity);
        bool minimum = kind.Contains("頭上") || kind.Contains("かすめ");
        if (minimum ? value < f.value : value > f.value)
        { f.value = value; f.distance = p; f.position = position; f.height = height; f.evidence = evidence; }
        return f;
    }

    [InitializeOnLoadMethod]
    static void Resume()
    {
        if (!SessionState.GetBool(Active, false)) return;
        began = EditorApplication.timeSinceStartup;
        EditorApplication.update -= Tick; EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged -= Mode; EditorApplication.playModeStateChanged += Mode;
    }
    static void Mode(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Active, false)) return;
        if (findings.Count == 0)
        {
            var saved = JsonUtility.FromJson<AuditState>(File.ReadAllText(Path.Combine(Output, "static-state.json")));
            findings.AddRange(saved.rows); limitations.AddRange(saved.notes); length = saved.courseLength;
            startProgress = saved.startProgress; direction = saved.direction; startReference = saved.startReference;
            sampleCount = saved.samples; colliderCount = saved.colliders; rendererCount = saved.renderers; excludedCount = saved.excluded;
        }
        startedRace = bound = finishing = false; elapsed = 0; drivers.Clear();
        ConfigureLanes();
        // Avoid external publishing during an observational run; no serialized state is saved.
        foreach (var publisher in Object.FindObjectsByType<RaceRemoteFirebasePublisher>(FindObjectsSortMode.None)) publisher.enabled = false;
        var loop = PlayerLoop.GetCurrentPlayerLoop();
        Install(ref loop); PlayerLoop.SetPlayerLoop(loop);
        Physics.ContactEvent -= Contacts;
        Physics.ContactEvent += Contacts;
    }
    static bool Install(ref PlayerLoopSystem system)
    {
        if (system.type == typeof(UnityEngine.PlayerLoop.FixedUpdate))
        {
            var list = system.subSystemList.ToList();
            int at = list.FindIndex(x => x.type == typeof(UnityEngine.PlayerLoop.FixedUpdate.ScriptRunBehaviourFixedUpdate));
            list.Insert(Math.Max(0, at), new PlayerLoopSystem { type = typeof(CoursePlayabilityAudit), updateDelegate = FixedTick });
            system.subSystemList = list.ToArray(); return true;
        }
        if (system.subSystemList != null)
            for (int i = 0; i < system.subSystemList.Length; i++) if (Install(ref system.subSystemList[i])) return true;
        return false;
    }
    static void Tick()
    {
        if (!SessionState.GetBool(Active, false) || finishing) return;
        try
        {
            if (EditorApplication.timeSinceStartup - began > wallLimit) { Finish("実走タイムアウト（壁時計 " + wallLimit + "秒）"); return; }
            if (!EditorApplication.isPlaying || !Application.isPlaying) return;
            var manager = Object.FindFirstObjectByType<Gmanager>();
            if (manager == null || Time.timeSinceLevelLoad < 1) return;
            if (!startedRace)
            {
                startedRace = true; Application.runInBackground = true;
                // Transient audit-only limits: do not finish the race after the normal two laps.
                // Lock preparation first; otherwise StartGameWhenScreenCovered reapplies normal limits.
                manager.Features?.Settings.BeginPreparation();
                typeof(Gmanager).GetField("activeRules", Private).SetValue(manager, null);
                typeof(Gmanager).GetField("maximumRaceSeconds", Private).SetValue(manager, simulationLimit + 60);
                Object.FindFirstObjectByType<LapManager>().SetGoalLaps(int.MaxValue);
                typeof(Gmanager).GetMethod("StartGameWhenScreenCovered", Private).Invoke(manager, null);
                typeof(Gmanager).GetMethod("CompleteGameStart", Private).Invoke(manager, null);
                foreach (var transition in Object.FindObjectsByType<ScreenTransitionController>(FindObjectsSortMode.None)) transition.ApplyStateImmediate(Gmanager.State.Countdown);
            }
            if (!bound && manager.state == Gmanager.State.Game && manager.IsDrivingEnabled)
            {
                course = Object.FindFirstObjectByType<RaceCourse>(); length = course.TotalLength;
                tube = new SerializedObject(course).FindProperty("road.tubeInterior").objectReferenceValue as MeshCollider;
                ResolveStart();
                Object.FindFirstObjectByType<LapManager>().SetGoalLaps(int.MaxValue);
                trace = new StreamWriter(Path.Combine(Output, "driving-trace.csv"), false, new UTF8Encoding(true));
                trace.WriteLine("time,car,lane_fraction,phase,progress_m,start_distance_m,travel_m,road_half_width_m,target_offset_m,actual_offset_m,lane_error_m,x,y,z,speed_mps,grounded,pedal,steering");
                contactTrace = new StreamWriter(Path.Combine(Output, "collision-events.csv"), false, new UTF8Encoding(true));
                contactTrace.WriteLine("time,car,lane_fraction,category,object,collider,progress_m,start_distance_m,x,y,z,normal_x,normal_y,normal_z,relative_speed_approx_mps");
                for (int i = 0; i < 2; i++)
                {
                    var car = GameObject.Find("Player" + (i + 1) + "_Car"); if (car == null) continue;
                    var d = new Driver { body = car.GetComponent<Rigidbody>(), mover = car.GetComponent<DebugMover>(), colliders = car.GetComponentsInChildren<Collider>(), tires = car.GetComponentsInChildren<TireForce>(), input = new AuditInput { PlayerIndex = i } };
                    d.previous = course.GetProgressDistance(d.body.position); d.mover.SetInputSource(d.input); drivers.Add(d);
                    d.halfWidth = .5f;
                    foreach (var collider in d.colliders)
                    {
                        collider.providesContacts = true;
                        if (!Solid(collider)) continue;
                        foreach (Vector3 corner in GetShape(collider).vertices)
                            d.halfWidth = Mathf.Max(d.halfWidth, Mathf.Abs(Vector3.Dot(corner - d.body.position, d.body.transform.right)));
                    }
                    AssignLane(d);
                    foreach (var capture in car.GetComponentsInChildren<PlayerAudioCapture>()) capture.enabled = false;
                }
                if (drivers.Count == 0) throw new InvalidOperationException("No player cars spawned.");
                bound = true;
            }
            if (bound && drivers.All(d => d.done)) Finish("実走終了（車線別の完了状態を参照）");
            else if (bound && (elapsed >= simulationLimit || manager.state == Gmanager.State.Goal)) Finish("実走中断（時間制限またはレース終了）");
        }
        catch (Exception e) { limitations.Add(e.ToString()); Finish("実走エラー"); }
    }
    static void AssignLane(Driver d)
    {
        d.input.CurrentState = DriveInputState.Neutral;
        if (nextLane >= runs.Count) { d.done = true; d.run = null; return; }
        d.run = runs[nextLane++]; d.run.car = d.body.name; d.run.began = elapsed; d.run.status = "走行中";
        d.previous = course.GetProgressDistance(d.body.position);
        d.previousPosition = d.body.position;
        d.stalled = d.offTime = 0; d.off = d.stuck = false; d.bot.Clear();
    }
    static float LaneOffset(Driver d, RaceCourse.CourseSample sample) =>
        Mathf.Sign(d.run.lane) * Mathf.Min(Mathf.Abs(d.run.lane) * sample.width * .5f,
            Mathf.Max(0, sample.width * .5f - d.halfWidth - 1));
    static void FixedTick()
    {
        if (!bound || finishing) return;
        try
        {
            elapsed += Time.fixedDeltaTime;
            foreach (var d in drivers)
            {
                if (d.body == null || d.done) continue;
                // Defer the handoff until the previous physics step's Contacts has been attributed.
                if (d.run.status != "走行中") { AssignLane(d); if (d.done) continue; }
                var r = d.run;
                float p = course.GetProgressDistance(d.body.position), speed = d.body.linearVelocity.magnitude;
                course.TryGetSampleAtProgress(p, out var current);
                float actual = Vector3.Dot(d.body.position - current.position, current.right);
                float targetOffset = LaneOffset(d, current), error = Mathf.Abs(actual - targetOffset);
                float delta = Mathf.Repeat(direction * (p - d.previous) + length * .5f, length) - length * .5f;
                float plausibleStep = Mathf.Max(5, Mathf.Max(speed, d.previousSpeed) * Time.fixedDeltaTime * 4);
                if (Mathf.Abs(delta) > plausibleStep || Vector3.Distance(d.previousPosition, d.body.position) > plausibleStep)
                {
                    // Respawn/projection jumps cannot count as driven coverage. Require a fresh circuit.
                    Add("実走位置不連続", 2, d.body, p, d.body.position, Mathf.Abs(delta), 0, true,
                        "固定stepの移動が速度からの許容値を超過; 周回計測をリセット", "リスポーンまたは中心線投影の飛び。連続走行の確認が必要", F(r.lane));
                    r.distance = r.onTargetDistance = 0; r.acquired = false; delta = 0;
                }
                if (r.acquired)
                {
                    r.distance += delta;
                    if (error <= .75f) r.onTargetDistance += Mathf.Max(0, delta);
                }
                else if (error <= .75f && speed > 1) r.acquired = true;
                d.previousPosition = d.body.position;
                d.previous = p; r.maxSpeed = Mathf.Max(r.maxSpeed, speed);
                if (r.acquired) r.maxError = Mathf.Max(r.maxError, error);
                int grounded = d.tires.Count(t => t.GetComponent<GroundCheck>().CheckNow());
                var input = d.bot.Decide(course, d.body, direction, grounded, Time.fixedTime, lane: targetOffset, noise: 0, vehicle: d.mover);
                // Keep Bot speed planning/pedal; use its steering law without the halfWidth-2m clamp.
                if (grounded > 0)
                {
                    float planarSpeed = Vector3.ProjectOnPlane(d.body.linearVelocity, d.body.rotation * Vector3.up).magnitude;
                    course.TryGetSampleAtProgress(p + direction * Mathf.Clamp(5 + planarSpeed * .4f, 8, 17), out var target);
                    Vector3 local = Quaternion.Inverse(d.body.rotation) * (target.position + target.right * LaneOffset(d, target) - d.body.position);
                    input.steering = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg * 1.5f - Vector3.Dot(d.body.angularVelocity, target.up) * 8, -30, 30);
                }
                d.input.CurrentState = input;
                d.stalled = speed < 1 && elapsed - r.began > 5 ? d.stalled + Time.fixedDeltaTime : 0;
                r.maxStall = Mathf.Max(r.maxStall, d.stalled);
                if (d.stalled >= 3 && !d.stuck)
                { d.stuck = true; r.stops++; Add("実走スタック", 1, d.body, p, d.body.position, d.stalled, 0, true, "速度<1m/sが3秒継続; pedal=" + F(input.pedal), "自動運転が走行継続できない", F(r.lane)); }
                if (d.stalled == 0) d.stuck = false;
                bool outside = !course.IsPointInsideDrivingArea(d.body.position);
                d.offTime = outside ? d.offTime + Time.fixedDeltaTime : 0;
                if (d.offTime > .5f && !d.off)
                { d.off = true; r.excursions++; Add("実走コースアウト", 1, d.body, p, d.body.position, d.offTime, 0, true, "DrivingArea外が0.5秒超", "コース外へ逸脱", F(r.lane)); }
                if (!outside) d.off = false;
                if (d.previousSpeed > 10 && speed < d.previousSpeed * .65f)
                { r.decelerations++; Add("実走大減速", 2, d.body, p, d.body.position, d.previousSpeed - speed, 0, true, "1固定stepで速度が35%以上減少; " + F(d.previousSpeed) + "→" + F(speed) + "m/s", "衝突・急制動などによる速度損失", F(r.lane)); }
                trace.WriteLine(Csv(F(elapsed), d.body.name, F(r.lane), r.acquired ? "measuring" : "acquiring", F(p), F(FromStart(p)), F(r.distance), F(current.width * .5f), F(targetOffset), F(actual), F(error), F(d.body.position.x), F(d.body.position.y), F(d.body.position.z), F(speed), grounded.ToString(), F(input.pedal), F(input.steering)));
                d.previousSpeed = speed; d.previousVelocity = d.body.linearVelocity;
                d.previousAngularVelocity = d.body.angularVelocity; d.previousCenter = d.body.worldCenterOfMass;
                if (r.distance >= length * lapsPerLane || elapsed - r.began >= laneSeconds)
                    r.status = r.distance >= length * lapsPerLane ? "完了" : "時間切れ";
            }
        }
        catch (Exception e) { limitations.Add(e.ToString()); Finish("実走計測エラー"); }
    }
    // Unity's global collision callback supplies the same enter flag as OnCollisionEnter.
    // https://docs.unity3d.com/ja/6000.0/ScriptReference/Physics.ContactEvent.html
    static void Contacts(PhysicsScene scene, Unity.Collections.NativeArray<ContactPairHeader>.ReadOnly headers)
    {
        if (!bound || finishing) return;
        foreach (var header in headers)
        for (int i = 0; i < header.pairCount; i++)
        {
            var pair = header.GetContactPair(i);
            if (!pair.isCollisionEnter || pair.contactCount == 0) continue;
            foreach (var d in drivers)
            {
                Collider other = d.colliders.Contains(pair.collider) ? pair.otherCollider :
                    d.colliders.Contains(pair.otherCollider) ? pair.collider : null;
                if (d.done || d.run == null || other == null || Vehicle(other)) continue;
                bool barrier = other.GetComponentInParent<RaceBarrierPath>() != null;
                if (!barrier && RoadOrBarrier(other)) continue;
                if (barrier) d.run.barriers++; else d.run.collisions++;
                for (int j = 0; j < pair.contactCount; j++)
                {
                    var contact = pair.GetContactPoint(j);
                    Vector3 point = contact.position;
                    Vector3 otherVelocity = other.attachedRigidbody != null ? other.attachedRigidbody.GetPointVelocity(point) : Vector3.zero;
                    float relative = (d.previousVelocity + Vector3.Cross(d.previousAngularVelocity, point - d.previousCenter) - otherVelocity).magnitude;
                    float p = course.GetProgressDistance(point);
                    if (!barrier) Add("実走衝突", 1, other, p, point, relative, point.y - course.GetNearestPointOnCenterLineWorld(point).y, true,
                        d.body.name + "; t=" + F(elapsed) + "s; 接触点=" + V(point) + "; 法線=" + V(contact.normal) + "; 相対速度は前step車体接触点速度との差", "道路・バリア・車以外との実接触。衝突成立を確認", F(d.run.lane));
                    contactTrace.WriteLine(Csv(F(elapsed), d.body.name, F(d.run.lane), barrier ? "barrier" : "obstacle", Hierarchy(other.transform), other.GetType().Name, F(p), F(FromStart(p)), F(point.x), F(point.y), F(point.z), F(contact.normal.x), F(contact.normal.y), F(contact.normal.z), F(relative)));
                }
            }
        }
    }
    static void Finish(string status)
    {
        if (finishing) return; finishing = true;
        SessionState.SetBool(Active, false); trace?.Dispose(); trace = null;
        contactTrace?.Dispose(); contactTrace = null; Physics.ContactEvent -= Contacts;
        foreach (var r in runs.Where(r => r.status == "走行中")) r.status = "中断";
        if (runs.Any(r => r.status != "完了")) limitations.Add("未完了の車線あり。完了状態・実測距離を参照。未走行部分の安全性は未確認。");
        WriteReport(status);
        WriteRuntime(status);
        EditorApplication.update -= Tick;
        EditorApplication.Exit(0);
    }
    static void WriteRuntime(string status)
    {
        var text = new StringBuilder("\n## 実走結果\n\n" + status + "; simulation=" + F(elapsed) + "s; direction=" + direction + ". RoadObservationBotを実車へ入力。pose/速度の補正なし。各固定stepで接触ペアの発生を取得。\n\n");
        text.AppendLine("車線あたり制限=" + F(laneSeconds) + "s; スタート基準=" + startReference + "; progress=" + F(startProgress) + "m。\n");
        text.AppendLine("|車線（半幅比）|車|状態|指定周回|実測周回相当|距離m|最高速m/s|障害物接触|バリア接触|コースアウト|大減速|停止回数|最大停止s|最大車線誤差m|目標±0.75m内の前進距離m|\n|---:|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        using (var summary = new StreamWriter(Path.Combine(Output, "lane-summary.csv"), false, new UTF8Encoding(true)))
        {
            summary.WriteLine("lane_fraction,car,status,requested_laps,measured_laps,travel_m,max_speed_mps,obstacle_enters,barrier_enters,excursions,decelerations,stops,max_stop_s,max_lane_error_m,on_target_forward_m");
            foreach (var r in runs)
            {
                var cells = new[] { F(r.lane), r.car, r.status, lapsPerLane.ToString(Inv), F(r.distance / length), F(r.distance), F(r.maxSpeed), r.collisions.ToString(Inv), r.barriers.ToString(Inv), r.excursions.ToString(Inv), r.decelerations.ToString(Inv), r.stops.ToString(Inv), F(r.maxStall), F(r.maxError), F(r.onTargetDistance) };
                summary.WriteLine(Csv(cells)); text.AppendLine("|" + string.Join("|", cells) + "|");
            }
        }
        using (var csv = new StreamWriter(Path.Combine(Output, "runtime-findings.csv"), false, new UTF8Encoding(true)))
        {
            csv.WriteLine(Header);
            foreach (var f in Ordered().Where(f => f.kind.StartsWith("実走", StringComparison.Ordinal))) csv.WriteLine(CsvRow(f));
        }
        text.AppendLine("\n制限: 車線はsample.right方向の半幅比。狭い区間では車体幅+端から1mの余白で制限。移行中もイベント集計し、目標車線への初回到達後の距離を周回換算。追従精度はdriving-trace.csvで確認。接触回数はColliderペアのEnter単位（接触点の行数とは異なる）。相対速度は前step車体の並進・角速度と相手の現在接触点速度による近似。全横位置の安全を保証しない。");
        foreach (var note in limitations.Distinct()) text.AppendLine("- " + note.Replace("\n", " "));
        File.AppendAllText(Path.Combine(Output, "findings.md"), text.ToString(), new UTF8Encoding(true));
        Debug.Log("COURSE_PLAYABILITY_AUDIT_COMPLETE runtimeRows=" + findings.Count + " seconds=" + F(elapsed));
    }

    const string Header = "severity,kind,path,component,lane_fraction,progress_m,start_distance_m,x,y,z,value,min_height_m,car_band,sample_hits,first_progress_m,last_progress_m,evidence,impact";
    static IEnumerable<Finding> Ordered() => findings.OrderBy(f => f.severity).ThenBy(f => f.kind).ThenByDescending(f => f.value).ThenBy(f => f.path);
    static string CsvRow(Finding f) => Csv(f.severity.ToString(), f.kind, f.path, f.component, f.lane, F(f.distance), F(FromStart(f.distance)), F(f.position.x), F(f.position.y), F(f.position.z), F(f.value), F(f.height), f.carBand.ToString(), f.count.ToString(), F(f.first), F(f.last), f.evidence, f.impact);
    static string Markdown(int i, Finding f) => "|" + i + "|" + (f.severity == 1 ? "高" : f.severity == 2 ? "中" : "参考") + "|" + f.kind + "|" + f.path.Replace("|", "\\|") + " (" + f.component + ")|" + F(f.distance) + "m / " + V(f.position) + "|" + F(FromStart(f.distance)) + "m|" + f.lane + "|" + F(f.value) + (f.kind == "高曲率" ? "rad/m" : f.kind == "幅の急変" ? "m/m" : f.kind == "実走衝突" || f.kind == "実走大減速" ? "m/s" : f.kind == "実走スタック" || f.kind == "実走コースアウト" ? "s" : "m") + "; 車高帯=" + f.carBand + "; hits=" + f.count + "; " + f.evidence.Replace("|", "\\|") + "|" + f.impact + "|";
    static void WriteReport(string runtime)
    {
        using (var csv = new StreamWriter(Path.Combine(Output, "findings.csv"), false, new UTF8Encoding(true)))
        { csv.WriteLine(Header); foreach (var f in Ordered()) csv.WriteLine(CsvRow(f)); }
        var md = new StringBuilder("# SampleScene コース走行性監査\n\n");
        md.AppendLine("Unity " + Application.unityVersion + "; scene=Assets/Scenes/SampleScene.unity; コース全長=" + F(length) + "m; 丸め後中心線0.5m間隔=" + sampleCount + "地点。シーン保存・Asset保存APIは使用しない。\n");
        md.AppendLine("スタートからの距離 = Repeat(direction * (progress - startProgress), 全長)。startProgress=" + F(startProgress) + "m; direction=" + direction + "; reference=" + startReference + "。逆方向では (startProgress-progress) mod 全長。\n");
        md.AppendLine("## 集計\n");
        foreach (var group in findings.GroupBy(f => f.kind).OrderBy(g => g.Key)) md.AppendLine("- " + group.Key + ": " + group.Count() + "件（種類×Component単位。複数地点をまとめ最大侵入/最小離隔の地点を表示）");
        md.AppendLine("\n## 計測方法と限界\n\n左右境界はTryGetSampleAtProgressのleftEdge/rightEdge。距離は丸め後3D中心線の距離（スタートライン基準とは限らない）。各0.5m区間の断面にColliderの実三角形をクリップし、左右境界からの最大到達深さをmで記録。高さは路面中心線からワールド鉛直上方（区間勾配を補正）。車高帯は0.02〜2.5m（接地面の数値誤差除外）。外側0〜1.5mは別区分。頭上は2.5〜4m、0〜2.5mは侵入表を参照。BoxColliderは変換後の実OBB、MeshColliderはmesh三角形。convex mesh・その他Colliderは候補扱いで精度を明記する。巨大な閉じた形状が区間を完全に内包し表面が区間外の場合は未検出になり得る。\n\nRendererは有効Colliderを自身・祖先・子に持たない有効物体だけ。world boundsをクリップするため、空白を含む保守的な候補であり、実ポリゴン侵入とは断定しない。道路生成物・チューブ走行面・RaceBarrierPath・車を除外。シーンを開く際のExecuteAlwaysが生成した一時道路・バリアを含めて計測。無効物体とtriggerは対象外。\n\n幅変化は5m窓で0.2m/m以上、曲率は5m窓で0.06rad/m以上を抽出。チューブ入口は既存の壁/天井ray判定の切替点±2mを0.1mで中心・左右35%位置計測。バリアは0.5m毎・高さ1mの側方rayと基部端点/隣接点の検査。表面全体の微細な継ぎ目や全高さの隙間を保証する検査ではない。\n");
        md.AppendLine("物理Collider総数=" + colliderCount + "; 道路/バリア/車の除外=" + excludedCount + "; 視覚候補Renderer=" + rendererCount + ". 生データはsamples.csv、全地点の幅・曲率・路面・バリアrayはgeometry.csv。hitsは検出回数で、first〜last全区間の連続占有を意味しない。\n");
        foreach (var note in limitations.Distinct()) md.AppendLine("- " + note.Replace("\n", " "));
        md.AppendLine("\n## 重要度順の検出一覧\n\n| # | 重要度 | 種類 | オブジェクト（ヒエラルキー）| コース距離 / 座標 | スタートからの距離 | 車線（半幅比）| 計測値 | プレイへの影響 |\n|---|---|---|---|---|---|---|---|---|");
        int i = 0; foreach (var f in Ordered()) md.AppendLine(Markdown(++i, f));
        md.AppendLine("\n実走状態: " + runtime);
        File.WriteAllText(Path.Combine(Output, "findings.md"), md.ToString(), new UTF8Encoding(true));
        Debug.Log("COURSE_AUDIT_STATIC_COMPLETE findings=" + findings.Count + " length=" + F(length));
    }
    static string F(float v) => v.ToString("0.###", Inv);
    static string V(Vector3 v) => "(" + F(v.x) + ", " + F(v.y) + ", " + F(v.z) + ")";
    static string Csv(params string[] values) => string.Join(",", values.Select(v => "\"" + (v ?? "").Replace("\"", "\"\"") + "\""));
}
