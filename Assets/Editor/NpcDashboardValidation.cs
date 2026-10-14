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
    }
    [Serializable] private sealed class Participant
    {
        public int number, position, lap, rivalNumber, recoveries;
        public bool isCpu, contactGrace;
        public float speed, progress, targetSpeed, gap;
        public string status;
    }
    [Serializable] private sealed class Player { public int number; }
    [Serializable] private sealed class Serial { public Line[] lines; }
    [Serializable] private sealed class Line { public string line; }
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
            Set(driver,"stuck",0f); entries[2].Result=null; entries[3].Result=null;
            File.WriteAllText("/tmp/racing-npc-dashboard-driving.json",bridge.BuildRemoteSnapshotJson(new string('a',32),2));
            entries.Clear(); manager.state=Gmanager.State.Title;
            Require(Read(bridge).participantCount==0, "Previous round telemetry survived reset");
            Debug.Log("NPC_DASHBOARD_PASS: four entries/two inputs, countdown, driving, air/reverse/yield/stall, finish/DNF, reset and Unicode 4 KiB limit");
        }
        finally { entries.Clear(); UnityEngine.Object.DestroyImmediate(root); }
    }
    private static Snapshot Read(RaceDashboardBridge bridge) => JsonUtility.FromJson<Snapshot>(bridge.BuildRemoteSnapshotJson(new string('a',32),1));
}
