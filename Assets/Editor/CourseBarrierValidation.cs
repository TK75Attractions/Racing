using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Coverage and physical containment checks for the current course, including its jump and portals.</summary>
public static class CourseBarrierValidation
{
    [Serializable] private sealed class Report
    {
        public int goalLaps, wallPaths, exteriorSamples, tubeSamples, missingSideHits, interiorWallNodes, portalTubeSideHits;
        public float courseLength, exteriorMeters, tubeMeters, maximumPhysicsExcursion, minimumPhysicsHeight;
        public int physicsTrials;
    }
    [MenuItem("Racing/Validate Course Edge Barriers And Two Laps")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        RaceBarrierPathValidation.Run();
        RaceCourseRoadValidation.Run();
        PlayerAudioValidation.Run();
        TubeCourseValidation.Run();
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        RaceCourse course = UnityEngine.Object.FindFirstObjectByType<RaceCourse>();
        LapManager laps = UnityEngine.Object.FindFirstObjectByType<LapManager>();
        course.RebuildCache(); course.RebuildRoad(); Physics.SyncTransforms();
        var report = new Report { goalLaps = laps.GoalLap, courseLength = course.TotalLength };
        Require(report.goalLaps == 2, "SampleScene must finish after two laps.");
        var settings = new SerializedObject(course).FindProperty("road");
        MeshCollider tube = settings.FindPropertyRelative("tubeInterior").objectReferenceValue as MeshCollider;
        Require(tube != null && settings.FindPropertyRelative("generateBarriers").boolValue, "Scene barrier configuration is missing.");
        Transform root = course.transform.Find(RaceCourseRoad.RootName + "/" + RaceCourseBarriers.RootName);
        Require(root != null, "Generated edge barriers are missing.");
        RaceBarrierPath[] paths = root.GetComponentsInChildren<RaceBarrierPath>(); report.wallPaths = paths.Length;
        Require(paths.Length >= 2, "Both edges need barriers.");
        MeshCollider[] walls = root.GetComponentsInChildren<MeshCollider>();
        var centers = new List<Vector3>(); var leftEdge = new List<Vector3>(); var rightEdge = new List<Vector3>();
        course.CopyCenterPathWorld(centers); course.CopyCourseBandWorld(leftEdge, rightEdge);
        foreach (RaceBarrierPath path in paths)
        {
            MeshRenderer renderer = path.GetComponentInChildren<MeshRenderer>();
            Require(renderer.sharedMaterial == Resources.Load<Material>("RaceBarrier/M_RaceBarrier"), "Use the existing blue material.");
            MeshCollider collision = path.GetComponentInChildren<MeshCollider>();
            Require(collision != null && !collision.isTrigger && !collision.convex, "Each edge must have a solid curved wall.");
            var nodes = new List<Vector3>(); path.CopyPreviewPathWorld(nodes);
            foreach (Vector3 node in nodes)
            {
                Vector3 center = CenterAtEdge(node, centers, leftEdge, rightEdge, out float edgeDistance);
                float raw = course.GetProgressDistance(center);
                Require(edgeDistance < .01f,
                    "Barrier base drifted away from the road edge: " + node);
                // Well inside the tunnel, no blue wall base should be present.
                if (TubeCeiling(tube, center) && TubeCeiling(tube, At(course, raw - 2)) && TubeCeiling(tube, At(course, raw + 2)))
                    report.interiorWallNodes++;
            }
        }
        for (float progress = 0; progress < course.TotalLength; progress += .5f)
        {
            course.TryGetSampleAtProgress(progress, out var sample);
            bool inside = TubeCeiling(tube, sample.position);
            if (inside) { report.tubeSamples++; report.tubeMeters += .5f; }
            else { report.exteriorSamples++; report.exteriorMeters += .5f; }
            foreach (int sign in new[] { -1, 1 })
            {
                Vector3 side = sample.right * sign;
                var ray = new Ray(sample.position + Vector3.up, side);
                bool hit = false;
                foreach (MeshCollider wall in walls) if (wall.Raycast(ray, out _, sample.width * .5f + 16)) { hit = true; break; }
                // At a tangent portal the real tube side can begin before its ceiling.
                // Check the combined collision envelope, including every interior sample.
                if (!hit && tube.Raycast(ray, out _, sample.width * .5f + 16))
                { hit = true; if (!inside) report.portalTubeSideHits++; }
                if (!hit) { report.missingSideHits++; Debug.LogError("Unprotected edge at " + progress + ": " + sample.position + ", side " + sign); }
            }
        }
        Debug.Log("COURSE_BARRIER_COVERAGE: " + JsonUtility.ToJson(report));
        Require(report.exteriorSamples > 400 && report.tubeSamples > 1000, "The coverage test must include exterior and tunnel sections.");
        Require(report.interiorWallNodes == 0 && report.missingSideHits == 0, "Barrier coverage or tunnel exclusion failed.");
        // Rebuilds must release both meshes per wall, rather than leaking an invisible old wall.
        var oldMeshes = new List<Mesh>();
        foreach (MeshCollider wall in walls) oldMeshes.Add(wall.sharedMesh);
        course.RebuildRoad();
        foreach (Mesh mesh in oldMeshes) Require(mesh == null, "A rebuilt barrier leaked a collider mesh.");
        Require(course.GetComponentsInChildren<RaceBarrierPath>().Length == report.wallPaths, "Rebuilding duplicated barriers.");
        ValidateContainment(report);
        string output = Path.GetFullPath("Artifacts/CourseSafety"); Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "coverage.json"), JsonUtility.ToJson(report, true));
        CaptureViews(course, tube, output);
        Debug.Log("COURSE_EDGE_BARRIERS_PASS: " + JsonUtility.ToJson(report));
    }

    [MenuItem("Racing/Capture Course Edge Barriers")]
    public static void CaptureScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        RaceCourse course = UnityEngine.Object.FindFirstObjectByType<RaceCourse>();
        course.RebuildCache(); course.RebuildRoad(); Physics.SyncTransforms();
        MeshCollider tube = new SerializedObject(course).FindProperty("road.tubeInterior").objectReferenceValue as MeshCollider;
        string output = Path.GetFullPath("Artifacts/CourseSafety"); Directory.CreateDirectory(output);
        CaptureViews(course, tube, output);
        Debug.Log("COURSE_BARRIER_CAPTURE_PASS");
    }
    private static void CaptureViews(RaceCourse course, MeshCollider tube, string output)
    {
        Capture(new Vector3(585, 58, 665), new Vector3(620, 17, 598), Path.Combine(output, "jump-barriers.png"));
        Capture(new Vector3(441, 32, 803), new Vector3(495, 4, 737), Path.Combine(output, "start-barriers.png"));
        float portal = 0, east = float.NegativeInfinity;
        bool previous = TubeCeiling(tube, At(course, 0));
        for (float raw = .5f; raw < course.TotalLength; raw += .5f)
        {
            Vector3 point = At(course, raw); bool inside = TubeCeiling(tube, point);
            if (inside != previous && point.x > east) { east = point.x; portal = raw; }
            previous = inside;
        }
        Require(east > float.NegativeInfinity, "Cannot locate the tube portal for capture.");
        float outside = TubeCeiling(tube, At(course, portal + 2)) ? -1 : 1;
        Capture(At(course, portal + outside * 16) + Vector3.up * 2.6f,
            At(course, portal - outside * 20) + Vector3.up * 2.3f, Path.Combine(output, "tube-portal.png"));
        float underground = course.GetProgressDistance(new Vector3(672, -25, 350));
        Capture(At(course, underground) + Vector3.up * 2.5f, At(course, underground - 22) + Vector3.up * 2.5f,
            Path.Combine(output, "tube-interior.png"));
    }
    private static bool TubeCeiling(MeshCollider tube, Vector3 position) =>
        tube.Raycast(new Ray(position + Vector3.up * .25f, Vector3.up), out _, 40);
    private static Vector3 At(RaceCourse course, float raw) { course.TryGetSampleAtProgress(raw, out var sample); return sample.position; }

    private static Vector3 CenterAtEdge(Vector3 point, List<Vector3> centers, List<Vector3> left, List<Vector3> right, out float distance)
    {
        // Project onto the actual edge polyline: projecting an offset point onto the centerline
        // gives a different longitudinal position on bends and width transitions.
        float best = float.PositiveInfinity; Vector3 result = default;
        foreach (List<Vector3> edge in new[] { left, right })
        for (int i = 1; i < edge.Count; i++)
        {
            Vector3 delta = edge[i] - edge[i - 1];
            float t = delta.sqrMagnitude > 0 ? Mathf.Clamp01(Vector3.Dot(point - edge[i - 1], delta) / delta.sqrMagnitude) : 0;
            float squared = (point - Vector3.Lerp(edge[i - 1], edge[i], t)).sqrMagnitude;
            if (squared >= best) continue;
            best = squared; result = Vector3.Lerp(centers[i - 1], centers[i], t);
        }
        distance = Mathf.Sqrt(best); return result;
    }

    private static void ValidateContainment(Report report)
    {
        report.minimumPhysicsHeight = float.PositiveInfinity;
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            for (int side = -1; side <= 1; side += 2)
            {
                GameObject wall = new GameObject("Physical edge"); SceneManager.MoveGameObjectToScene(wall, scene);
                wall.AddComponent<RaceBarrierPath>().SetWorldPath(new[] { new Vector3(side * 10, 0, -100), new Vector3(side * 10, 0, 100) }, side, false, null, null, true);
            }
            GameObject floor = new GameObject("Road", typeof(BoxCollider)); SceneManager.MoveGameObjectToScene(floor, scene);
            floor.GetComponent<BoxCollider>().size = new Vector3(20, 1, 200); floor.transform.position = Vector3.down * .5f;
            foreach (int side in new[] { -1, 1 }) foreach (float height in new[] { 1f, 4f })
            {
                GameObject car = new GameObject("Impact body", typeof(BoxCollider), typeof(Rigidbody)); SceneManager.MoveGameObjectToScene(car, scene);
                car.GetComponent<BoxCollider>().size = new Vector3(2, 1, 4);
                Rigidbody body = car.GetComponent<Rigidbody>(); body.position = new Vector3(side * 7, height, -40);
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                body.linearVelocity = new Vector3(side * 25, 0, 10);
                Physics.SyncTransforms();
                PhysicsScene physics = scene.GetPhysicsScene();
                Require(physics.IsValid() && physics != Physics.defaultPhysicsScene, "Containment trials require isolated physics.");
                for (int frame = 0; frame < 300; frame++)
                {
                    physics.Simulate(.02f);
                    report.maximumPhysicsExcursion = Mathf.Max(report.maximumPhysicsExcursion, Mathf.Abs(body.position.x));
                    report.minimumPhysicsHeight = Mathf.Min(report.minimumPhysicsHeight, body.position.y);
                    Require(Mathf.Abs(body.position.x) < 24 && body.position.y > -1, "A vehicle-sized impact body escaped the side barrier.");
                }
                report.physicsTrials++;
                UnityEngine.Object.DestroyImmediate(car);
            }
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }
    private static void Capture(Vector3 eye, Vector3 target, string path)
    {
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
        GameObject cameraObject = new GameObject("Barrier review camera");
        var output = new RenderTexture(1280, 720, 24); Texture2D image = null;
        RenderTexture previous = RenderTexture.active;
        try
        {
            Camera camera = cameraObject.AddComponent<Camera>(); camera.nearClipPlane = .1f; camera.farClipPlane = 2000; camera.fieldOfView = 60;
            camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye)); camera.targetTexture = output; camera.Render();
            // The first SRP render can import assets and unload unreferenced transient objects.
            // Allocate the CPU readback only after rendering so it cannot be collected mid-frame.
            image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            RenderTexture.active = output; image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply(); File.WriteAllBytes(path, image.EncodeToPNG());
        }
        finally { RenderTexture.active = previous; output.Release(); UnityEngine.Object.DestroyImmediate(cameraObject); UnityEngine.Object.DestroyImmediate(output); UnityEngine.Object.DestroyImmediate(image); }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
