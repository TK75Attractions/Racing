using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Production car, real contacts and PhysX; only the initial conditions are placed.</summary>
public static class HighSpeedCorneringValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Vector3 Origin = new Vector3(5000, 5000, 5000);
    private sealed class Input : IDriveInputSource
    {
        public int PlayerIndex => 0;
        public string DeviceId => "Cornering QA";
        public bool IsConnected { get; set; } = true;
        public DriveInputState CurrentState { get; set; }
        public void UpdateInput(float dt) { }
        public void Dispose() { }
    }
    [Serializable] private sealed class Trial
    {
        public string name;
        public bool enabled;
        public float initialSpeed, finalSpeed, travelTurn, maxBrake, maxSlip, maxTilt, maxNormalSpeed;
        public float distance, maxLateralError, outsideSeconds;
    }
    [Serializable] private sealed class Report { public List<Trial> trials = new List<Trial>(); }
    private static RaceFeatureController features;
    private static readonly Report Results = new Report();

    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        var previous = Gmanager.Control;
        var mode = Physics.simulationMode;
        var root = new GameObject("Cornering QA manager"); root.SetActive(false);
        string folder = Environment.GetEnvironmentVariable("RACING_CORNERING_OUTPUT") ?? "/private/tmp/racing-cornering";
        Directory.CreateDirectory(folder);
        Results.trials.Clear();
        try
        {
            Physics.simulationMode = SimulationMode.Script;
            var manager = root.AddComponent<Gmanager>(); Gmanager.Control = manager; manager.state = Gmanager.State.Game;
            features = root.AddComponent<RaceFeatureController>(); features.Configure(manager);
            Set(manager, "<Features>k__BackingField", features);
            foreach (float bank in new[] { 0f, -60f, 60f })
            foreach (float speed in new[] { 40f, 60f })
            foreach (float handle in new[] { -12f, 12f })
            {
                Trial before = DriveBank(bank, speed, handle, false);
                Trial after = DriveBank(bank, speed, handle, true);
                Require(after.travelTurn * Mathf.Sign(handle) > before.travelTurn * Mathf.Sign(handle) + 3f,
                    after.name + ": steering must curve the trajectory more strongly, not only turn the body.");
                Require(after.finalSpeed < before.finalSpeed - 3f && after.maxBrake > 4f,
                    after.name + ": turning during a full boost must actually reduce speed.");
                Require(after.maxSlip < 20f && after.maxTilt < 12f && after.maxNormalSpeed < .8f,
                    after.name + ": keep grip, banked contact and the surface-normal motion stable.");
            }
            Trial straightOff = DriveBank(0, 40, 0, false), straightOn = DriveBank(0, 40, 0, true);
            Require(Mathf.Abs(straightOff.finalSpeed - straightOn.finalSpeed) < .05f && straightOn.maxBrake == 0,
                "Straight boost must keep its acceleration.");
            Trial small = DriveBank(0, 40, .07f, true);
            Require(small.maxBrake == 0 && Mathf.Abs(small.travelTurn) < .1f, "IMU center noise must not turn or brake.");
            foreach (float handle in new[] { -3f, 3f })
            {
                Trial off = DriveBank(0, 60, handle, false), on = DriveBank(0, 60, handle, true);
                Require(on.travelTurn * Mathf.Sign(handle) > off.travelTurn * Mathf.Sign(handle) + 3f && on.finalSpeed < off.finalSpeed,
                    "Small deliberate corrections must retain high-speed steering and shed excess speed too.");
            }
            ValidateIneligibleInput();
            features.SetFeature(RaceFeature.HighSpeedCornering, false);
            SteeringValidation.Run();
            DriftValidation.Run();
            Debug.Log("HIGH_SPEED_CORNERING_PASS: 31 production-car trials, boost, trajectory, +/-60 degree banks, small corrections, center noise, airborne, blocked, disconnect, live OFF and steering/drift regressions.");
        }
        finally
        {
            File.WriteAllText(Path.Combine(folder, "physics.json"), JsonUtility.ToJson(Results, true));
            UnityEngine.Object.DestroyImmediate(root); Gmanager.Control = previous; Physics.simulationMode = mode;
        }
    }

    private static Trial DriveBank(float bank, float speed, float handle, bool enabled)
    {
        features.SetFeature(RaceFeature.HighSpeedCornering, enabled);
        Quaternion rotation = Quaternion.AngleAxis(bank, Vector3.forward);
        Vector3 up = rotation * Vector3.up, forward = rotation * Vector3.forward;
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube), car = null;
        var trial = new Trial { name = $"bank={bank} speed={speed} handle={handle}", enabled = enabled, initialSpeed = speed };
        Results.trials.Add(trial);
        try
        {
            floor.transform.SetPositionAndRotation(Origin - up * .5f, rotation);
            floor.transform.localScale = new Vector3(500, 1, 500);
            UnityEngine.Object.DestroyImmediate(floor.GetComponent<BoxCollider>());
            var surface = floor.AddComponent<MeshCollider>(); surface.sharedMesh = floor.GetComponent<MeshFilter>().sharedMesh;
            TubeDriveSurface.Ensure(surface);
            car = CreateCar(Origin + up * .36f, rotation, out DebugMover mover, out Rigidbody body);
            var input = new Input { CurrentState = new DriveInputState { pedal = 1, steering = handle } };
            mover.SetInputSource(input);
            body.useGravity = false; body.linearVelocity = forward * speed;
            Call(mover, "StartDriftBoost", 30f); // Production prefab's maximum charged release.
            Physics.SyncTransforms();
            for (int step = 0; step < 50; step++)
            {
                Call(mover, "ApplyDriving");
                Call(car.GetComponent<CarStabilityController>(), "FixedUpdate");
                Physics.Simulate(.02f); Physics.SyncTransforms();
                Vector3 velocity = Vector3.ProjectOnPlane(body.linearVelocity, up);
                trial.finalSpeed = velocity.magnitude;
                trial.travelTurn = Vector3.SignedAngle(forward, velocity, up);
                trial.maxBrake = Mathf.Max(trial.maxBrake, mover.CorneringBrakingAcceleration);
                trial.maxTilt = Mathf.Max(trial.maxTilt, Vector3.Angle(body.rotation * Vector3.up, up));
                trial.maxNormalSpeed = Mathf.Max(trial.maxNormalSpeed, Mathf.Abs(Vector3.Dot(body.linearVelocity, up)));
                trial.maxSlip = Mathf.Max(trial.maxSlip, Vector3.Angle(Vector3.ProjectOnPlane(body.rotation * Vector3.forward, up), velocity));
            }
            Debug.Log($"CORNERING_PHYSICS {trial.name} ON={enabled} speed={trial.finalSpeed:F2} turn={trial.travelTurn:F2} brake={trial.maxBrake:F2} slip={trial.maxSlip:F2} tilt={trial.maxTilt:F2} normal={trial.maxNormalSpeed:F3}");
            return trial;
        }
        finally { UnityEngine.Object.DestroyImmediate(car); UnityEngine.Object.DestroyImmediate(floor); }
    }

    private static void ValidateIneligibleInput()
    {
        features.SetFeature(RaceFeature.HighSpeedCornering, true);
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.transform.position = Origin - Vector3.up * .5f; floor.transform.localScale = new Vector3(100, 1, 100);
        GameObject car = CreateCar(Origin + Vector3.up * .36f, Quaternion.identity, out DebugMover mover, out Rigidbody body);
        var input = new Input { CurrentState = new DriveInputState { pedal = 1, steering = 30 } };
        var gate = new PlayerDriveInputGate(input); mover.SetInputSource(gate);
        try
        {
            body.linearVelocity = Vector3.forward * 60; Physics.SyncTransforms();
            for (int i = 0; i < 20; i++) Call(mover, "ReadInput");
            Call(mover, "UpdateCorneringBrake");
            Require(mover.CorneringBrakingAcceleration > 4f, "Start with active turn braking on real ground.");
            features.SetFeature(RaceFeature.HighSpeedCornering, false); Call(mover, "UpdateCorneringBrake");
            Require(mover.CorneringBrakingAcceleration == 0, "Live OFF must remove braking immediately.");
            features.SetFeature(RaceFeature.HighSpeedCornering, true); Call(mover, "UpdateCorneringBrake");
            Require(mover.CorneringBrakingAcceleration > 4f, "Live ON must restore braking without a respawn.");
            car.transform.position += Vector3.up * 10; body.position = car.transform.position; Physics.SyncTransforms();
            Call(mover, "ReadInput"); Call(mover, "UpdateCorneringBrake");
            Require(mover.CorneringBrakingAcceleration == 0, "Flight must not acquire turn braking.");
            car.transform.position -= Vector3.up * 10; body.position = car.transform.position; Physics.SyncTransforms();
            gate.SetBlocked(true); Call(mover, "ReadInput"); Call(mover, "UpdateCorneringBrake");
            Require(mover.CorneringBrakingAcceleration == 0, "Paused input must not retain braking.");
            gate.SetBlocked(false); input.IsConnected = false; Call(mover, "ReadInput");
            Require(mover.CorneringBrakingAcceleration == 0, "Disconnected input must clear braking.");
        }
        finally { UnityEngine.Object.DestroyImmediate(car); UnityEngine.Object.DestroyImmediate(floor); }
    }

    private static GameObject CreateCar(Vector3 position, Quaternion rotation, out DebugMover mover, out Rigidbody body)
    {
        var car = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/car/car2.prefab"), position, rotation);
        body = car.GetComponent<Rigidbody>(); mover = car.GetComponent<DebugMover>();
        foreach (var tire in car.GetComponentsInChildren<TireForce>())
        { Call(tire.GetComponent<GroundCheck>(), "Awake"); Call(tire, "Awake"); }
        Call(mover, "Awake"); Call(car.GetComponent<CarStabilityController>(), "Awake");
        return car;
    }

    public static void RunTube()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        RaceCourse course = UnityEngine.Object.FindFirstObjectByType<RaceCourse>();
        course.RebuildCache(); course.RebuildRoad(); Physics.SyncTransforms();
        float start = 0f, strongest = 0f;
        for (float p = 0; p < course.TotalLength; p += 2f)
        {
            course.TryGetSampleAtProgress(p, out var here); course.TryGetSampleAtProgress(p + 30f, out var ahead);
            if (!course.IsPointInsideTube(here.position + here.up) || !course.IsPointInsideTube(ahead.position + ahead.up)) continue;
            float bend = Vector3.Angle(here.forward, ahead.forward);
            if (bend > strongest) { start = p; strongest = bend; }
        }
        Require(strongest > 5f, "The authored tube must provide an actual curve.");
        var previous = Gmanager.Control; var mode = Physics.simulationMode;
        var root = new GameObject("Tube cornering QA manager"); root.SetActive(false);
        string folder = Environment.GetEnvironmentVariable("RACING_CORNERING_OUTPUT") ?? "/private/tmp/racing-cornering";
        Directory.CreateDirectory(folder); Results.trials.Clear();
        try
        {
            Physics.simulationMode = SimulationMode.Script;
            var manager = root.AddComponent<Gmanager>(); Gmanager.Control = manager; manager.state = Gmanager.State.Game;
            manager.course = course;
            features = root.AddComponent<RaceFeatureController>(); features.Configure(manager); Set(manager, "<Features>k__BackingField", features);
            using (var trace = new StreamWriter(Path.Combine(folder, "tube-trace.csv")))
            {
                trace.WriteLine("enabled,initialSpeed,direction,time,x,y,z,speed,handle,appliedAngle,brake,lateralError,insideTube,grounded");
                foreach (float speed in new[] { 35f, 45f })
                foreach (float sign in new[] { 1f, -1f })
                {
                    float entry = sign > 0 ? start : start + 30f;
                    Trial off = DriveTube(course, entry, speed, sign, false, trace);
                    Trial on = DriveTube(course, entry, speed, sign, true, trace);
                    Require(on.distance > 25f && on.maxBrake > 1f && on.outsideSeconds == 0f,
                        on.name + ": the observing driver must negotiate the tube curve without leaving the driving area.");
                    Require(on.maxLateralError < off.maxLateralError,
                        on.name + ": improve line holding during the boost compared with OFF.");
                }
            }
            Debug.Log($"AUTHORED_TUBE_CORNERING_PASS: bend={strongest:F1}deg/30m progress={start:F0}, forward/reverse, 35/45m/s, full boost, observation-driven pedal/steering only.");
        }
        finally
        {
            File.WriteAllText(Path.Combine(folder, "tube.json"), JsonUtility.ToJson(Results, true));
            UnityEngine.Object.DestroyImmediate(root); Gmanager.Control = previous; Physics.simulationMode = mode;
        }
    }

    private static Trial DriveTube(RaceCourse course, float start, float speed, float sign, bool enabled, StreamWriter trace)
    {
        features.SetFeature(RaceFeature.HighSpeedCornering, enabled);
        course.TryGetSampleAtProgress(start, out var entry);
        GameObject car = CreateCar(entry.position + entry.up * .36f, Quaternion.LookRotation(entry.forward * sign, entry.up),
            out DebugMover mover, out Rigidbody body);
        var input = new Input(); mover.SetInputSource(input); body.linearVelocity = entry.forward * (sign * speed);
        Call(mover, "StartDriftBoost", 30f);
        Physics.SyncTransforms();
        var trial = new Trial { name = $"tube speed={speed} direction={sign}", enabled = enabled, initialSpeed = speed };
        Results.trials.Add(trial);
        try
        {
            for (int step = 0; step < 100; step++)
            {
                float raw = course.GetProgressDistance(body.position);
                course.TryGetSampleAtProgress(raw, out var here);
                course.TryGetSampleAtProgress(raw + sign * Mathf.Clamp(6f + body.linearVelocity.magnitude * .3f, 10f, 20f), out var target);
                Vector3 local = Quaternion.Inverse(body.rotation) * (target.position - body.position);
                float handle = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg * 1.5f - Vector3.Dot(body.angularVelocity, here.up) * 8f, -30f, 30f);
                // Observe the actual pose/velocity each step. No pose, velocity, lap or result writes in this loop.
                input.CurrentState = new DriveInputState { pedal = 1f, steering = handle };
                Call(mover, "ApplyDriving"); Call(car.GetComponent<CarStabilityController>(), "FixedUpdate");
                Physics.Simulate(.02f); Physics.SyncTransforms();
                float error = Mathf.Abs(Vector3.Dot(body.position - here.position, here.right));
                trial.finalSpeed = body.linearVelocity.magnitude;
                trial.distance = (course.GetProgressDistance(body.position) - start) * sign;
                trial.maxLateralError = Mathf.Max(trial.maxLateralError, error);
                trial.maxBrake = Mathf.Max(trial.maxBrake, mover.CorneringBrakingAcceleration);
                if (!course.IsPointInsideDrivingArea(body.position)) trial.outsideSeconds += .02f;
                int contacts = 0; foreach (var ground in car.GetComponentsInChildren<GroundCheck>()) if (ground.CheckNow()) contacts++;
                trace.WriteLine(FormattableString.Invariant($"{enabled},{speed},{sign},{(step + 1) * .02f:F2},{body.position.x:F3},{body.position.y:F3},{body.position.z:F3},{trial.finalSpeed:F3},{handle:F3},{mover.AppliedSteeringAngle:F3},{mover.CorneringBrakingAcceleration:F3},{error:F3},{course.IsPointInsideTube(body.position)},{contacts}"));
            }
            Debug.Log($"TUBE_CORNERING {trial.name} ON={enabled} distance={trial.distance:F2} speed={trial.finalSpeed:F2} lateral={trial.maxLateralError:F2} brake={trial.maxBrake:F2} outside={trial.outsideSeconds:F2}");
            return trial;
        }
        finally { UnityEngine.Object.DestroyImmediate(car); }
    }
    private static void Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Private).Invoke(target, args);
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    public static void RunGameplay()
    {
        RaceFeatureValidation.Run();
        foreach (bool bold in new[] { false, true })
            Require(RacingUIFontCatalog.GetHUD(bold).HasCharacters("高速時は深く切ると減速暗い範囲は切れ角上限"), "The turn-braking caption needs baked Japanese glyphs.");
        CarStabilityValidation.RunBatch();
    }

    public static void RunStabilityAndSeams()
    {
        RoadSeamValidation.Run();
        CarStabilityValidation.RunBatch();
    }
}
