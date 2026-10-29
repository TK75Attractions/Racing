using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[DefaultExecutionOrder(-200)]
public sealed class FinishRunValidationProbe : MonoBehaviour, IDriveInputSource
{
    [Serializable] private sealed class FinishSample
    {
        public int trial, index, rank;
        public float time, speedAtFinish, distanceAfterFinish, duration;
        public bool stopped, droveInGoal;
        [NonSerialized] public Vector3 position;
        [NonSerialized] public float began;
    }
    [Serializable] private sealed class Report { public List<FinishSample> finishes = new List<FinishSample>(); }
    public Action<bool, string> Complete;
    private void Awake() { Time.timeScale = 3f; }
    private void OnDestroy() { Time.timeScale = 1f; }
    private readonly Report report = new Report();
    private readonly RoadObservationBot bot = new RoadObservationBot();
    private Gmanager manager;
    private LapManager laps;
    private RaceParticipant player;
    private GroundCheck[] wheels;
    private int trial;
    private bool launching, bound, switchedOff;
    public int PlayerIndex => 0;
    public string DeviceId => "Finish run observation bot";
    public bool IsConnected => true;
    public DriveInputState CurrentState { get; private set; }

    private void Update()
    {
        try
        {
            manager = Gmanager.Control;
            if (manager == null) return;
            if (!launching && (manager.state == Gmanager.State.Title || manager.state == Gmanager.State.Result) && manager.CanUseDashboardControls)
            {
                launching = true; bound = false; switchedOff = false; bot.Clear();
                manager.Features.SelectPreset(RacePreset.Standard);
                manager.Features.SetValidationFeature(RaceFeature.FinishRun, trial != 2);
                foreach (RaceItemPickup pickup in UnityEngine.Object.FindObjectsByType<RaceItemPickup>(FindObjectsSortMode.None)) pickup.gameObject.SetActive(false);
                manager.StartAutodriveTestRace();
            }
            if (!bound && manager.state == Gmanager.State.Game)
            {
                bound = true; player = manager.GetParticipant(0); wheels = player.Car.GetComponentsInChildren<GroundCheck>();
                player.Car.GetComponent<DebugMover>().SetInputSource(this);
                laps = UnityEngine.Object.FindFirstObjectByType<LapManager>(); laps.CarFinished += OnFinished;
                Debug.Log("FINISH_RUN_TRIAL_START: " + trial);
            }
            if (!bound) return;
            foreach (FinishSample sample in report.finishes)
            {
                if (sample.trial != trial || sample.stopped) continue;
                RaceParticipant entry = manager.GetParticipant(sample.index);
                var run = entry.Car.GetComponent<FinishRunController>();
                sample.distanceAfterFinish = Mathf.Max(sample.distanceAfterFinish, Vector3.Distance(sample.position, entry.Body.position));
                Require(entry.Result.totalRaceTime == sample.time && entry.Result.finishPosition == sample.rank,
                    "The finish run must not change official time or rank.");
                if (run?.IsActive == true)
                {
                    sample.droveInGoal |= manager.state == Gmanager.State.Goal && entry.Body.linearVelocity.magnitude > 1f;
                    Require(!entry.Body.isKinematic && manager.CanDriveVehicle(entry.Body) && manager.IsEngineAudioActive(entry.Body), "Finishing must retain physical driving and engine audio.");
                    if (trial == 1 && !switchedOff && Time.time - sample.began > .5f)
                    {
                        switchedOff = true; manager.Features.SetValidationFeature(RaceFeature.FinishRun, false);
                        Require(!run.IsActive && entry.Body.isKinematic, "Live OFF must stop the winning run immediately.");
                        Debug.Log("FINISH_RUN_LIVE_OFF_PASS");
                    }
                }
                else
                {
                    sample.stopped = true; sample.duration = run.ElapsedSeconds;
                    Require(entry.Body.isKinematic && entry.Body.linearVelocity.sqrMagnitude < .001f, "The short run must stop after its time limit.");
                    if (trial == 0) Require(sample.distanceAfterFinish > 10f && sample.duration >= 3.4f && sample.duration < 3.6f,
                        "A finisher must travel briefly, then stop within the celebration.");
                    Debug.Log($"FINISH_RUN_STOP_PASS: trial={trial} index={sample.index} travel={sample.distanceAfterFinish:F2}m duration={sample.duration:F2}s");
                }
            }
            if (manager.state != Gmanager.State.Result || !manager.CanUseDashboardControls) return;
            Require(report.finishes.Exists(s => s.trial == trial && s.index == 0) &&
                report.finishes.Exists(s => s.trial == trial && s.index == 1) &&
                report.finishes.Exists(s => s.trial == trial && s.index >= 2),
                "Both humans and at least one CPU must actually finish the race.");
            foreach (RaceParticipant entry in manager.Participants)
                if (entry.Result.didFinish == false)
                {
                    Require(entry.Car.GetComponent<FinishRunController>() == null && entry.Body.isKinematic,
                        "DNF must stop without a victory run.");
                    Debug.Log($"FINISH_RUN_DNF_PASS: trial={trial} index={entry.Index}");
                }
            if (trial == 0)
                Require(report.finishes.Exists(s => s.trial == 0 && s.droveInGoal), "The last human must keep driving during the Goal state.");
            laps.CarFinished -= OnFinished;
            trial++; launching = false; bound = false;
            if (trial == 3)
            {
                string output = Environment.GetEnvironmentVariable("RACING_FINISH_RUN_OUTPUT") ?? "/private/tmp/racing-finish-run";
                Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "report.json"), JsonUtility.ToJson(report, true));
                Complete?.Invoke(true, "FINISH_RUN_VALIDATION_PASS: three physical one-lap races, human/CPU finishes, preserved finish speed, post-finish travel, final Goal driving, official time/rank unchanged, live OFF and pre-race OFF.");
            }
        }
        catch (Exception error) { Debug.LogException(error); Complete?.Invoke(false, error.Message); }
    }
    private void OnFinished(Rigidbody body, RaceResultRecord result)
    {
        RaceParticipant entry = manager.FindParticipant(body);
        var run = body.GetComponent<FinishRunController>();
        if (trial == 0 || (trial == 1 && !switchedOff))
        {
            Require(run?.IsActive == true && !body.isKinematic && body.linearVelocity.magnitude > 1f, "Goal crossing must not zero speed.");
            foreach (RaceParticipant other in manager.Participants)
            {
                if (other == entry) continue;
                foreach (Collider a in body.GetComponentsInChildren<Collider>())
                foreach (Collider b in other.Car.GetComponentsInChildren<Collider>())
                    Require(Physics.GetIgnoreCollision(a, b), "A finished car must not obstruct other racers.");
            }
        }
        else Require(run == null && body.isKinematic, "OFF before finishing must retain the former immediate stop.");
        report.finishes.Add(new FinishSample { trial = trial, index = entry.Index, rank = entry.Result.finishPosition,
            time = entry.Result.totalRaceTime, speedAtFinish = body.linearVelocity.magnitude, position = body.position, began = Time.time,
            stopped = run == null });
        Debug.Log($"FINISH_RUN_CROSSING: trial={trial} index={entry.Index} speed={body.linearVelocity.magnitude:F2}m/s time={entry.Result.totalRaceTime:F2}");
    }
    private void FixedUpdate()
    {
        if (!bound || player == null || !player.IsRacing) { CurrentState = DriveInputState.Neutral; return; }
        int contacts = 0; foreach (GroundCheck wheel in wheels) if (wheel.CheckNow()) contacts++;
        CurrentState = bot.Decide(manager.course, player.Body, manager.CourseProgressSign, contacts, Time.time,
            cruise: 35f, lane: 0f, noise: 0f, vehicle: player.Car.GetComponent<DebugMover>());
    }
    public void UpdateInput(float dt) { }
    public void Dispose() { }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
