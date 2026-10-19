using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Exercises tube containment and the production recovery loop with controlled contact states.</summary>
public static class TubeWallRecoveryValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Vector3 Origin = new Vector3(10000f, 100f, 10000f);
    private sealed class Input : IDriveInputSource
    {
        public int PlayerIndex => 0;
        public string DeviceId => "TUBE_RECOVERY_VALIDATION";
        public bool IsConnected => true;
        public DriveInputState CurrentState { get; set; }
        public void UpdateInput(float dt) { }
        public void Dispose() { }
    }

    public static void RunFullBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Run the full suite in an isolated batch editor.");
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        ValidateAuthoredTube();
        Run();
        SlopeTubeDrivingValidation.Run();
    }

    private static void ValidateAuthoredTube()
    {
        RaceCourse course = UnityEngine.Object.FindFirstObjectByType<RaceCourse>();
        MeshCollider tube = new SerializedObject(course).FindProperty("road.tubeInterior").objectReferenceValue as MeshCollider;
        Require(tube != null, "The authored scene must have a configured tube.");
        TubeDriveSurface.Ensure(tube); Physics.SyncTransforms();
        int positions = 0;
        for (float progress = 0f; progress < course.TotalLength; progress += 10f)
        {
            course.TryGetSampleAtProgress(progress, out var sample);
            if (!RaceCourseBarriers.IsInsideTube(sample.position, sample.right, tube)) continue;
            Vector3 origin = sample.position + Vector3.up * .25f;
            foreach (Vector3 direction in new[] { -sample.right, sample.right, Vector3.up })
            {
                Require(tube.Raycast(new Ray(origin, direction), out var hit, 40f), "An interior tube sample needs a wall.");
                Vector3 inside = hit.point - direction * .6f;
                Require(course.IsPointInsideTube(inside), $"Authored tube wall interior rejected at {inside}.");
                positions++;
            }
        }
        Require(positions > 20, "Authored tube validation must cover multiple road samples.");
        Debug.Log($"AUTHORED_TUBE_CONTAINMENT_PASS: {positions} side/ceiling positions.");
    }

    [MenuItem("Racing/Validate Tube Wall Recovery")]
    public static void Run()
    {
        RaceCourseValidation.Run();
        GameObject root = new GameObject("Tube recovery validation");
        GameObject managerRoot = new GameObject("Inactive tube recovery manager");
        managerRoot.SetActive(false);
        Gmanager previous = Gmanager.Control;
        Mesh mesh = MakeTube();
        try
        {
            Gmanager manager = managerRoot.AddComponent<Gmanager>();
            manager.state = Gmanager.State.Game; Gmanager.Control = manager;
            GameObject tube = new GameObject("Configured tube", typeof(MeshCollider));
            tube.transform.SetParent(root.transform); tube.transform.position = Origin;
            MeshCollider collider = tube.GetComponent<MeshCollider>(); collider.sharedMesh = mesh;
            TubeDriveSurface.Ensure(collider);
            RaceCourse course = root.AddComponent<RaceCourse>();
            var settings = new SerializedObject(course);
            settings.FindProperty("generateRoad").boolValue = false;
            settings.FindProperty("closedLoop").boolValue = false;
            settings.FindProperty("cornerRoundingDistance").floatValue = 0f;
            settings.FindProperty("slopeBlendDistance").floatValue = 0f;
            settings.FindProperty("road.tubeInterior").objectReferenceValue = collider;
            var points = settings.FindProperty("waypoints"); points.arraySize = 2;
            for (int i = 0; i < 2; i++)
            {
                var point = points.GetArrayElementAtIndex(i);
                point.FindPropertyRelative("position").vector2Value = new Vector2(Origin.x, Origin.z + i * 100f);
                point.FindPropertyRelative("height").floatValue = Origin.y;
                point.FindPropertyRelative("width").floatValue = 10f;
            }
            settings.ApplyModifiedPropertiesWithoutUndo(); course.RebuildCache();
            Physics.SyncTransforms();
            foreach (Vector3 inside in new[] { new Vector3(11f, 12f, 30f), new Vector3(-11f, 12f, 30f),
                new Vector3(0f, 23f, 30f), new Vector3(11f, 12f, .1f), new Vector3(11f, 12f, 99.9f) })
                Require(course.IsPointInsideDrivingArea(Origin + inside), "Tube sides, ceiling and portals are inside the driving area.");
            foreach (Vector3 outside in new[] { new Vector3(11f, 1f, 30f), new Vector3(11f, 23f, 30f),
                new Vector3(13f, 12f, 30f), new Vector3(11f, 12f, -1f), new Vector3(11f, 12f, 101f) })
                Require(!course.IsPointInsideDrivingArea(Origin + outside), "The tube exterior must remain outside, including gaps inside its world bounds.");
            var marker = tube.GetComponent<TubeDriveSurface>(); marker.enabled = false;
            Require(!course.IsPointInsideTube(Origin + new Vector3(11f, 12f, 30f)), "Disabled tube markers are excluded.");
            marker.enabled = true; collider.enabled = false;
            Require(!course.IsPointInsideTube(Origin + new Vector3(11f, 12f, 30f)), "Disabled tube colliders are excluded.");
            collider.enabled = true;
            LapManager laps = root.AddComponent<LapManager>(); Set(laps, "raceCourse", course);
            GameObject car = new GameObject("Tube recovery car", typeof(DebugMover), typeof(BoxCollider), typeof(CarResetter));
            car.transform.SetParent(root.transform);
            DebugMover mover = car.GetComponent<DebugMover>(); Call(mover, "Awake");
            CarResetter resetter = car.GetComponent<CarResetter>(); Call(resetter, "Awake");
            Rigidbody body = car.GetComponent<Rigidbody>(); body.useGravity = false;
            var input = new Input(); resetter.SetInputSource(input);
            body.position = Origin + new Vector3(0f, 1f, 30f);
            laps.RegisterCar(body); laps.GetCarData(body).lapCount = 1; resetter.SetCourse(course, laps);
            MethodInfo contact = typeof(CarResetter).GetMethod("RememberWallContact", Private, null,
                new[] { typeof(Collider), typeof(Vector3) }, null);
            int frames = Mathf.CeilToInt(4.2f / Time.fixedDeltaTime);
            int recovered = 0;
            foreach (float angle in new[] { -150f, -90f, -60f, -45f, 0f, 45f, 60f, 90f, 150f, 180f })
            {
                float radians = angle * Mathf.Deg2Rad;
                Vector3 normal = new Vector3(-Mathf.Sin(radians), Mathf.Cos(radians), 0f);
                Vector3 pinned = Origin + new Vector3(-normal.x * 11.3f, 12f - normal.y * 11.3f, 30f);
                void Prepare()
                {
                    Call(resetter, "OnDisable"); Set(mover, "inputSuppressedUntil", float.NegativeInfinity);
                    car.transform.SetPositionAndRotation(pinned, Quaternion.AngleAxis(angle, Vector3.forward));
                    body.position = pinned; body.rotation = car.transform.rotation; body.WakeUp();
                    input.CurrentState = new DriveInputState { pedal = 1f };
                }
                void Tick()
                {
                    contact.Invoke(resetter, new object[] { collider, normal }); Call(resetter, "FixedUpdate");
                }
                Prepare();
                for (int i = 0; i < frames * 2; i++)
                { body.position = pinned + Vector3.forward * (i * Time.fixedDeltaTime * 2f); Tick(); }
                Require(resetter.AutomaticRecoveryCount == recovered, $"Bank {angle}: smooth tube driving must not recover or count as overturned (reason={resetter.RecoveryReason}, position={body.position}).");
                Prepare(); input.CurrentState = DriveInputState.Neutral;
                for (int i = 0; i < frames; i++) Tick();
                Require(resetter.AutomaticRecoveryCount == recovered, $"Bank {angle}: neutral input must not request recovery.");
                Prepare();
                for (int i = 0; i < frames && resetter.AutomaticRecoveryCount == recovered; i++)
                {
                    body.position = pinned + Vector3.forward * (i % 2 == 0 ? .15f : -.15f);
                    body.linearVelocity = Vector3.forward * 2f; Tick();
                }
                recovered++;
                Require(resetter.AutomaticRecoveryCount == recovered && resetter.RecoveryReason == "stuck" &&
                    course.IsPointInsideCourse(body.position) && body.position.y < Origin.y + 2f && laps.GetCarData(body).lapCount == 1,
                    $"Bank {angle}: trapped tube contacts must return to safe road without changing laps.");
                if (angle == 45f)
                {
                    Prepare(); input.CurrentState = new DriveInputState { pedal = -1f };
                    contact.Invoke(resetter, new object[] { collider, normal });
                    Set(resetter, "lastWallContactTime", Time.fixedTime - .2f); body.Sleep();
                    Require(body.IsSleeping(), "The tube sleep trial must use a sleeping body.");
                    for (int i = 0; i < frames && resetter.AutomaticRecoveryCount == recovered; i++) Call(resetter, "FixedUpdate");
                    recovered++;
                    Require(resetter.AutomaticRecoveryCount == recovered && resetter.RecoveryReason == "stuck",
                        "A sleeping tube contact must recover with reverse input and no collision-stay messages.");
                }
            }
            Call(resetter, "OnDisable"); Set(mover, "inputSuppressedUntil", float.NegativeInfinity);
            body.position = Origin + new Vector3(0f, .7f, 30f);
            car.transform.rotation = Quaternion.AngleAxis(180f, Vector3.forward);
            body.rotation = car.transform.rotation; body.WakeUp();
            input.CurrentState = DriveInputState.Neutral;
            for (int i = 0; i < frames && resetter.AutomaticRecoveryCount == recovered; i++)
            { contact.Invoke(resetter, new object[] { collider, Vector3.up }); Call(resetter, "FixedUpdate"); }
            Require(resetter.AutomaticRecoveryCount == recovered + 1 && resetter.RecoveryReason == "overturned",
                "An overturned car on the tube floor must still recover.");
            Debug.Log("TUBE_WALL_RECOVERY_PASS: 10 banks including ceiling, smooth/neutral/jitter/sleep states, reverse input, safe road and laps, true overturn, tube interior/exterior, portals and disabled surfaces.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(managerRoot);
            UnityEngine.Object.DestroyImmediate(mesh); Gmanager.Control = previous;
        }
    }

    private static Mesh MakeTube()
    {
        const int sections = 96;
        var vertices = new List<Vector3>(); var triangles = new List<int>();
        for (int i = 0; i <= sections; i++)
        {
            float angle = i * Mathf.PI * 2f / sections;
            Vector3 point = new Vector3(Mathf.Sin(angle) * 12f, 12f - Mathf.Cos(angle) * 12f, 0f);
            vertices.Add(point); vertices.Add(point + Vector3.forward * 100f);
            if (i == 0) continue;
            int a = (i - 1) * 2, b = i * 2;
            triangles.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1, a, a + 1, b, a + 1, b + 1, b });
        }
        var mesh = new Mesh { name = "Double-sided tube recovery validation" };
        mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return mesh;
    }
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
    private static void Call(object target, string method) => target.GetType().GetMethod(method, Private).Invoke(target, null);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
