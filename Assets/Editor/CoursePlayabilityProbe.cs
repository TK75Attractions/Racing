using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Ray probes at car heights to tell real side/front/overhead obstacles from coplanar floors.</summary>
public static class CoursePlayabilityProbe
{
    const string Output = "Artifacts/CoursePlayabilityAudit";
    static readonly float[] Heights = { .3f, .8f, 1.5f, 2.2f };

    sealed class Hit { public string kind, path; public float first = float.MaxValue, last, min = float.MaxValue, max; public int count; public Vector3 at; public string heights = ""; }

    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
        var course = Object.FindFirstObjectByType<RaceCourse>();
        course.RebuildCache();
        Physics.SyncTransforms();
        var tube = new SerializedObject(course).FindProperty("road.tubeInterior").objectReferenceValue as Collider;
        bool Ignore(Collider c) => c.isTrigger || c.gameObject.layer == CoursePhysicsLayers.OcclusionLayer || c == tube || c.GetComponentInParent<RaceBarrierPath>() != null ||
            c.GetComponentInParent<DebugMover>() != null || c.GetComponentInParent<TireForce>() != null || Road(c.transform);
        bool Road(Transform t) { for (; t != null; t = t.parent) if (t.name == RaceCourseRoad.RootName) return true; return false; }
        string Path(Transform t) { var p = new List<string>(); for (; t != null; t = t.parent) p.Add(t.name); p.Reverse(); return string.Join("/", p); }
        var hits = new Dictionary<string, Hit>();
        void Add(string kind, Collider c, float progress, float value, Vector3 at, string h)
        {
            string key = kind + "|" + Path(c.transform);
            if (!hits.TryGetValue(key, out var r)) hits[key] = r = new Hit { kind = kind, path = Path(c.transform), at = at };
            r.count++; r.first = Mathf.Min(r.first, progress); r.last = Mathf.Max(r.last, progress);
            r.min = Mathf.Min(r.min, value); r.max = Mathf.Max(r.max, value);
            if (!r.heights.Contains(h)) r.heights += h + " ";
        }
        bool First(Ray ray, float distance, out RaycastHit best)
        {
            best = default; float d = float.MaxValue; bool any = false;
            foreach (var h in Physics.RaycastAll(ray, distance, ~0, QueryTriggerInteraction.Ignore))
                if (!Ignore(h.collider) && h.distance < d) { d = h.distance; best = h; any = true; }
            return any;
        }
        float length = course.TotalLength;
        for (float p = 0; p < length; p += .5f)
        {
            if (!course.HasRoadAtProgress(p) || !course.TryGetSampleAtProgress(p, out var s)) continue;
            if (RaceCourseBarriers.IsInsideTube(s.position, s.right, tube as MeshCollider)) continue;
            float half = Vector3.Distance(s.leftEdge, s.rightEdge) * .5f;
            Vector3 right = Vector3.ProjectOnPlane(s.right, Vector3.up).normalized;
            Vector3 fwd = Vector3.ProjectOnPlane(s.forward, Vector3.up).normalized;
            foreach (float h in Heights)
            {
                Vector3 o = s.position + Vector3.up * h;
                foreach (int side in new[] { -1, 1 })
                {
                    // Reachable space ends at the generated barrier; the shoulder before it is drivable.
                    float barrier = half + 8f;
                    foreach (var b in Physics.RaycastAll(new Ray(o, right * side), half + 8f, ~0, QueryTriggerInteraction.Ignore))
                        if (b.collider.GetComponentInParent<RaceBarrierPath>() != null) barrier = Mathf.Min(barrier, b.distance);
                    if (First(new Ray(o, right * side), barrier, out var hit))
                        Add(hit.distance < half ? "側面の壁が走路内" : "路肩（走路端〜バリア間）に障害物", hit.collider, p,
                            hit.distance < half ? half - hit.distance : hit.distance - half, hit.point, h.ToString("0.0"));
                }
                // Forward rays across the lane catch walls/beams that cross the road.
                foreach (float lane in new[] { -.6f, 0f, .6f })
                    if (First(new Ray(o + right * half * lane, fwd), .5f, out var hit))
                        Add("進路正面に壁", hit.collider, p, h, hit.point, h.ToString("0.0"));
            }
            foreach (float lane in new[] { -.8f, -.4f, 0f, .4f, .8f })
            {
                Vector3 o = s.position + right * half * lane;
                if (First(new Ray(o + Vector3.up * .05f, Vector3.up), 4f, out var up))
                    Add("頭上の低い天井・梁", up.collider, p, up.distance + .05f, up.point, "lane" + lane.ToString("0.0"));
                if (First(new Ray(o + Vector3.up * 4f, Vector3.down), 4.2f, out var down) && down.point.y - s.position.y > -.1f)
                    Add(down.point.y - s.position.y > .08f ? "路面上の段差・突起" : "路面と同じ高さの床(重なり)",
                        down.collider, p, down.point.y - s.position.y, down.point, "lane" + lane.ToString("0.0"));
                // Model surface just below the road: a gap means the road visibly floats over the floor.
                if (First(new Ray(o + Vector3.up * .05f, Vector3.down), 6f, out var below) && below.distance - .05f > .3f)
                    Add("路面の下に隙間（浮き）", below.collider, p, below.distance - .05f, below.point, "lane" + lane.ToString("0.0"));
            }
        }
        var md = new StringBuilder("# 車高ray確認（CoursePlayabilityProbe）\n\n| 種類 | Collider | 区間(m) | 件数 | 値の範囲(m) | 高さ/車線 | 代表点 |\n|---|---|---|---:|---|---|---|\n");
        foreach (var r in hits.Values.OrderBy(r => r.kind).ThenByDescending(r => r.count))
            md.AppendLine($"| {r.kind} | {r.path} | {r.first:0.0}–{r.last:0.0} | {r.count} | {r.min:0.00}–{r.max:0.00} | {r.heights.Trim()} | ({r.at.x:0.0},{r.at.y:0.0},{r.at.z:0.0}) |");
        File.WriteAllText(System.IO.Path.Combine(Output, "probe.md"), md.ToString(), new UTF8Encoding(true));
        Debug.Log("COURSE_PROBE_DONE " + hits.Count);
    }

    /// <summary>Dumps raw side-ray hits on one collider (env COURSE_PROBE_TARGET) with the road frame at each sample.</summary>
    public static void DumpTarget()
    {
        string target = System.Environment.GetEnvironmentVariable("COURSE_PROBE_TARGET") ?? "Plane.017";
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
        var course = Object.FindFirstObjectByType<RaceCourse>();
        course.RebuildCache();
        Physics.SyncTransforms();
        var csv = new StringBuilder("progress,height,side,center_x,center_y,center_z,right_x,right_y,right_z,half,hit_x,hit_y,hit_z,normal_x,normal_y,normal_z,distance,triangle,mesh\n");
        for (float p = 0; p < course.TotalLength; p += .5f)
        {
            if (!course.TryGetSampleAtProgress(p, out var s)) continue;
            float half = Vector3.Distance(s.leftEdge, s.rightEdge) * .5f;
            Vector3 right = Vector3.ProjectOnPlane(s.right, Vector3.up).normalized;
            foreach (float h in Heights)
                foreach (int side in new[] { -1, 1 })
                    foreach (var hit in Physics.RaycastAll(new Ray(s.position + Vector3.up * h, right * side), half + 8f, ~0, QueryTriggerInteraction.Ignore))
                        if (hit.collider.name == target)
                            csv.AppendLine(string.Join(",", p, h, side, s.position.x, s.position.y, s.position.z, s.right.x, s.right.y, s.right.z, half,
                                hit.point.x, hit.point.y, hit.point.z, hit.normal.x, hit.normal.y, hit.normal.z, hit.distance, hit.triangleIndex,
                                (hit.collider as MeshCollider)?.sharedMesh?.name));
        }
        File.WriteAllText(System.IO.Path.Combine(Output, "target-" + target + ".csv"), csv.ToString(), new UTF8Encoding(false));
        var reach = typeof(CourseBuildingClearance).GetMethod("ReadBarrierReach", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).Invoke(null, null);
        Debug.Log("COURSE_PROBE_TARGET_DONE " + target + " clearanceReach=" + reach);
    }

    /// <summary>Exports world-space collider triangles near the road so the geometry can be studied offline.</summary>
    public static void ExportNearRoad()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
        var course = Object.FindFirstObjectByType<RaceCourse>();
        course.RebuildCache();
        var grid = new Dictionary<Vector2Int, List<Vector3>>();
        for (float p = 0; p < course.TotalLength; p += 1f)
        {
            course.TryGetSampleAtProgress(p, out var s);
            var key = new Vector2Int(Mathf.FloorToInt(s.position.x / 10f), Mathf.FloorToInt(s.position.z / 10f));
            if (!grid.TryGetValue(key, out var list)) grid[key] = list = new List<Vector3>();
            list.Add(s.position);
        }
        bool Near(Vector3 v)
        {
            var c = new Vector2Int(Mathf.FloorToInt(v.x / 10f), Mathf.FloorToInt(v.z / 10f));
            for (int x = -2; x <= 2; x++) for (int z = -2; z <= 2; z++)
                if (grid.TryGetValue(c + new Vector2Int(x, z), out var list))
                    foreach (var q in list)
                        if (new Vector2(v.x - q.x, v.z - q.z).sqrMagnitude < 18f * 18f && v.y > q.y - 1.5f && v.y < q.y + 6f) return true;
            return false;
        }
        var obj = new StringBuilder("# world-space MeshCollider triangles within 18m (XZ) and -1.5..+6m (Y) of the rounded road centerline\n");
        int vertexBase = 1, groups = 0;
        foreach (var c in Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None))
        {
            if (!c.enabled || c.sharedMesh == null || c.GetComponentInParent<RaceBarrierPath>() != null) continue;
            var mesh = c.sharedMesh; var verts = mesh.vertices; var tris = mesh.triangles;
            var world = verts.Select(v => c.transform.TransformPoint(v)).ToArray();
            var keep = new List<int>();
            for (int i = 0; i < tris.Length; i += 3)
                if (Near(world[tris[i]]) || Near(world[tris[i + 1]]) || Near(world[tris[i + 2]]) ||
                    Near((world[tris[i]] + world[tris[i + 1]] + world[tris[i + 2]]) / 3f)) keep.Add(i);
            if (keep.Count == 0) continue;
            groups++;
            string path = c.transform.name; for (var t = c.transform.parent; t != null; t = t.parent) path = t.name + "/" + path;
            obj.AppendLine("g " + path.Replace(' ', '_'));
            foreach (int i in keep)
            {
                for (int k = 0; k < 3; k++) { var v = world[tris[i + k]]; obj.AppendLine($"v {v.x:0.###} {v.y:0.###} {v.z:0.###}"); }
                obj.AppendLine($"f {vertexBase} {vertexBase + 1} {vertexBase + 2}");
                vertexBase += 3;
            }
        }
        File.WriteAllText(System.IO.Path.Combine(Output, "near-road-colliders.obj"), obj.ToString(), new UTF8Encoding(false));
        Debug.Log("COURSE_EXPORT_DONE groups=" + groups + " triangles=" + (vertexBase - 1) / 3);
    }

    /// <summary>Logs generated road triangles whose upward normal is not positive (folds or degenerate).</summary>
    public static void FindFolds()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
        var course = Object.FindFirstObjectByType<RaceCourse>();
        course.RebuildCache(); course.RebuildRoad();
        Mesh road = course.GetComponentInChildren<MeshCollider>().sharedMesh;
        var v = road.vertices; var t = road.triangles;
        for (int i = 0; i < t.Length; i += 3)
        {
            Vector3 n = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
            if (n.y > 0f) continue;
            Vector3 c = course.transform.TransformPoint((v[t[i]] + v[t[i + 1]] + v[t[i + 2]]) / 3f);
            Debug.Log($"COURSE_FOLD tri={i / 3} n={n} area={n.magnitude * .5f:0.000000} at={c} verts={course.transform.TransformPoint(v[t[i]])} {course.transform.TransformPoint(v[t[i + 1]])} {course.transform.TransformPoint(v[t[i + 2]])}");
        }
        Debug.Log("COURSE_FOLD_DONE");
    }

    /// <summary>Renders chase-height stills at distances from the start (env COURSE_PROBE_SHOTS, metres; COURSE_PROBE_SHOT_DIR).</summary>
    public static void CaptureSpots()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
        var course = Object.FindFirstObjectByType<RaceCourse>();
        course.RebuildCache();
        string dir = System.Environment.GetEnvironmentVariable("COURSE_PROBE_SHOT_DIR") ?? System.IO.Path.Combine(Output, "shots");
        Directory.CreateDirectory(dir);
        string list = System.Environment.GetEnvironmentVariable("COURSE_PROBE_SHOTS") ?? "100,150,200,255,265,280,295,325,345";
        float length = course.TotalLength;
        // Resolve the moved grid instead of retaining the old course's progress.
        CheckpointSensor start = Object.FindObjectsByType<CheckpointSensor>(FindObjectsSortMode.None)
            .OrderBy(c => c.CheckpointIndex).FirstOrDefault();
        if (start == null) throw new System.InvalidOperationException("Start checkpoint is missing.");
        float startProgress = course.GetProgressDistance(start.transform.position);
        course.TryGetSampleAtProgress(startProgress, out var startSample);
        float direction = Vector3.Dot(start.transform.forward, startSample.forward) < 0f ? -1f : 1f;
        float Progress(float fromStart) => Mathf.Repeat(startProgress + direction * fromStart, length);
        var go = new GameObject("Probe Camera") { hideFlags = HideFlags.HideAndDontSave };
        var cam = go.AddComponent<Camera>();
        cam.fieldOfView = 70f; cam.nearClipPlane = .1f; cam.farClipPlane = 1500f;
        var rt = new RenderTexture(1280, 720, 24);
        cam.targetTexture = rt;
        var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        foreach (string item in list.Split(','))
        {
            float d = float.Parse(item.Trim(), System.Globalization.CultureInfo.InvariantCulture);
            course.TryGetSampleAtProgress(Progress(d - 7f), out var eye);
            course.TryGetSampleAtProgress(Progress(d + 18f), out var look);
            go.transform.position = eye.position + Vector3.up * 2.6f;
            go.transform.rotation = Quaternion.LookRotation(look.position + Vector3.up * 1f - go.transform.position, Vector3.up);
            cam.Render();
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes(System.IO.Path.Combine(dir, "start+" + d.ToString("000") + "m.png"), tex.EncodeToPNG());
        }
        Object.DestroyImmediate(go); Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
        Debug.Log("COURSE_PROBE_SHOTS_DONE " + dir);
    }

    /// <summary>Lists physics-space triangles of one collider (COURSE_PROBE_TARGET) within 3 m of COURSE_PROBE_POINT "x,y,z".</summary>
    public static void DumpTriangles()
    {
        string target = System.Environment.GetEnvironmentVariable("COURSE_PROBE_TARGET") ?? "Plane.017";
        var parts = (System.Environment.GetEnvironmentVariable("COURSE_PROBE_POINT") ?? "625.883,10.151,588.07").Split(',');
        var point = new Vector3(float.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
            float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture), float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture));
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
        var course = Object.FindFirstObjectByType<RaceCourse>();
        course.RebuildCache();
        var csv = new StringBuilder("collider,layer,enabled,mesh,ax,ay,az,bx,by,bz,cx,cy,cz,ny\n");
        foreach (var c in Object.FindObjectsByType<MeshCollider>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (c.sharedMesh == null || (c.name != target && c.transform.parent?.name != target)) continue;
            // PhysX uses position/rotation/lossyScale, not the full (possibly sheared) matrix.
            var m = Matrix4x4.TRS(c.transform.position, c.transform.rotation, c.transform.lossyScale);
            var v = c.sharedMesh.vertices; var t = c.sharedMesh.triangles;
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 a = m.MultiplyPoint3x4(v[t[i]]), b = m.MultiplyPoint3x4(v[t[i + 1]]), d = m.MultiplyPoint3x4(v[t[i + 2]]);
                var bounds = new Bounds(a, Vector3.zero); bounds.Encapsulate(b); bounds.Encapsulate(d);
                if (bounds.SqrDistance(point) > 9f) continue;
                Vector3 n = Vector3.Cross(b - a, d - a).normalized;
                csv.AppendLine(string.Join(",", c.name, c.gameObject.layer, c.enabled, c.sharedMesh.name, a.x, a.y, a.z, b.x, b.y, b.z, d.x, d.y, d.z, n.y));
            }
        }
        File.WriteAllText(System.IO.Path.Combine(Output, "triangles-" + target + ".csv"), csv.ToString(), new UTF8Encoding(false));
        Debug.Log("COURSE_PROBE_TRIANGLES_DONE " + target);
    }
}
