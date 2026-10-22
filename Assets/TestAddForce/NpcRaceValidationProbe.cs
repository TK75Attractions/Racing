#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

/// <summary>Four actual vehicles in SampleScene, two test human inputs and two production CPU drivers.</summary>
[DefaultExecutionOrder(-200)]
public sealed class NpcRaceValidationProbe : MonoBehaviour
{
    private sealed class Input : IDriveInputSource
    {
        public int PlayerIndex { get; set; }
        public string DeviceId => "npc-validation-human";
        public bool IsConnected => true;
        public DriveInputState CurrentState { get; set; }
        public void UpdateInput(float dt) { }
        public void Dispose() { }
    }
    [Serializable] private sealed class Trial
    {
        public string name;
        public int laps, checkpoints, contacts, recoveries;
        public float progress, maxStall, maxTilt;
        public bool jumped, landed, finished;
        [NonSerialized] public float stall;
        [NonSerialized] public TireForce[] tires;
    }
    [Serializable] private sealed class Report
    {
        public string mode="full-race";
        public bool success, countdownStopped, cpuFinishKeptHumansRacing, resultsContainFour, retryCreatesNewCpus;
        public float simulatedSeconds;
        public int cpuOvertakes, humanOvertakes;
        public float splitSeconds, rearPairedSeconds;
        public bool occupiedRecoveryRejected, visibleRecoveryRejected, hiddenRecoverySucceeded, checkpointStatePreserved, droveAfterRecovery;
        public bool automaticRecoverySucceeded, humanContactPreserved, humanContactSeparated;
        public bool repeatedRecoveryMovedBack;
        public string automaticRecoveryDetails;
        public List<Trial> cars = new List<Trial>();
        public string error;
    }
    private Gmanager manager;
    private LapManager laps;
    private readonly Input[] human = { new Input { PlayerIndex = 0 }, new Input { PlayerIndex = 1 } };
    private Report report = new Report();
    private bool driving;
    private readonly int[] previousPairOrder = new int[4];
    private string output;
    private StreamWriter trace;
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private IEnumerator Start()
    {
        output = Environment.GetEnvironmentVariable("RACING_NPC_VALIDATION_OUTPUT") ?? Path.Combine(Path.GetTempPath(), "racing-npc-validation");
        Directory.CreateDirectory(output);
        trace = new StreamWriter(Path.Combine(output, "trace.csv")); trace.WriteLine("time,car,x,y,z,speed,progress,lap,checkpoint,pedal,steering");
        Application.runInBackground = true; Time.timeScale = 3;
        manager = Gmanager.Control; laps = FindFirstObjectByType<LapManager>();
        foreach (RaceRemoteFirebasePublisher publisher in FindObjectsByType<RaceRemoteFirebasePublisher>(FindObjectsSortMode.None)) publisher.enabled = false;
        // Isolate normal driving from randomly drawn human rockets/confuse/oil.
        foreach (RaceItemPickup pickup in FindObjectsByType<RaceItemPickup>(FindObjectsSortMode.None)) pickup.gameObject.SetActive(false);
        if (Environment.GetEnvironmentVariable("RACING_NPC_RECOVERY_ONLY") == "1")
        {
            report.mode="recovery-only";
            typeof(Gmanager).GetMethod("StartGameWhenScreenCovered", Private).Invoke(manager, null);
            yield return ValidateRecovery();
            report.success=report.occupiedRecoveryRejected && report.visibleRecoveryRejected && report.hiddenRecoverySucceeded && report.checkpointStatePreserved && report.droveAfterRecovery && report.automaticRecoverySucceeded && report.humanContactPreserved && report.humanContactSeparated && report.repeatedRecoveryMovedBack;
            FinishReport(); yield break;
        }
        typeof(LapManager).GetField("goalLap", Private).SetValue(laps, 2);
        typeof(Gmanager).GetMethod("StartGameWhenScreenCovered", Private).Invoke(manager, null);
        typeof(Gmanager).GetMethod("CompleteGameStart", Private).Invoke(manager, null);
        Vector3 first = manager.GetParticipant(2).Body.position;
        yield return new WaitForSeconds(.8f);
        report.countdownStopped = Vector3.Distance(first, manager.GetParticipant(2).Body.position) < .001f;
        if (!report.countdownStopped) report.error = "CPU moved during countdown";
        while (manager.state != Gmanager.State.Game) yield return null;
        foreach (RaceParticipant entry in manager.Participants)
        {
            Trial trial = new Trial { name = entry.Label, tires = entry.Car.GetComponentsInChildren<TireForce>() };
            report.cars.Add(trial);
            if (!entry.IsCpu) entry.Car.GetComponent<DebugMover>().SetInputSource(human[entry.Index]);
            entry.Car.AddComponent<NpcValidationContact>().Owner = this;
        }
        driving = true;
        while (manager.state == Gmanager.State.Game && manager.time < 290 && report.error == null) yield return null;
        driving = false;
        foreach (RaceParticipant entry in manager.Participants)
        {
            Trial t = report.cars[entry.Index]; t.laps = laps.GetCarData(entry.Body).lapCount; t.progress = laps.GetRaceProgressDistance(entry.Body); t.finished = entry.Result?.didFinish == true; t.recoveries = entry.Driver?.RecoveryCount ?? 0;
            if (entry.IsCpu && (t.laps < 1 || !t.jumped || !t.landed)) report.error = entry.Label + " failed lap/jump/landing";
        }
        report.simulatedSeconds = manager.time;
        report.resultsContainFour = manager.LatestSessionResult?.ParticipantCount == 4;
        int count = 0; foreach (RaceResultRecord r in manager.LatestSessionResult?.playerResults ?? new RaceResultRecord[0]) if (r != null) count++;
        report.resultsContainFour &= count == 4;
        GameObject oldCpu = manager.GetParticipant(2).Car;
        typeof(Gmanager).GetMethod("ClearCurrentRaceObjects", Private).Invoke(manager, null);
        typeof(Gmanager).GetMethod("StartGameWhenScreenCovered", Private).Invoke(manager, null);
        report.retryCreatesNewCpus = manager.GetParticipant(2).Car != oldCpu && manager.Participants.Count == 4;
        yield return ValidateRecovery();
        report.success = report.error == null && report.countdownStopped && report.resultsContainFour && report.retryCreatesNewCpus && report.cpuFinishKeptHumansRacing && report.cpuOvertakes > 0 && report.humanOvertakes > 0 &&
            report.occupiedRecoveryRejected && report.visibleRecoveryRejected && report.hiddenRecoverySucceeded && report.checkpointStatePreserved && report.droveAfterRecovery && report.automaticRecoverySucceeded && report.humanContactPreserved && report.humanContactSeparated && report.repeatedRecoveryMovedBack;
        FinishReport();
    }
    private void FinishReport()
    {
        File.WriteAllText(Path.Combine(output, "report.json"), JsonUtility.ToJson(report,true)); trace.Dispose(); trace = null;
        string successMessage=report.mode=="recovery-only"
            ? "NPC_RECOVERY_PASS: occupied/visible guards, backward retry, preserved laps, driving, automatic recovery and contact-grace expiry"
            : "NPC_RACE_PASS: four actual cars, CPU laps/jump/landing, overtakes, human finish policy, results, retry and guarded recovery";
        Type.GetType("NpcRaceValidation, Assembly-CSharp-Editor").GetMethod("Finish").Invoke(null,new object[]{report.success, report.success ? successMessage : JsonUtility.ToJson(report)});
    }
    private void FixedUpdate()
    {
        if (!driving) return;
        foreach (RaceParticipant entry in manager.Participants)
        {
            Trial trial = report.cars[entry.Index];
            Rigidbody body = entry.Body;
            LapManager.CarTimeData data = laps.GetCarData(body);
            trial.progress = laps.GetRaceProgressDistance(body); trial.laps = data.lapCount; trial.checkpoints = data.lastCheckpointIndex;
            float speed = Vector3.ProjectOnPlane(body.linearVelocity,Vector3.up).magnitude;
            int grounded=0; foreach (TireForce tire in trial.tires) if(tire.IsGrounded) grounded++;
            float raw=manager.course.GetProgressDistance(body.position), sign=manager.CourseProgressSign;
            bool gap = !manager.course.HasRoadAtProgress(raw);
            if (entry.IsCpu && gap && grounded==0 && body.position.y>10) trial.jumped=true;
            if(trial.jumped && !gap && grounded>=2) trial.landed=true;
            trial.maxTilt=Mathf.Max(trial.maxTilt,Vector3.Angle(body.rotation*Vector3.up,Vector3.up));
            trial.stall = entry.IsRacing && speed<.5f && manager.time>6 ? trial.stall+Time.fixedDeltaTime:0;
            trial.maxStall=Mathf.Max(trial.maxStall,trial.stall);
            if(trial.stall>20) report.error=entry.Label+" stalled for 20s";
            if(entry.IsCpu && entry.Result?.didFinish==true && (manager.GetParticipant(0).IsRacing || manager.GetParticipant(1).IsRacing))
                report.cpuFinishKeptHumansRacing=true;
            if (!entry.IsCpu && entry.IsRacing)
            {
                manager.course.TryGetSampleAtProgress(raw+sign*Mathf.Clamp(5+speed*.4f,8,17),out RaceCourse.CourseSample target);
                manager.course.TryGetSampleAtProgress(raw+sign*25,out RaceCourse.CourseSample far);
                float targetSpeed=Vector3.Angle(target.forward,far.forward)>15 ? 14 : (entry.Index==0 ? 25:21);
                bool jumpAhead=false; for(int d=0;d<=30;d+=5) if(!manager.course.HasRoadAtProgress(raw+sign*d)) jumpAhead=true;
                if(jumpAhead)targetSpeed=24;
                Vector3 aim=Quaternion.Inverse(body.rotation)*(target.position+target.right*(entry.Index==0?2:-2)-body.position);
                human[entry.Index].CurrentState=new DriveInputState { pedal=grounded==0?0:Mathf.Clamp((targetSpeed-speed)*.12f,-1,1), steering=grounded==0?0:Mathf.Clamp(Mathf.Atan2(aim.x,aim.z)*Mathf.Rad2Deg*1.5f-body.angularVelocity.y*8,-30,30)};
            }
            DriveInputState input = entry.IsCpu?entry.Driver.CurrentState:human[entry.Index].CurrentState;
            trace.WriteLine(FormattableString.Invariant($"{manager.time:F2},{entry.Label},{body.position.x:F3},{body.position.y:F3},{body.position.z:F3},{speed:F2},{trial.progress:F2},{trial.laps},{trial.checkpoints},{input.pedal:F2},{input.steering:F2}"));
        }
        for(int c=2;c<4;c++) for(int h=0;h<2;h++)
        {
            RaceParticipant cpu=manager.GetParticipant(c), player=manager.GetParticipant(h);
            float difference=report.cars[c].progress-report.cars[h].progress;
            int order=difference>2?1:difference < -2?-1:0, pair=(c-2)*2+h;
            if(order!=0)
            {
                if(previousPairOrder[pair]!=0 && previousPairOrder[pair]!=order && Vector3.Distance(cpu.Body.position,player.Body.position)<30)
                { if(order>0)report.cpuOvertakes++; else report.humanOvertakes++; }
                previousPairOrder[pair]=order;
            }
        }
        if(manager.GetParticipant(0).IsRacing && manager.GetParticipant(1).IsRacing && Mathf.Abs(report.cars[0].progress-report.cars[1].progress)>80)
        {
            report.splitSeconds+=Time.fixedDeltaTime;
            int rear=report.cars[0].progress<report.cars[1].progress?0:1;
            float nearest=Mathf.Min(Mathf.Abs(report.cars[2].progress-report.cars[rear].progress),Mathf.Abs(report.cars[3].progress-report.cars[rear].progress));
            if(nearest<60)report.rearPairedSeconds+=Time.fixedDeltaTime;
        }
    }
    private IEnumerator ValidateRecovery()
    {
        typeof(Gmanager).GetMethod("CompleteGameStart", Private).Invoke(manager, null);
        typeof(Gmanager).GetMethod("StartRaceAfterCountdown", Private).Invoke(manager, null);
        float gridRaw=manager.course.GetProgressDistance(manager.GetParticipant(0).Body.position);
        manager.course.TryGetSampleAtProgress(gridRaw+manager.CourseProgressSign*150,out RaceCourse.CourseSample parked);
        for(int i=0;i<2;i++)
        {
            RaceParticipant player=manager.GetParticipant(i);
            player.Body.isKinematic=true; player.Car.GetComponent<DebugMover>().enabled=false;
            player.Body.position=parked.position+Vector3.up*1.2f+parked.right*(i==0?-2.5f:2.5f);
        }
        yield return new WaitForSeconds(15f);
        RaceParticipant cpu = manager.GetParticipant(2), otherCpu = manager.GetParticipant(3);
        NpcRaceDriver driver = cpu.Driver;
        bool saved = (bool)typeof(NpcRaceDriver).GetField("hasSafeState", Private).GetValue(driver);
        if (!saved) { report.error = "CPU never saved a grounded recovery point"; yield break; }
        float raw = (float)typeof(NpcRaceDriver).GetField("safeRawProgress", Private).GetValue(driver);
        LapManager.NpcRecoveryState state = (LapManager.NpcRecoveryState)typeof(NpcRaceDriver).GetField("safeState", Private).GetValue(driver);
        manager.course.TryGetSampleAtProgress(raw, out RaceCourse.CourseSample safe);
        foreach (RaceParticipant entry in manager.Participants)
        {
            if(entry==cpu)continue;
            entry.Driver?.SetDriving(false); entry.Body.isKinematic=true;
            entry.Body.position = new Vector3(2500+entry.Index*20,100,2500);
        }
        yield return new WaitForSeconds(1f);
        raw = (float)typeof(NpcRaceDriver).GetField("safeRawProgress", Private).GetValue(driver);
        state = (LapManager.NpcRecoveryState)typeof(NpcRaceDriver).GetField("safeState", Private).GetValue(driver);
        manager.course.TryGetSampleAtProgress(raw, out safe);
        // Explicit failure injections and camera poses belong only to this test harness.
        cpu.Body.position = safe.position - Vector3.up*20; cpu.Body.linearVelocity=Vector3.zero;
        Vector3 fallen = cpu.Body.position;
        MethodInfo recover = typeof(NpcRaceDriver).GetMethod("TryRecover", Private);
        int before = driver.RecoveryCount;
        for(int i=0;i<2;i++) manager.GetPlayerViewCamera(i).transform.SetPositionAndRotation(new Vector3(2500,100,2500),Quaternion.identity);
        otherCpu.Body.position = safe.position + Vector3.up*1.2f;
        // Recovery now considers side lanes too. Occupy all candidates for this rejection case.
        GameObject blockedRoad=new GameObject("Recovery lanes occupied",typeof(Rigidbody),typeof(BoxCollider));
        blockedRoad.GetComponent<Rigidbody>().isKinematic=true;
        blockedRoad.GetComponent<BoxCollider>().size=new Vector3(40,4,20);
        blockedRoad.transform.SetPositionAndRotation(safe.position+safe.up,Quaternion.LookRotation(safe.forward,safe.up));
        Physics.SyncTransforms();
        recover.Invoke(driver,null);
        report.occupiedRecoveryRejected = driver.RecoveryCount==before && Vector3.Distance(cpu.Body.position,fallen)<.01f;
        blockedRoad.SetActive(false); Destroy(blockedRoad); Physics.SyncTransforms();
        otherCpu.Body.position = new Vector3(2600,100,2500);
        Physics.SyncTransforms();
        Camera viewed = manager.GetPlayerViewCamera(0);
        Vector3 cameraPosition=safe.position+Vector3.up*3-Vector3.forward*20;
        viewed.transform.SetPositionAndRotation(cameraPosition,Quaternion.LookRotation(safe.position-cameraPosition));
        recover.Invoke(driver,null);
        report.visibleRecoveryRejected = driver.RecoveryCount==before && Vector3.Distance(cpu.Body.position,fallen)<.01f;
        for(int i=0;i<2;i++) manager.GetPlayerViewCamera(i).transform.SetPositionAndRotation(new Vector3(2500,100,2500),Quaternion.identity);
        recover.Invoke(driver,null);
        report.hiddenRecoverySucceeded = driver.RecoveryCount==before+1 && Vector3.Distance(cpu.Body.position,safe.position)<2;
        LapManager.CarTimeData data = laps.GetCarData(cpu.Body);
        report.checkpointStatePreserved = data.lapCount==state.lap && data.nextCheckpointIndex==state.nextCheckpoint &&
            data.passedCheckpointCount==state.passedCount && laps.GetRaceProgressDistance(cpu.Body)<=state.progress+1;
        float firstRecoveryProgress=laps.GetRaceProgressDistance(cpu.Body);
        cpu.Body.position=safe.position-Vector3.up*20; cpu.Body.linearVelocity=Vector3.zero;
        recover.Invoke(driver,null);
        report.repeatedRecoveryMovedBack=driver.RecoveryCount==before+2 && driver.LastRecoveryRollbackMeters>0 &&
            laps.GetRaceProgressDistance(cpu.Body)<firstRecoveryProgress-1f && data.lapCount==state.lap;
        float recoveredProgress = laps.GetRaceProgressDistance(cpu.Body);
        manager.course.TryGetSampleAtProgress(raw+manager.CourseProgressSign*150, out RaceCourse.CourseSample ahead);
        manager.GetParticipant(0).Body.position=ahead.position+Vector3.up;
        yield return new WaitForSeconds(10f);
        report.droveAfterRecovery = laps.GetRaceProgressDistance(cpu.Body)>recoveredProgress+15 && cpu.Body.position.y>-35;
        before=driver.RecoveryCount;
        cpu.Body.position-=Vector3.up*20; cpu.Body.linearVelocity=Vector3.zero;
        yield return new WaitForSeconds(3.5f);
        report.automaticRecoverySucceeded=driver.RecoveryCount==before+1 && cpu.Body.position.y>-35;
        float savedRawNow=(float)typeof(NpcRaceDriver).GetField("safeRawProgress",Private).GetValue(driver);
        manager.course.TryGetSampleAtProgress(savedRawNow,out RaceCourse.CourseSample savedNow);
        report.automaticRecoveryDetails="count="+before+" -> "+driver.RecoveryCount+"; position="+cpu.Body.position+"; sourceVisible="+manager.IsNpcRecoveryVisible(cpu.Body.position)+"; destinationVisible="+manager.IsNpcRecoveryVisible(savedNow.position);
        RaceParticipant humanCar=manager.GetParticipant(0);
        humanCar.Body.position=cpu.Body.position+cpu.Body.rotation*Vector3.forward*2;
        humanCar.Body.rotation=cpu.Body.rotation;
        yield return new WaitForSeconds(.3f);
        Collider ownSolid=Array.Find(cpu.Car.GetComponentsInChildren<Collider>(),c=>!c.isTrigger && c.attachedRigidbody==cpu.Body);
        Collider humanSolid=Array.Find(humanCar.Car.GetComponentsInChildren<Collider>(),c=>!c.isTrigger && c.attachedRigidbody==humanCar.Body);
        report.humanContactPreserved=!Physics.GetIgnoreCollision(ownSolid,humanSolid);
        // Contact stays enabled both during contact and after separation.
        humanCar.Body.position=new Vector3(2600,100,2700); Physics.SyncTransforms();
        yield return new WaitForSeconds(3f);
        report.humanContactSeparated=!Physics.GetIgnoreCollision(ownSolid,humanSolid);
    }

    public void Contact(GameObject vehicle,Collision collision)
    {
        if(!driving || collision.rigidbody==null)return;
        RaceParticipant entry=manager.FindParticipant(vehicle.GetComponent<Rigidbody>());
        if(entry!=null)report.cars[entry.Index].contacts++;
    }
    private void OnDestroy() => trace?.Dispose();
}
public sealed class NpcValidationContact : MonoBehaviour
{
    public NpcRaceValidationProbe Owner;
    private void OnCollisionEnter(Collision collision)=>Owner?.Contact(gameObject,collision);
}
#endif
