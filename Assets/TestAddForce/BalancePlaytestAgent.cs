#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Test-only human substitutes. Reads course geometry and supplies pedal, steering and optional
/// reset input; no writes to vehicle pose/velocity. Production CPUs and item physics are unchanged.
/// </summary>
[DefaultExecutionOrder(-200)]
public sealed class BalancePlaytestAgent : MonoBehaviour
{
    private sealed class Input : IDriveInputSource
    {
        public int PlayerIndex { get; set; }
        public string DeviceId => "balance-playtest-agent";
        public bool IsConnected { get; set; } = true;
        private DriveInputState state;
        public DriveInputState CurrentState { get => IsConnected ? state : DriveInputState.Neutral; set => state = value; }
        public void UpdateInput(float dt) { }
        public void Dispose() { }
    }
    [Serializable] public sealed class CarRecord
    {
        public string label;
        public bool finished;
        public int position, laps, vehicleContacts, environmentContacts, itemsUsed, afflictions, charges, manualResets, cpuRecoveries;
        public float finishTime, progress, distanceDriven, meanSpeed, maxSpeed, maxStall, offCourseSeconds, maxTilt, nearbyHumanSeconds, racingSeconds;
        public int takeoffs, landings, humanRecoveries, automaticRecoveries;
        public string recoveryBlockedReason;
        [NonSerialized] public TireForce[] tires;
        [NonSerialized] public DebugMover mover;
        [NonSerialized] public Vector3 previousPosition;
        [NonSerialized] public float stall, offCourseStreak, resetCooldown, speedSum;
        [NonSerialized] public int samples;
        [NonSerialized] public bool wasAirborne, wasOffCourse;
    }
    [Serializable] public sealed class RunRecord
    {
        public string name;
        public string p1Profile, p2Profile;
        public int seed, goalLaps, npcCount;
        public bool itemsEnabled, resetAssistance, completed, raceEnded;
        public float p1TargetSpeed, p2TargetSpeed, seconds, physicsSeconds, startGlobalTime, firstHumanFinish, humanFinishGap, humanSeparationSeconds, cpuNearRearHumanSeconds, grantedHumanGrace;
        public string termination;
        public List<CarRecord> cars = new List<CarRecord>();
    }
    [Serializable] private sealed class Suite
    {
        public string startedUtc, unityVersion, revision;
        public float courseLength, fixedDeltaTime, timeScale, observationWindowSeconds;
        public string methodology = "Actual SampleScene, scene lap count recorded per run, production 360-second limit and 40-to-180-second human grace. Human substitutes use course geometry and pedal/steering input. Items disabled only in named control runs. No forced results, checkpoints, vehicle pose or velocity. Reset-assisted run uses the normal reset input after 4 seconds stuck/off-course. Seed fixes Unity random sequence, not physics determinism; CPU contest phase depends on global Time.time, so equal seeds do not isolate reset assistance. CSV time uses accumulated fixed physics steps; seconds/finishTime use official game clocks. Environment contacts include road landings, not just walls. No judgment of human usability, real input latency or audio.";
        public bool completed;
        public string error;
        public List<RunRecord> runs = new List<RunRecord>();
        public List<string> runtimeErrors = new List<string>();
    }
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly Input[] inputs = { new Input { PlayerIndex = 0 }, new Input { PlayerIndex = 1 } };
    private readonly List<RaceItemPickup> pickups = new List<RaceItemPickup>();
    private readonly Suite suite = new Suite();
    private Gmanager manager;
    private LapManager laps;
    private RunRecord run;
    private bool driving;
    private float nextTrace, previousScale, previousMaximumDelta;
    private int previousVsync, previousFrameRate;
    private bool previousRunInBackground;
    private string output;
    private StreamWriter trace, events;
    private bool skillSuite;
    private readonly float[] nextDecisionAt = new float[2];
    private readonly DriveInputState[] heldDecision = new DriveInputState[2];
    private readonly float[] releaseRequestedAt = {-1,-1};
    private readonly float[] releaseUntil = {-1,-1};
    private readonly float[] jumpNoticedAt = {-1,-1};
    private OnPlayUIManager[] driverUis;

    private IEnumerator Start()
    {
        output = Environment.GetEnvironmentVariable("RACING_BALANCE_OUTPUT") ?? Path.GetFullPath("Artifacts/BalancePlaytest");
        Directory.CreateDirectory(output);
        suite.startedUtc = DateTime.UtcNow.ToString("o"); suite.unityVersion = Application.unityVersion;
        suite.revision = Environment.GetEnvironmentVariable("RACING_BALANCE_REVISION") ?? "working-tree";
        previousScale = Time.timeScale; previousMaximumDelta = Time.maximumDeltaTime;
        previousVsync = QualitySettings.vSyncCount; previousFrameRate = Application.targetFrameRate;
        float scale = 8;
        if (float.TryParse(Environment.GetEnvironmentVariable("RACING_BALANCE_TIME_SCALE"),
            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float configuredScale))
            scale = Mathf.Clamp(configuredScale, 1, 8);
        Time.timeScale = scale; Time.maximumDeltaTime = .5f; QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
        previousRunInBackground = Application.runInBackground; Application.runInBackground = true;
        Application.logMessageReceived += LogError;
        manager = Gmanager.Control; laps = FindFirstObjectByType<LapManager>();
        driverUis=(OnPlayUIManager[])typeof(Gmanager).GetField("onPlayUIManagers",Private).GetValue(manager);
        pickups.AddRange(FindObjectsByType<RaceItemPickup>(FindObjectsSortMode.None));
        suite.courseLength = manager.course.TotalLength; suite.fixedDeltaTime = Time.fixedDeltaTime; suite.timeScale = Time.timeScale;
        if (float.TryParse(Environment.GetEnvironmentVariable("RACING_BALANCE_WINDOW_SECONDS"),
            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float window))
            suite.observationWindowSeconds = Mathf.Clamp(window, 0, 360);
        foreach (var publisher in FindObjectsByType<RaceRemoteFirebasePublisher>(FindObjectsSortMode.None)) publisher.enabled = false;
        var plans = new[] {
            Plan("equal-control-101", 101, 25, 25, false), Plan("equal-control-102", 102, 25, 25, false),
            Plan("split-fast-p1", 201, 27, 13, false), Plan("split-fast-p2", 202, 13, 27, false),
            Plan("both-slow", 301, 12, 12, false),
            Plan("items-401", 401, 25, 25, true), Plan("items-402", 402, 25, 25, true),
            Plan("items-reset-assisted", 401, 25, 25, true, true), Plan("temporary-stop-p2", 501, 25, 25, false)
        };
        skillSuite = Environment.GetEnvironmentVariable("RACING_BALANCE_SKILL_SUITE") == "1";
        if (skillSuite)
        {
            plans = new[] {
                SkillPlan("skill-novice-expert", 6101, 12, 27, false, "novice", "expert"),
                SkillPlan("skill-club-expert", 6102, 20, 28, true, "intermediate", "expert"),
                SkillPlan("skill-aggressive-items", 6103, 32, 22, true, "aggressive", "intermediate"),
                SkillPlan("skill-stop-rejoin", 6104, 24, 16, false, "intermediate", "cautious"),
                SkillPlan("skill-disconnect-rejoin", 6105, 22, 22, false, "intermediate", "intermediate"),
                SkillPlan("skill-operator-stop", 6106, 18, 18, false, "cautious", "cautious")
            };
            suite.methodology = "Actual SampleScene and production cars/CPUs/items. Course-following input agents approximate novice, cautious, intermediate, expert and aggressive control using distinct speed, reaction intervals, steering noise and jump anticipation; they are not human usability subjects. Includes a 12-second stop, 5-second input disconnection and operator result action. Drivers use InputManager and production input gates; never force vehicle pose, velocity, laps or outcomes. An agent responds to the public pedal-release notice after 0.35 seconds with 0.3 seconds of neutral input. Novice/cautious agents respond to a visible jump notice after 0.35 seconds by targeting 24m/s; understanding that cue is an assumed response, not a human-study result. Preparation is bypassed. Random seeds do not guarantee deterministic physics. Official and physics clocks are recorded separately.";
            var sources = (IDriveInputSource[])typeof(InputManager).GetField("inputSources", Private).GetValue(manager.IManager);
            for (int i = 0; i < 2; i++) sources[i] = inputs[i];
        }
        string filter = Environment.GetEnvironmentVariable("RACING_BALANCE_RUN");
        foreach (RunRecord plan in plans)
        {
            if (!string.IsNullOrEmpty(filter) && Array.IndexOf(filter.Split(','), plan.name) < 0) continue;
            yield return Execute(plan);
            if (suite.error != null) break;
        }
        suite.completed = suite.error == null && suite.runs.Count > 0 && suite.runs.TrueForAll(r => r.completed);
        Save();
        Type.GetType("BalancePlaytestValidation, Assembly-CSharp-Editor").GetMethod("Finish").Invoke(null,
            new object[] { suite.completed, "BALANCE_PLAYTEST_" + (suite.completed ? "RECORDED: " : "INCOMPLETE: ") + output });
    }
    private static RunRecord Plan(string name, int seed, float p1, float p2, bool items, bool resets = false) =>
        new RunRecord { name = name, seed = seed, p1TargetSpeed = p1, p2TargetSpeed = p2, itemsEnabled = items, resetAssistance = resets, firstHumanFinish = -1, humanFinishGap = -1 };
    private static RunRecord SkillPlan(string name, int seed, float p1, float p2, bool items, string first, string second)
    {
        RunRecord plan = Plan(name, seed, p1, p2, items);
        plan.p1Profile = first; plan.p2Profile = second; return plan;
    }
    private void Invoke(string method) => typeof(Gmanager).GetMethod(method, Private).Invoke(manager, null);

    private IEnumerator Execute(RunRecord plan)
    {
        driving = false;
        Invoke("ClearCurrentRaceObjects");
        yield return null; // Let Destroy complete before binding the next session.
        foreach (RaceItemPickup pickup in pickups) if (pickup != null) pickup.gameObject.SetActive(plan.itemsEnabled);
        UnityEngine.Random.InitState(plan.seed);
        run = plan; run.goalLaps = laps.GoalLap;
        suite.runs.Add(run);
        string folder = Path.Combine(output, run.name); Directory.CreateDirectory(folder);
        trace = new StreamWriter(Path.Combine(folder, "trace.csv"));
        trace.WriteLine("time,car,x,y,z,speed,progress,lap,checkpoint,position,pedal,steering,grounded,offCourse,rocket,confuse,oil,spin,cpuTargetSpeed,cpuRival,connected,needsRelease,recoveries,recoveryBlocked");
        events = new StreamWriter(Path.Combine(folder, "events.csv")); events.WriteLine("time,car,event,detail,x,y,z");
        inputs[0].CurrentState = inputs[1].CurrentState = DriveInputState.Neutral;
        for (int i = 0; i < 2; i++) { inputs[i].IsConnected = true; nextDecisionAt[i] = 0; heldDecision[i] = DriveInputState.Neutral; releaseRequestedAt[i]=releaseUntil[i]=jumpNoticedAt[i]=-1; }
        Invoke("StartGameWhenScreenCovered"); Invoke("CompleteGameStart");
        foreach (ScreenTransitionController transition in FindObjectsByType<ScreenTransitionController>(FindObjectsSortMode.None))
            transition.ApplyStateImmediate(Gmanager.State.Countdown);
        while (manager.state != Gmanager.State.Game) yield return null;
        run.npcCount = manager.Participants.Count - 2;
        foreach (RaceParticipant entry in manager.Participants)
        {
            var record = new CarRecord { label = entry.Label, tires = entry.Car.GetComponentsInChildren<TireForce>(),
                mover = entry.Car.GetComponent<DebugMover>(), previousPosition = entry.Body.position };
            run.cars.Add(record);
            if (!entry.IsCpu && !skillSuite)
            {
                record.mover.SetInputSource(inputs[entry.Index]);
                entry.Car.GetComponent<CarResetter>()?.SetInputSource(inputs[entry.Index]);
            }
            var contact = entry.Car.AddComponent<BalancePlaytestContact>(); contact.Owner = this; contact.Index = entry.Index;
            int index = entry.Index;
            entry.Effects.ItemUsed += item => { record.itemsUsed++; Event(index, "item", item.ToString()); };
            entry.Effects.Afflicted += item => { record.afflictions++; Event(index, "afflicted", item.ToString()); };
            entry.Effects.ChargeCollected += () => { record.charges++; Event(index, "charge", ""); };
        }
        nextTrace = 0; run.startGlobalTime = Time.time; driving = true;
        Debug.Log("BALANCE_RUN_STARTED: " + run.name);
        Save();
        bool capturedDisconnect=false, capturedRelease=false, capturedDriving=false;
        while (manager.state == Gmanager.State.Game && manager.time < 365 &&
            (suite.observationWindowSeconds <= 0 || run.physicsSeconds < suite.observationWindowSeconds))
        {
            if (run.name == "skill-operator-stop" && run.physicsSeconds >= 20f) manager.ShowResult();
            if(skillSuite && !capturedDriving && run.physicsSeconds>=30f)
            { Capture(folder,"driving-p1",0); capturedDriving=true; }
            if (run.name == "skill-disconnect-rejoin" && !capturedDisconnect && run.physicsSeconds>=46f)
            { Capture(folder,"disconnected-p2",1); capturedDisconnect=true; }
            if (run.name == "skill-disconnect-rejoin" && !capturedRelease && run.physicsSeconds>=50.1f)
            { Capture(folder,"reconnected-p2",1); capturedRelease=true; }
            yield return null;
        }
        driving = false;
        run.seconds = manager.time;
        run.raceEnded = manager.state != Gmanager.State.Game;
        run.completed = run.raceEnded || (suite.observationWindowSeconds > 0 && run.physicsSeconds >= suite.observationWindowSeconds);
        run.termination = run.completed ? (manager.WaitingForSecondPlace ? "human-grace-timeout" : "race-ended") : "agent-safety-timeout";
        int finishedHumans = 0;
        foreach (RaceParticipant entry in manager.Participants)
        {
            CarRecord record = run.cars[entry.Index];
            record.finished = entry.Result?.didFinish == true; record.position = entry.Result?.finishPosition ?? entry.Position;
            record.finishTime = record.finished ? entry.Result.totalRaceTime : -1;
            record.laps = laps.GetCarData(entry.Body).lapCount; record.progress = laps.GetRaceProgressDistance(entry.Body);
            record.cpuRecoveries = entry.Driver?.RecoveryCount ?? 0;
            record.recoveryBlockedReason=entry.Driver?.RecoveryBlockedReason ?? "";
            CarResetter resetter=entry.Car.GetComponent<CarResetter>();
            if(!entry.IsCpu) {record.humanRecoveries=resetter?.RecoveryCount??0; record.automaticRecoveries=resetter?.AutomaticRecoveryCount??0;}
            record.meanSpeed = record.samples > 0 ? record.speedSum / record.samples : 0;
            if (entry.Index < 2 && record.finished) finishedHumans++;
            Event(entry.Index, entry.Result == null ? "still-racing" : record.finished ? "finished" : "dnf", "position=" + record.position + ";laps=" + record.laps);
        }
        run.termination = !run.completed ? "agent-safety-timeout" : !run.raceEnded ? "observation-window-ended" :
            run.name=="skill-operator-stop" ? "operator-stop" : finishedHumans == 2 ? "both-humans-finished" : run.seconds >= 359 ? "360-second-limit" : "human-grace-timeout";
        if (finishedHumans == 2) run.humanFinishGap = Mathf.Abs(run.cars[0].finishTime - run.cars[1].finishTime);
        trace.Dispose(); events.Dispose(); trace = events = null;
        File.WriteAllText(Path.Combine(folder, "summary.json"), JsonUtility.ToJson(run, true)); Save();
        Debug.Log("BALANCE_RUN_RECORDED: " + run.name + " " + run.termination + " " + run.seconds);
        if (!run.completed) suite.error = "Race failed to terminate: " + run.name;
        // Let goal/result animations finish before another session; interrupted goal callbacks
        // otherwise retain transition state and can change the next session's spectator cameras.
        float waitStarted = Time.realtimeSinceStartup;
        while ((run.raceEnded && manager.state != Gmanager.State.Result) || !manager.CanUseDashboardControls)
        {
            if (Time.realtimeSinceStartup - waitStarted > 20)
            { suite.error = "Result transition failed to settle after " + run.name; break; }
            yield return null;
        }
        if(skillSuite) {Capture(folder,"result-p1",0); Capture(folder,"result-p2",1);}
        yield return null;
    }

    private void Capture(string folder,string name,int index)
    {
        byte[] bytes=manager.CapturePlayerFrame(index);
        if(bytes!=null)File.WriteAllBytes(Path.Combine(folder,name+".jpg"),bytes);
        var rigs=(PlayerDisplayRig[])typeof(Gmanager).GetField("displayRigs",Private).GetValue(manager);
        PlayerDisplayRig rig=rigs[index];
        var description=new System.Text.StringBuilder();
        description.AppendLine("State="+manager.state+"; Notice="+driverUis[index]?.DriverNotice);
        description.AppendLine("Canvas="+rig.Canvas.renderMode+"; Scale="+rig.Canvas.transform.lossyScale+"; Size="+((RectTransform)rig.Canvas.transform).rect+"; Enabled="+rig.Canvas.enabled);
        description.AppendLine("UI camera="+rig.UiCamera.enabled+"; Mask="+rig.UiCamera.cullingMask+"; Rect="+rig.UiCamera.rect+"; Position="+rig.UiCamera.transform.position);
        foreach(CanvasGroup group in rig.Canvas.GetComponentsInChildren<CanvasGroup>(true))
            description.AppendLine(group.name+" active="+group.gameObject.activeInHierarchy+" alpha="+group.alpha+" scale="+group.transform.lossyScale);
        File.WriteAllText(Path.Combine(folder,name+".txt"),description.ToString());
    }

    private void FixedUpdate()
    {
        if (!driving || manager.state != Gmanager.State.Game) return;
        float dt = Time.fixedDeltaTime;
        run.physicsSeconds += dt;
        run.grantedHumanGrace=Mathf.Max(run.grantedHumanGrace,manager.SecondPlaceTimeRemaining);
        float now = run.physicsSeconds;
        bool write = now >= nextTrace; if (write) nextTrace = now + .1f;
        foreach (RaceParticipant entry in manager.Participants)
        {
            CarRecord record = run.cars[entry.Index]; Rigidbody body = entry.Body;
            if (!entry.IsRacing)
            {
                if (entry.Index < 2 && entry.Result?.didFinish == true && run.firstHumanFinish < 0) run.firstHumanFinish = now;
                continue;
            }
            float speed = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up).magnitude;
            float raw = manager.course.GetProgressDistance(body.position);
            manager.course.TryGetSampleAtProgress(raw, out RaceCourse.CourseSample here);
            int grounded = 0; foreach (TireForce tire in record.tires) if (tire.IsGrounded) grounded++;
            bool offCourse = !manager.course.IsPointInsideCourse(body.position) || body.position.y < here.position.y - 6;
            record.offCourseStreak = offCourse ? record.offCourseStreak + dt : 0;
            if (offCourse) record.offCourseSeconds += dt;
            if (offCourse != record.wasOffCourse) { Event(entry.Index, offCourse ? "off-course" : "on-course", ""); record.wasOffCourse = offCourse; }
            bool airborne = grounded == 0;
            if (airborne != record.wasAirborne)
            {
                if (airborne) record.takeoffs++; else record.landings++;
                Event(entry.Index, airborne ? "airborne" : "grounded", "speed=" + speed.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
                record.wasAirborne = airborne;
            }
            float step = Vector3.Distance(record.previousPosition, body.position);
            if (step > 8) Event(entry.Index, "position-discontinuity", "meters=" + step.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
            else record.distanceDriven += step;
            record.previousPosition = body.position;
            record.speedSum += speed; record.samples++; record.racingSeconds += dt;
            record.maxSpeed = Mathf.Max(record.maxSpeed, speed);
            record.maxTilt = Mathf.Max(record.maxTilt, Vector3.Angle(body.rotation * Vector3.up, Vector3.up));
            record.stall = speed < .5f && now > 6 ? record.stall + dt : 0;
            record.maxStall = Mathf.Max(record.maxStall, record.stall);
            if (record.stall >= 4 && record.stall < 4 + dt * 1.5f) Event(entry.Index, "stalled-4-seconds", "");
            if (entry.IsCpu)
            {
                float nearest = float.PositiveInfinity;
                for (int h = 0; h < 2; h++) if (manager.GetParticipant(h).IsRacing)
                    nearest = Mathf.Min(nearest, Mathf.Abs(entry.Progress - manager.GetParticipant(h).Progress));
                if (nearest < 40) record.nearbyHumanSeconds += dt;
            }
            else
            {
                bool reset = run.resetAssistance && now > record.resetCooldown &&
                    (record.stall > 4 || record.offCourseStreak > 4);
                if (reset) { record.manualResets++; record.resetCooldown = now + 5; Event(entry.Index, "reset-input", ""); }
                // Latch until the render-frame CarResetter.Update consumes the input, even
                // when accelerated time executes several physics steps before one Update.
                reset |= inputs[entry.Index].CurrentState.resetPressed && !record.mover.IsInputSuppressed;
                bool connected = run.name != "skill-disconnect-rejoin" || entry.Index != 1 || now < 45f || now >= 50f;
                if (inputs[entry.Index].IsConnected != connected) Event(entry.Index, connected ? "input-reconnected" : "input-disconnected", "");
                inputs[entry.Index].IsConnected = connected;
                inputs[entry.Index].CurrentState = Drive(entry, grounded, speed, raw, reset);
                if(skillSuite && record.mover.InputSource is PlayerDriveInputGate gate && connected)
                {
                    int i=entry.Index;
                    if(gate.NeedsPedalRelease && releaseRequestedAt[i]<0) releaseRequestedAt[i]=now;
                    if(releaseRequestedAt[i]>=0 && now-releaseRequestedAt[i]>=.35f)
                    { releaseUntil[i]=now+.3f; releaseRequestedAt[i]=-1; Event(i,"release-pedal-response",""); }
                    if(now<releaseUntil[i]) inputs[i].CurrentState=DriveInputState.Neutral;
                    if(!gate.NeedsPedalRelease) releaseRequestedAt[i]=-1;
                }
            }
            if (write)
            {
                var data = laps.GetCarData(body); var effects = entry.Effects;
                var input = entry.IsCpu ? entry.Driver.CurrentState : inputs[entry.Index].CurrentState;
                var gate=record.mover.InputSource as PlayerDriveInputGate;
                trace.WriteLine(FormattableString.Invariant($"{now:F2},{entry.Label},{body.position.x:F3},{body.position.y:F3},{body.position.z:F3},{speed:F3},{laps.GetRaceProgressDistance(body):F3},{data.lapCount},{data.lastCheckpointIndex},{entry.Position},{input.pedal:F3},{input.steering:F3},{grounded},{offCourse},{effects.IsRocketActive},{effects.IsConfused},{effects.IsOiled},{effects.IsSpinning},{(entry.Driver?.TargetSpeed ?? 0):F3},{(entry.Driver?.RivalIndex ?? -1)},{inputs[Mathf.Min(entry.Index,1)].IsConnected},{gate?.NeedsPedalRelease??false},{(entry.IsCpu?entry.Driver.RecoveryCount:entry.Car.GetComponent<CarResetter>().RecoveryCount)},{entry.Driver?.RecoveryBlockedReason??""}"));
            }
        }
        if (manager.GetParticipant(0).IsRacing && manager.GetParticipant(1).IsRacing &&
            Mathf.Abs(manager.GetParticipant(0).Progress - manager.GetParticipant(1).Progress) > 80)
        {
            run.humanSeparationSeconds += dt;
            var rear = manager.GetParticipant(manager.GetParticipant(0).Progress < manager.GetParticipant(1).Progress ? 0 : 1);
            float nearest = float.PositiveInfinity;
            for (int c = 2; c < manager.Participants.Count; c++) if (manager.GetParticipant(c).IsRacing)
                nearest = Mathf.Min(nearest, Mathf.Abs(manager.GetParticipant(c).Progress - rear.Progress));
            if (nearest < 60) run.cpuNearRearHumanSeconds += dt;
        }
    }

    private DriveInputState Drive(RaceParticipant entry, int grounded, float speed, float raw, bool reset)
    {
        float sign = manager.CourseProgressSign;
        manager.course.TryGetSampleAtProgress(raw + sign * Mathf.Clamp(5 + speed * .4f, 8, 17), out RaceCourse.CourseSample target);
        manager.course.TryGetSampleAtProgress(raw + sign * 25, out RaceCourse.CourseSample far);
        float requested = entry.Index == 0 ? run.p1TargetSpeed : run.p2TargetSpeed;
        if (Vector3.Angle(target.forward, far.forward) > 15) requested = Mathf.Min(requested, 14);
        bool jumpAhead = false;
        for (int d = 0; d <= 30; d += 5) if (!manager.course.HasRoadAtProgress(raw + sign * d)) jumpAhead = true;
        string profile = entry.Index == 0 ? run.p1Profile : run.p2Profile;
        bool anticipatesJump = !skillSuite || profile == "expert" || profile == "intermediate";
        if (jumpAhead && anticipatesJump) requested = 24;
        if (skillSuite && (profile=="novice" || profile=="cautious"))
        {
            bool warning=driverUis[entry.Index]?.DriverNotice.Contains("ジャンプ")==true;
            if(warning && jumpNoticedAt[entry.Index]<0)
            { jumpNoticedAt[entry.Index]=run.physicsSeconds; Event(entry.Index,"jump-notice-seen",""); }
            if(!warning)jumpNoticedAt[entry.Index]=-1;
            if(warning && run.physicsSeconds-jumpNoticedAt[entry.Index]>=.35f)requested=24;
        }
        if (run.name == "skill-stop-rejoin" && entry.Index == 1 && run.physicsSeconds >= 45 && run.physicsSeconds < 57) requested = 0;
        if (run.name == "temporary-stop-p2" && entry.Index == 1 && manager.time >= 45 && manager.time < 60) requested = 0;
        float lane = jumpAhead ? 0 : entry.Index == 0 ? 2 : -2;
        Vector3 aim = Quaternion.Inverse(entry.Body.rotation) * (target.position + target.right * lane - entry.Body.position);
        float steering = Mathf.Clamp(Mathf.Atan2(aim.x, aim.z) * Mathf.Rad2Deg * 1.5f - entry.Body.angularVelocity.y * 8, -30, 30);
        if (skillSuite)
        {
            float reaction = profile == "expert" ? .06f : profile == "intermediate" ? .14f : profile == "aggressive" ? .18f : .35f;
            if (run.physicsSeconds >= nextDecisionAt[entry.Index])
            {
                nextDecisionAt[entry.Index] = run.physicsSeconds + reaction;
                float noise = profile == "novice" ? 3f : profile == "aggressive" ? 4f : profile == "cautious" ? 1.5f : .3f;
                if (profile == "aggressive") steering *= 1.25f;
                steering += Mathf.Sin(run.physicsSeconds * 2.7f + entry.Index) * noise;
                heldDecision[entry.Index] = new DriveInputState {
                    pedal = grounded == 0 ? 0 : Mathf.Clamp((requested - speed) * .12f, -1, 1),
                    steering = grounded == 0 ? 0 : Mathf.Clamp(steering, -30, 30) };
            }
            DriveInputState decision = heldDecision[entry.Index]; decision.resetPressed = reset; return decision;
        }
        return new DriveInputState { pedal = grounded == 0 ? 0 : Mathf.Clamp((requested - speed) * .12f, -1, 1),
            steering = grounded == 0 ? 0 : steering, resetPressed = reset };
    }
    public void Contact(int index, Collision collision)
    {
        if (!driving || !manager.GetParticipant(index).IsRacing) return;
        if (collision.rigidbody != null && manager.FindParticipant(collision.rigidbody) != null)
        { run.cars[index].vehicleContacts++; Event(index, "vehicle-contact", collision.gameObject.name); }
        else { run.cars[index].environmentContacts++; Event(index, "environment-contact", collision.gameObject.name); }
    }
    private void Event(int index, string kind, string detail)
    {
        if (events == null || manager.GetParticipant(index)?.Body == null) return;
        Vector3 p = manager.GetParticipant(index).Body.position;
        detail = "\"" + detail.Replace("\"", "\"\"").Replace("\n", " ").Replace("\r", " ") + "\"";
        events.WriteLine(FormattableString.Invariant($"{run.physicsSeconds:F2},{manager.GetParticipant(index).Label},{kind},{detail},{p.x:F3},{p.y:F3},{p.z:F3}"));
        events.Flush();
    }
    private void Save() => File.WriteAllText(Path.Combine(output, "report.json"), JsonUtility.ToJson(suite, true));
    private void LogError(string message, string stack, LogType type)
    {
        if ((type == LogType.Exception || type == LogType.Error || type == LogType.Assert) && suite.runtimeErrors.Count < 50)
        { suite.runtimeErrors.Add(message + "\n" + stack); Save(); }
    }
    private void OnDestroy()
    {
        Application.logMessageReceived -= LogError;
        trace?.Dispose(); events?.Dispose();
        Time.timeScale = previousScale; Time.maximumDeltaTime = previousMaximumDelta;
        QualitySettings.vSyncCount = previousVsync; Application.targetFrameRate = previousFrameRate;
        Application.runInBackground = previousRunInBackground;
    }
}
public sealed class BalancePlaytestContact : MonoBehaviour
{
    public BalancePlaytestAgent Owner;
    public int Index;
    private void OnCollisionEnter(Collision collision) => Owner?.Contact(Index, collision);
}
#endif
