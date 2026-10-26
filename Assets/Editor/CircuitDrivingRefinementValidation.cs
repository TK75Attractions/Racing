using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>Focused production-component regressions for the October circuit refinements.</summary>
public static class CircuitDrivingRefinementValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Racing/Validate Circuit Rocket Acceleration")]
    public static void RunRocket()
    {
        RaceItemValidation.Run();
        GameObject root = new GameObject("Rocket landing QA");
        GameObject inactive = new GameObject("Inactive rocket manager"); inactive.SetActive(false);
        Gmanager previous = Gmanager.Control;
        try
        {
            root.transform.position = new Vector3(10000f, 100f, 10000f);
            RaceCourse course = root.AddComponent<RaceCourse>();
            SetPath(course, new[] { Vector3.zero, Vector3.forward * 100, Vector3.forward * 112, Vector3.forward * 400 }, 1);
            Gmanager manager = inactive.AddComponent<Gmanager>(); manager.course = course; Gmanager.Control = manager;
            GameObject car = new GameObject("Rocket contact QA", typeof(Rigidbody), typeof(CarItemEffects));
            car.transform.SetParent(root.transform, false);
            var wheel = new GameObject("Contact probe", typeof(GroundCheck)); wheel.transform.SetParent(car.transform, false);
            Call(wheel.GetComponent<GroundCheck>(), "Awake");
            CarItemEffects effects = car.GetComponent<CarItemEffects>(); Call(effects, "Awake");
            Rigidbody body = car.GetComponent<Rigidbody>(); body.useGravity = false;
            // Contact uses a real raycast, including during autopilot when no TireForce tick runs.
            var floor = new GameObject("Landing", typeof(BoxCollider)); floor.transform.SetParent(root.transform, false);
            floor.transform.localPosition = new Vector3(0, -.5f, 256); floor.GetComponent<BoxCollider>().size = new Vector3(20, 1, 288);
            body.position = root.transform.position + new Vector3(0, .5f, 118); Physics.SyncTransforms();
            effects.UseItem(RaceItemType.Rocket, null);
            for (int i = 0; i < 100; i++) effects.ApplyRocketAutopilot(.02f);
            Require(Mathf.Abs(body.linearVelocity.magnitude * 3.6f - 240f) < .1f,
                "A grounded pickup 6m after the gap must reach 240 km/h, rather than remain at 79.2.");
            body.position = root.transform.position + new Vector3(0, 5, 118); Physics.SyncTransforms();
            effects.ApplyRocketAutopilot(.02f);
            Require(body.linearVelocity.magnitude <= 22.01f, "Airborne landing overshoot remains protected.");
            Set(effects, "rocketTimeRemaining", 0f); body.linearVelocity = new Vector3(0, -5, 65);
            effects.LimitRocketJumpMomentum();
            Require(Mathf.Abs(body.linearVelocity.z - 22) < .01f && body.linearVelocity.y == -5,
                "Expiry in flight retains the horizontal limit without slowing the fall.");
            effects.UseItem(RaceItemType.Rocket, null);
            body.position = root.transform.position + new Vector3(0, .5f, 85); Physics.SyncTransforms();
            effects.ApplyRocketAutopilot(.02f);
            Require(Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up).magnitude <= 22.01f, "Takeoff braking remains active.");
            Debug.Log("CIRCUIT_ROCKET_PASS: landed pickup=240km/h; approach/flight=79.2km/h; expiry preserves fall; fresh contacts release cap.");
        }
        finally { Gmanager.Control = previous; UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(inactive); }
    }

    [MenuItem("Racing/Validate Circuit Lap Coverage")]
    public static void RunLaps()
    {
        SkillGameplayValidation.Run();
        var root = new GameObject("Wide lap QA");
        try
        {
            RaceCourse course = root.AddComponent<RaceCourse>();
            SetPath(course, new[] { Vector3.zero, Vector3.forward * 200, new Vector3(200, 0, 200), Vector3.right * 200 }, -1);
            var so = new SerializedObject(course); so.FindProperty("closedLoop").boolValue = true;
            so.FindProperty("generateRoad").boolValue = true;
            so.FindProperty("road.generateBarriers").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo(); course.RebuildCache(); course.RebuildRoad();
            var goal = new GameObject("Goal reference", typeof(GoalSensor)); goal.transform.SetParent(root.transform);
            goal.transform.position = Vector3.forward * 40;
            LapManager laps = root.AddComponent<LapManager>(); Set(laps, "raceCourse", course);
            var grid = new GameObject("Grid"); grid.transform.SetParent(root.transform); grid.transform.position = Vector3.forward * 41;
            var car = new GameObject("Lap car", typeof(Rigidbody)); car.transform.SetParent(root.transform);
            Rigidbody body = car.GetComponent<Rigidbody>(); body.useGravity = false; body.position = grid.transform.position;
            laps.RegisterCar(body, grid.transform);
            float start = course.GetProgressDistance(goal.transform.position);
            for (int lap = 0; lap < 2; lap++)
            {
                for (float d = 2; d < course.TotalLength + 4; d += 2)
                {
                    course.TryGetSampleAtProgress(start + d, out var sample);
                    // Ride the outside barrier across several virtual gates, then cross
                    // the finish above the old goal trigger. No trigger callback is sent.
                    bool wall = d > 80 && d < 170 || d > 770;
                    body.position = sample.position + sample.right * (wall ? 11.2f : 0) + Vector3.up * (wall ? 3f : .6f);
                    body.linearVelocity = sample.forward * (240f / 3.6f); Physics.SyncTransforms();
                    Call(laps, "FixedUpdate");
                }
                Require(laps.GetCarData(body).lapCount == lap + 1, "Wall riding and a missed narrow goal trigger must still count once.");
            }
            Require(laps.GetCarData(body).isFinished, "Two valid wide circuits finish the race.");
            laps.RegisterCar(body, grid.transform);
            for (int i = 0; i < 3; i++)
            {
                course.TryGetSampleAtProgress(start + i * course.TotalLength / 3, out var sample);
                body.position = sample.position + Vector3.up; Physics.SyncTransforms(); Call(laps, "FixedUpdate");
            }
            Require(laps.GetCarData(body).lapCount == 0, "Teleporting between thirds cannot earn a lap.");
            // Nearby parallel lanes can move a projection past a gate despite only
            // a few meters of real motion. That must not award the skipped detour.
            SetPath(course, new[] { Vector3.zero, Vector3.forward * 100, new Vector3(4,0,100),
                new Vector3(4,0,80), new Vector3(100,0,80), Vector3.right * 100 }, -1);
            so = new SerializedObject(course); so.FindProperty("closedLoop").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo(); course.RebuildCache(); goal.transform.position = Vector3.zero;
            var shortcutLaps = root.AddComponent<LapManager>(); Set(shortcutLaps, "raceCourse", course);
            grid.transform.position = Vector3.forward; body.position = grid.transform.position;
            shortcutLaps.RegisterCar(body, grid.transform);
            for (int z = 2; z <= 90; z += 2) { body.position = new Vector3(0,.6f,z); shortcutLaps.GetRaceProgressDistance(body); }
            double beforeShortcut = shortcutLaps.GetCarData(body).traversal.Travel;
            body.position = new Vector3(4,.6f,90); shortcutLaps.GetRaceProgressDistance(body);
            Require(Math.Abs(shortcutLaps.GetCarData(body).traversal.Travel - beforeShortcut) < .0001,
                "A 4m lane hop must not award the 24m detour or its virtual gate.");
            Debug.Log("CIRCUIT_LAP_PASS: two full circuits; barrier-width gates; high finish sweep; no trigger dependency; teleport and nearby-lane shortcuts blocked.");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    private sealed class Input : IDriveInputSource
    {
        public int PlayerIndex => 0;
        public string DeviceId => "CIRCUIT_QA";
        public bool IsConnected { get; set; } = true;
        public DriveInputState CurrentState { get; set; }
        public void UpdateInput(float dt) { }
        public void Dispose() { }
    }
    [MenuItem("Racing/Validate Circuit Jump Run-Up")]
    public static void RunJump()
    {
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene);
        var root = new GameObject("Jump run-up QA");
        var inactive = new GameObject("Inactive jump manager"); inactive.SetActive(false);
        Gmanager previous = Gmanager.Control; SimulationMode mode = Physics.simulationMode;
        try
        {
            Physics.simulationMode = SimulationMode.Script;
            var course = root.AddComponent<RaceCourse>();
            SetPath(course, new[] { Vector3.zero, Vector3.forward * 100, Vector3.forward * 112, Vector3.forward * 400 }, 1);
            var so = new SerializedObject(course); so.FindProperty("generateRoad").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo(); course.RebuildCache(); course.RebuildRoad();
            var guidance = course.transform.Find(RaceCourseRoad.RootName + "/Jump Guidance");
            Require(guidance != null && guidance.GetComponentsInChildren<MeshFilter>().Length == 2 &&
                guidance.GetComponentsInChildren<Collider>().Length == 0, "Ramp edges have contrast backing and add no collision.");
            var manager = inactive.AddComponent<Gmanager>(); manager.course = course; manager.state = Gmanager.State.Game; Gmanager.Control = manager;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/car/car2.prefab");
            var car = UnityEngine.Object.Instantiate(prefab, new Vector3(0, 1.3f, 70), Quaternion.identity, root.transform);
            var body = car.GetComponent<Rigidbody>(); body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            foreach (var ground in car.GetComponentsInChildren<GroundCheck>()) Call(ground, "Awake");
            foreach (var tire in car.GetComponentsInChildren<TireForce>()) Call(tire, "Awake");
            var mover = car.GetComponent<DebugMover>(); Call(mover, "Awake");
            var input = new Input(); var gate = new PlayerDriveInputGate(input); mover.SetInputSource(gate);
            Physics.SyncTransforms();
            for (int i = 0; i < 100; i++) Physics.Simulate(.02f);
            body.linearVelocity = Vector3.zero;
            for (int i = 0; i < 75; i++) { Call(mover, "ApplyDriving"); Physics.Simulate(.02f); }
            Require(body.position.z > 80 && body.linearVelocity.z > 12,
                "A stopped, connected car with a released pedal must naturally build a jump run-up. position=" + body.position + " velocity=" + body.linearVelocity);
            body.position = new Vector3(0, .8f, 70); body.linearVelocity = Vector3.zero; Physics.SyncTransforms();
            gate.SetBlocked(true);
            for (int i = 0; i < 30; i++) { Call(mover, "ApplyDriving"); Physics.Simulate(.02f); }
            Require(Mathf.Abs(body.linearVelocity.z) < .1f, "A blocked pedal gate must never auto-accelerate.");
            gate.SetBlocked(false); input.IsConnected = false;
            for (int i = 0; i < 30; i++) { Call(mover, "ApplyDriving"); Physics.Simulate(.02f); }
            Require(Mathf.Abs(body.linearVelocity.z) < .1f, "Disconnected cars must never auto-accelerate.");
            input.IsConnected = true; _ = gate.CurrentState;
            var npc = car.AddComponent<NpcRaceDriver>(); npc.enabled = false;
            body.position = new Vector3(0, .8f, 70); body.linearVelocity = Vector3.zero; Physics.SyncTransforms();
            for (int i = 0; i < 30; i++) { Call(mover, "ApplyDriving"); Physics.Simulate(.02f); }
            Require(Mathf.Abs(body.linearVelocity.z) < .1f, "CPU launch spacing may deliberately wait; human run-up assistance must not override it.");
            Debug.Log("CIRCUIT_JUMP_PASS: production car starts from rest on run-up with pedal released; blocked/disconnected/CPU waiting remain stopped; flush contrast outlines have no collider.");
        }
        finally { Physics.simulationMode = mode; Gmanager.Control = previous; UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(inactive); }
    }

    public static void RunAll()
    {
        RunRocket(); RunLaps(); RunJump(); SlipstreamValidation.Run();
        CircuitVisualRefinementValidation.RunTube();
        CourseBarrierValidation.Run();
        TubeWallRecoveryValidation.RunFullBatch();
        Debug.Log("CIRCUIT_FULL_REGRESSION_PASS");
    }

    private static void SetPath(RaceCourse course, Vector3[] positions, int jump)
    {
        var so = new SerializedObject(course);
        so.FindProperty("closedLoop").boolValue = false;
        so.FindProperty("generateRoad").boolValue = false;
        so.FindProperty("cornerRoundingDistance").floatValue = 0;
        so.FindProperty("slopeBlendDistance").floatValue = 0;
        var points = so.FindProperty("waypoints"); points.arraySize = positions.Length;
        for (int i = 0; i < positions.Length; i++)
        {
            var point = points.GetArrayElementAtIndex(i);
            point.FindPropertyRelative("position").vector2Value = new Vector2(positions[i].x, positions[i].z);
            point.FindPropertyRelative("height").floatValue = positions[i].y;
            point.FindPropertyRelative("width").floatValue = 20;
            point.FindPropertyRelative("jumpToNext").boolValue = i == jump;
        }
        so.ApplyModifiedPropertiesWithoutUndo(); course.RebuildCache();
    }
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
    private static void Call(object target, string name) => target.GetType().GetMethod(name, Private).Invoke(target, null);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
