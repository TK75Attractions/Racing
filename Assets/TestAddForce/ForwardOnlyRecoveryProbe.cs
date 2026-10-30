#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

/// <summary>Only fixture setup moves a car. Trials use non-negative pedal and no reset button.</summary>
public sealed class ForwardOnlyRecoveryProbe : MonoBehaviour
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private sealed class Input : IDriveInputSource
    {
        public int PlayerIndex => 0;
        public string DeviceId => "forward-only-recovery";
        public bool IsConnected { get; set; } = true;
        public DriveInputState CurrentState { get; set; }
        public void UpdateInput(float dt) { }
        public void Dispose() { }
    }
    [Serializable] private sealed class Trial
    {
        public string name;
        public bool passed;
        public int automaticRecoveries, initialLap, finalLap, minGrounded;
        public float secondsToRecovery = -1, forwardTravel;
    }
    [Serializable] private sealed class Report { public bool passed; public List<Trial> trials = new List<Trial>(); }
    private readonly Report report = new Report();
    private readonly Input input = new Input();
    private Gmanager race;
    private Rigidbody body;
    private DebugMover mover;
    private CarResetter resetter;
    private PlayerDriveInputGate gate;
    private LapManager laps;
    private RaceCourse.CourseSample anchor;
    private string folder;
    private IEnumerator Start()
    {
        folder = Environment.GetEnvironmentVariable("RACING_RECOVERY_OUTPUT") ?? "Logs/ForwardOnly/Recovery";
        Directory.CreateDirectory(folder); Application.runInBackground = true; Time.timeScale = 4f;
        race = Gmanager.Control;
        foreach (var publisher in FindObjectsByType<RaceRemoteFirebasePublisher>(FindObjectsSortMode.None)) publisher.enabled = false;
        foreach (var bridge in FindObjectsByType<RaceDashboardBridge>(FindObjectsSortMode.None)) bridge.enabled = false;
        foreach (string method in new[] { "StartGameWhenScreenCovered", "CompleteGameStart", "StartRaceAfterCountdown" })
            typeof(Gmanager).GetMethod(method, Private).Invoke(race, null);
        race.GetComponent<CourseIntroDirector>()?.Stop();
        typeof(Gmanager).GetMethod("SwitchCameraForState", Private).Invoke(race, new object[] { Gmanager.State.Game });
        foreach (var transition in FindObjectsByType<ScreenTransitionController>(FindObjectsSortMode.None)) transition.ApplyStateImmediate(Gmanager.State.Game);
        race.enabled = false;
        foreach (var entry in race.Participants) if (entry.Index != 0) entry.Car.SetActive(false);
        var own = race.GetParticipant(0); body = own.Body; mover = own.Car.GetComponent<DebugMover>();
        resetter = own.Car.GetComponent<CarResetter>(); laps = FindFirstObjectByType<LapManager>(); laps.enabled = false;
        gate = new PlayerDriveInputGate(input); mover.SetInputSource(gate); resetter.SetInputSource(gate);
        bool found = false;
        for (float raw = 100; raw < race.course.TotalLength; raw += 5)
        {
            race.course.TryGetSampleAtProgress(raw, out anchor);
            if (anchor.width >= 16 && Vector3.Dot(anchor.up, Vector3.up) > .999f &&
                Mathf.Abs(anchor.forward.y) < .001f && race.course.HasRoadAtProgress(raw) &&
                !race.course.IsPointInsideTube(anchor.position) && !race.course.IsJumpAhead(anchor.position, race.CourseProgressSign, 100))
            { found = true; break; }
        }
        if (!found) throw new InvalidOperationException("No flat fixture road");
        if (Environment.GetEnvironmentVariable("RACING_RECOVERY_LANDING_ONLY") != "1")
        {
        foreach (string kind in new[] { "static-high-center", "dynamic-high-center", "neutral-high-center", "disconnected-high-center", "static-low-pedal-high-center" })
            yield return HighCenter(kind);
        yield return StaticOccupancy();
        yield return NormalRoad();
        yield return DeliberateStop();
        yield return LightPedalRoad();
        }
        yield return MissedLanding();
        yield return AirborneJump();
        report.passed = report.trials.TrueForAll(x => x.passed);
        File.WriteAllText(Path.Combine(folder, "report.json"), JsonUtility.ToJson(report, true));
        bool baseline = Environment.GetEnvironmentVariable("RACING_RECOVERY_BASELINE") == "1";
        Type.GetType("ForwardOnlyRecoveryValidation, Assembly-CSharp-Editor").GetMethod("Finish").Invoke(null,
            new object[] { baseline || report.passed, "FORWARD_RECOVERY_" + (report.passed ? "PASS" : "FAIL") + ": " + folder });
    }
    private IEnumerator Prepare()
    {
        resetter.enabled = false; input.IsConnected = true; input.CurrentState = DriveInputState.Neutral; gate.Reset();
        body.GetComponent<CarItemEffects>()?.ClearAll();
        body.position = anchor.position + anchor.up * 1.4f;
        body.rotation = Quaternion.LookRotation(anchor.forward * race.CourseProgressSign, anchor.up);
        body.linearVelocity = body.angularVelocity = Vector3.zero; Physics.SyncTransforms();
        resetter.TryResetToRoadSample(anchor); gate.Reset();
        yield return new WaitForSeconds(.6f);
        resetter.SetCourse(race.course, laps); resetter.enabled = true;
    }
    private IEnumerator HighCenter(string name)
    {
        yield return Prepare();
        var obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube); obstacle.name = "QA chassis support " + name;
        Vector3 forward = anchor.forward * race.CourseProgressSign;
        obstacle.transform.SetPositionAndRotation(anchor.position + forward * 8f + anchor.up * 1.2f, body.rotation);
        obstacle.transform.localScale = new Vector3(.35f, 2.4f, 3.5f);
        if (name.StartsWith("dynamic"))
        { var support = obstacle.AddComponent<Rigidbody>(); support.constraints = RigidbodyConstraints.FreezeAll; support.useGravity = false; }
        body.position = anchor.position + forward * 8f + anchor.up * 2.55f;
        body.linearVelocity = body.angularVelocity = Vector3.zero; Physics.SyncTransforms();
        yield return new WaitForSeconds(.7f);
        int before = resetter.AutomaticRecoveryCount;
        var trial = new Trial { name = name, initialLap = laps.GetCarData(body).lapCount, minGrounded = 4 };
        report.trials.Add(trial);
        input.CurrentState = new DriveInputState { pedal = name.StartsWith("neutral") ? 0f : name.Contains("low-pedal") ? .1f : 1f };
        input.IsConnected = !name.StartsWith("disconnected");
        float began = Time.time; bool captured = false;
        while (Time.time - began < 7f && resetter.AutomaticRecoveryCount == before)
        {
            int grounded = 0; foreach (var tire in body.GetComponentsInChildren<TireForce>()) if (tire.IsGrounded) grounded++;
            trial.minGrounded = Mathf.Min(trial.minGrounded, grounded);
            if (!captured && Time.time - began > .4f) { Capture(name + "-stuck"); captured = true; }
            yield return new WaitForFixedUpdate();
        }
        trial.automaticRecoveries = resetter.AutomaticRecoveryCount - before;
        trial.finalLap = laps.GetCarData(body).lapCount;
        if (trial.automaticRecoveries > 0) trial.secondsToRecovery = Time.time - began;
        bool shouldRecover = name.StartsWith("static") || name.StartsWith("dynamic");
        trial.passed = trial.minGrounded == 0 && trial.initialLap == trial.finalLap &&
            (shouldRecover ? trial.automaticRecoveries == 1 && trial.secondsToRecovery < 5f : trial.automaticRecoveries == 0);
        Capture(name + "-end");
        input.CurrentState = DriveInputState.Neutral; Destroy(obstacle); yield return null;
    }
    private IEnumerator StaticOccupancy()
    {
        yield return Prepare(); resetter.enabled = false;
        var obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube); obstacle.name = "QA blocked recovery lanes";
        obstacle.transform.SetPositionAndRotation(anchor.position + anchor.up, body.rotation);
        obstacle.transform.localScale = new Vector3(30, 2, 12); Physics.SyncTransforms();
        Vector3 before = body.position;
        bool accepted = resetter.TryResetToRoadSample(anchor);
        var trial = new Trial { name = "static-occupied-reset", passed = !accepted && resetter.IsRecoveryPending && body.position == before };
        report.trials.Add(trial); Destroy(obstacle); yield return null; Physics.SyncTransforms();
        bool restored = resetter.TryResetToRoadSample(anchor);
        trial.passed &= restored && !resetter.IsRecoveryPending;
    }
    private IEnumerator NormalRoad()
    {
        yield return Prepare(); int before = resetter.AutomaticRecoveryCount; Vector3 start = body.position;
        input.CurrentState = new DriveInputState { pedal = .8f };
        float began = Time.time;
        while (Time.time - began < 4f) yield return new WaitForFixedUpdate();
        float travel = Vector3.Dot(body.position - start, anchor.forward * race.CourseProgressSign);
        report.trials.Add(new Trial { name = "normal-forward-driving", passed = travel > 10 && resetter.AutomaticRecoveryCount == before,
            automaticRecoveries = resetter.AutomaticRecoveryCount - before, forwardTravel = travel });
    }
    private IEnumerator DeliberateStop()
    {
        yield return Prepare();int before=resetter.AutomaticRecoveryCount;Vector3 start=body.position;
        input.CurrentState=DriveInputState.Neutral;yield return new WaitForSeconds(10f);
        float travel=Vector3.Distance(body.position,start);
        report.trials.Add(new Trial{name="deliberate-neutral-stop",passed=resetter.AutomaticRecoveryCount==before&&travel<.5f,
            automaticRecoveries=resetter.AutomaticRecoveryCount-before,forwardTravel=travel});
    }
    private IEnumerator LightPedalRoad()
    {
        yield return Prepare();int before=resetter.AutomaticRecoveryCount;Vector3 start=body.position;
        input.CurrentState=new DriveInputState{pedal=.1f};yield return new WaitForSeconds(4f);
        float travel=Vector3.Dot(body.position-start,anchor.forward*race.CourseProgressSign);
        report.trials.Add(new Trial{name="normal-light-pedal",passed=resetter.AutomaticRecoveryCount==before&&travel>2f,
            automaticRecoveries=resetter.AutomaticRecoveryCount-before,forwardTravel=travel});
    }
    private IEnumerator MissedLanding()
    {
        yield return Prepare();
        // Recorded at t=76.14 in forward-wall-stress/seed8106: below the landing
        // ramp, shortly before virtual gate 2. The car used to drive out after 1.2s,
        // missing that gate permanently and losing a complete lap.
        Vector3 failedPosition = new Vector3(617.183f, .348f, 597.464f);
        race.course.TryGetNearestRoadSample(failedPosition, out var road);
        if (Vector3.Dot(failedPosition-road.position,road.up) > -2.5f ||
            !race.course.IsJumpAhead(failedPosition,-race.CourseProgressSign,60f))
            throw new InvalidOperationException("Recorded missed-landing fixture no longer matches the course.");
        body.position = failedPosition; body.rotation = Quaternion.LookRotation(road.forward * race.CourseProgressSign, road.up);
        body.linearVelocity = body.angularVelocity = Vector3.zero; Physics.SyncTransforms();
        int before = resetter.AutomaticRecoveryCount, lap = laps.GetCarData(body).lapCount;
        input.CurrentState = new DriveInputState { pedal = 1f };
        float began = Time.time;
        while (Time.time-began < .8f && resetter.AutomaticRecoveryCount == before) yield return new WaitForFixedUpdate();
        report.trials.Add(new Trial { name = "recorded-missed-landing", initialLap = lap, finalLap = laps.GetCarData(body).lapCount,
            automaticRecoveries = resetter.AutomaticRecoveryCount-before, secondsToRecovery = Time.time-began,
            passed = resetter.AutomaticRecoveryCount == before+1 && Time.time-began < .6f && laps.GetCarData(body).lapCount == lap });
    }
    private IEnumerator AirborneJump()
    {
        yield return Prepare(); int before = resetter.AutomaticRecoveryCount;
        body.position += anchor.up * 15f; body.linearVelocity = anchor.forward * race.CourseProgressSign * 22f;
        Physics.SyncTransforms(); input.CurrentState = new DriveInputState { pedal = 1f };
        yield return new WaitForSeconds(.8f);
        report.trials.Add(new Trial { name = "normal-airborne-jump", automaticRecoveries = resetter.AutomaticRecoveryCount-before,
            passed = resetter.AutomaticRecoveryCount == before });
    }
    private void Capture(string name)
    {
        byte[] bytes = race.CapturePlayerFrame(0); if (bytes != null) File.WriteAllBytes(Path.Combine(folder, name + ".jpg"), bytes);
    }
}
#endif
