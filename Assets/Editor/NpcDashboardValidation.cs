using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Checks the actual bridge payload, including remote byte limits and race lifecycle.</summary>
public static class NpcDashboardValidation
{
    [Serializable] private sealed class Snapshot
    {
        public string state;
        public int participantCount;
        public Participant[] participants;
        public Player[] players;
        public Serial serial;
        public Preparation[] playerProgress;
    }
    [Serializable] private sealed class Preparation { public int number; public string state, carName, step; public float remaining, progress; }
    [Serializable] private sealed class Participant
    {
        public int number, position, lap, rivalNumber, recoveries;
        public bool isCpu, contactGrace;
        public float speed, progress, targetSpeed, gap;
        public string status;
    }
    [Serializable] private sealed class Player { public int number; }
    [Serializable] private sealed class Serial { public Line[] lines; }
    [Serializable] private sealed class Line { public int id; public string status, line; }
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    [MenuItem("Racing/Validate NPC Dashboard")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        Gmanager manager = UnityEngine.Object.FindFirstObjectByType<Gmanager>();
        var entries = (List<RaceParticipant>)typeof(Gmanager).GetField("participants", Private).GetValue(manager);
        GameObject root = new GameObject("NPC dashboard validation");
        RaceDashboardBridge bridge = root.AddComponent<RaceDashboardBridge>();
        Set(bridge, "race", manager);
        try
        {
            for (int i = 0; i < 4; i++)
            {
                GameObject car = new GameObject("Dashboard entry " + i); car.transform.SetParent(root.transform);
                Rigidbody body = car.AddComponent<Rigidbody>(); body.useGravity = false;
                body.linearVelocity = Vector3.forward * 20f;
                RaceParticipant entry = new RaceParticipant { Index=i, IsCpu=i>=2, Car=car, Body=body, Position=i+1, Progress=100+i*10 };
                if (entry.IsCpu)
                {
                    entry.Driver = car.AddComponent<NpcRaceDriver>();
                    Set(entry.Driver, "driving", true); Set(entry.Driver, "groundedTires", 4);
                    Set(entry.Driver, "rivalIndex", i-2);
                    Set(entry.Driver, "<RecoveryCount>k__BackingField", 2);
                }
                entries.Add(entry);
            }
            manager.state = Gmanager.State.Countdown;
            Snapshot snapshot = Read(bridge);
            Require(snapshot.participantCount==4 && snapshot.players.Length==2, "Participant/input counts changed");
            Require(snapshot.participants[2].status=="Stopped" && snapshot.participants[2].targetSpeed==0, "Countdown did not stop CPU telemetry");
            manager.state = Gmanager.State.Game;
            snapshot = Read(bridge);
            Require(snapshot.participants[2].isCpu && snapshot.participants[2].number==3 && snapshot.participants[2].position==3 &&
                snapshot.participants[2].rivalNumber==1 && snapshot.participants[2].gap==-20 && snapshot.participants[2].speed==72 &&
                snapshot.participants[2].targetSpeed==90 && snapshot.participants[2].recoveries==2, "CPU driving diagnostics mismatch");
            NpcRaceDriver driver = entries[2].Driver;
            Set(driver,"groundedTires",0); Require(Read(bridge).participants[2].status=="Airborne", "Airborne not reported");
            Set(driver,"groundedTires",4); Set(driver,"reverseUntil",Time.time+10); Require(Read(bridge).participants[2].status=="Reversing", "Reverse not reported");
            Set(driver,"reverseUntil",0f); Set(driver,"yieldUntil",Time.time+10); Require(Read(bridge).participants[2].status=="Yielding", "Yield not reported");
            Set(driver,"yieldUntil",0f); Set(driver,"stuck",1f); Require(Read(bridge).participants[2].status=="Stalled", "Stall not reported");
            entries[2].Result = new RaceResultRecord { didFinish=true, finishPosition=1, completedLaps=2 };
            entries[3].Result = new RaceResultRecord { didFinish=false, finishPosition=4, completedLaps=1 };
            snapshot = Read(bridge);
            Require(snapshot.state=="Game" && snapshot.participants[2].status=="Finished" && snapshot.participants[2].lap==2 &&
                snapshot.participants[2].position==1 && snapshot.participants[2].targetSpeed==0 && snapshot.participants[2].rivalNumber==0 &&
                snapshot.participants[3].status=="DNF", "CPU finish/DNF lifecycle mismatch");
            manager.state = Gmanager.State.Result;
            Require(Read(bridge).participants[0].status=="Stopped", "Race end did not stop unresolved entries");
            manager.state = Gmanager.State.Game;
            for (int i=0;i<40;i++) typeof(RaceDashboardBridge).GetMethod("RecordSerialLine",Private).Invoke(bridge,new object[]{"OK",new string('車',512)});
            string bounded = bridge.BuildRemoteSnapshotJson(new string('a',32),1);
            Require(bounded!=null && Encoding.UTF8.GetByteCount(bounded)<=4096 && JsonUtility.FromJson<Snapshot>(bounded).participants.Length==4,
                "CPU diagnostics were lost when trimming oversized Unicode serial logs");
            File.WriteAllText("/tmp/racing-npc-dashboard.json",bounded);
            // Alerts must survive more than the entire 80-line TEL window, including remote trimming.
            MethodInfo record = typeof(RaceDashboardBridge).GetMethod("RecordSerialLine", Private);
            record.Invoke(bridge, new object[] { "ERROR", SerialInputProtocol.Encode("ERR,100,0,ERROR,I2C,CONNECTION_LOST").TrimEnd('\n') });
            for (int i=0;i<200;i++) record.Invoke(bridge, new object[] { "OK", SerialInputProtocol.Encode("TEL,"+(101+i)+",0,0,1,0,0,0,1").TrimEnd('\n') });
            Snapshot retained = Read(bridge);
            Require(Array.Exists(retained.serial.lines, line => line.status=="ERROR" && line.line.Contains("CONNECTION_LOST")),
                "Remote high-rate telemetry erased the last error");
            Require(!Array.Exists(retained.serial.lines, line => line.line.StartsWith("@TEL,")), "TEL entered non-telemetry history");
            object local = typeof(RaceDashboardBridge).GetMethod("BuildSnapshot", Private).Invoke(bridge, null);
            Snapshot localSnapshot = JsonUtility.FromJson<Snapshot>(JsonUtility.ToJson(local));
            Require(localSnapshot.serial.lines.Length<=80 && Array.Exists(localSnapshot.serial.lines, line => line.status=="ERROR"),
                "Local alert retention failed or exceeded 80 lines");
            for (int i=0;i<12;i++) record.Invoke(bridge,new object[]{"WARN","retained warning "+i});
            retained = Read(bridge);
            Require(Array.FindAll(retained.serial.lines, line => line.status=="WARN").Length==8 &&
                !Array.Exists(retained.serial.lines,line=>line.status=="ERROR"), "Alert history did not retain exactly the newest eight alerts");
            File.WriteAllText("/tmp/racing-serial-dashboard-alerts.json",bridge.BuildRemoteSnapshotJson(new string('a',32),3));
            Set(driver,"stuck",0f); entries[2].Result=null; entries[3].Result=null;
            File.WriteAllText("/tmp/racing-npc-dashboard-driving.json",bridge.BuildRemoteSnapshotJson(new string('a',32),2));
            entries.Clear(); manager.state=Gmanager.State.Title;
            Require(Read(bridge).participantCount==0, "Previous round telemetry survived reset");
            ValidatePlayerProgress(manager, bridge, root);
            Debug.Log("NPC_DASHBOARD_PASS: four entries/two inputs, countdown, driving, air/reverse/yield/stall, finish/DNF, reset, Unicode 4 KiB limit, local/remote serial alert retention");
        }
        finally { entries.Clear(); UnityEngine.Object.DestroyImmediate(root); }
    }
    private static void ValidatePlayerProgress(Gmanager manager, RaceDashboardBridge bridge, GameObject root)
    {
        Array players = (Array)typeof(Gmanager).GetField("players", Private).GetValue(manager);
        object[] previous = { players.GetValue(0), players.GetValue(1) };
        Type runtime = players.GetType().GetElementType();
        var tutorial = root.AddComponent<DrivingTutorialController>();
        object oldTutorial = typeof(Gmanager).GetField("drivingTutorial", Private).GetValue(manager);
        Gmanager.State oldState = manager.state;
        try
        {
            for (int i = 0; i < 2; i++) players.SetValue(Activator.CreateInstance(runtime, true), i);
            runtime.GetField("preparationState").SetValue(players.GetValue(0), Gmanager.State.Tutorial);
            runtime.GetField("preparationState").SetValue(players.GetValue(1), Gmanager.State.CarSelection);
            var selection = root.AddComponent<CarSelectionUI>();
            Set(selection, "catalog", CarSelectionCatalog.Load());
            Set(selection, "countdown", new PreparationCountdown(12.5f));
            runtime.GetField("carSelection").SetValue(players.GetValue(1), selection);
            var practices = (System.Collections.IList)typeof(DrivingTutorialController).GetField("players", Private).GetValue(tutorial);
            object practice = Activator.CreateInstance(practices.GetType().GetGenericArguments()[0], true);
            var lesson = new DrivingTutorialProgress(60f);
            lesson.Tick(0f, true, 0f, 0f, 0f, 0f); lesson.Tick(.61f, true, .5f, 0f, 4f, 0f);
            practice.GetType().GetField("playerIndex").SetValue(practice, 0);
            practice.GetType().GetField("lesson").SetValue(practice, lesson); practices.Add(practice);
            Set(manager, "drivingTutorial", tutorial); manager.state = Gmanager.State.Tutorial;
            Func<Snapshot> local = () => JsonUtility.FromJson<Snapshot>(JsonUtility.ToJson(typeof(RaceDashboardBridge).GetMethod("BuildSnapshot", Private).Invoke(bridge,null)));
            Snapshot snapshot = local();
            Require(snapshot.playerProgress[0].state == "Tutorial" && snapshot.playerProgress[0].step == "Steer" &&
                snapshot.playerProgress[1].state == "CarSelection" && snapshot.playerProgress[1].step == "Vehicle" &&
                snapshot.playerProgress[1].remaining == 12.5f && snapshot.playerProgress[1].carName.Length > 0,
                "Per-player tutorial and vehicle-selection progress did not remain independent");
            lesson.Tick(60f, false, 0f, 0f, 0f, 0f);
            Require(local().playerProgress[0].step == "TimedOut", "Tutorial timeout was reported as completion");
            manager.state = Gmanager.State.Result;
            runtime.GetField("returnedToTitle").SetValue(players.GetValue(0), true);
            Require(local().playerProgress[0].step == "Returned" && local().playerProgress[1].state == "Result",
                "Independent return-to-title status was lost");
            Debug.Log("DASHBOARD_PLAYER_PROGRESS_PASS: mixed selection/tutorial, car name, time remaining, tutorial timeout and independent result return");
        }
        finally
        {
            players.SetValue(previous[0],0); players.SetValue(previous[1],1);
            Set(manager,"drivingTutorial",oldTutorial); manager.state = oldState;
        }
    }
    private static Snapshot Read(RaceDashboardBridge bridge) => JsonUtility.FromJson<Snapshot>(bridge.BuildRemoteSnapshotJson(new string('a',32),1));
}
